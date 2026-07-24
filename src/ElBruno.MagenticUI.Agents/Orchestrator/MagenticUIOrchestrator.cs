using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ElBruno.MagenticUI.Agents.Agents;
using ElBruno.MagenticUI.Agents.Models;
using ElBruno.MagenticUI.Agents.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ElBruno.MagenticUI.Agents.Orchestrator;

public sealed class MagenticUIOrchestrator
{
    private readonly IChatClient _orchestratorClient;
    private readonly FileSurferTool _fileSurfer;
    private readonly WebFetchTool _webFetcher;
    private readonly CodeExecutorTool _coder;
    private readonly UserProxyAgent _userProxy;
    private readonly int _maxRounds;
    private readonly ILogger<MagenticUIOrchestrator> _logger;

    private IProgress<AgentMessage>? _currentProgress;
    private CancellationToken _currentCt;

    public MagenticUIOrchestrator(
        IChatClient orchestratorClient,
        FileSurferTool fileSurfer,
        WebFetchTool webFetcher,
        CodeExecutorTool coder,
        UserProxyAgent userProxy,
        int maxRounds = 15,
        ILogger<MagenticUIOrchestrator>? logger = null)
    {
        _orchestratorClient = orchestratorClient;
        _fileSurfer = fileSurfer;
        _webFetcher = webFetcher;
        _coder = coder;
        _userProxy = userProxy;
        _maxRounds = maxRounds;
        _logger = logger ?? NullLogger<MagenticUIOrchestrator>.Instance;
    }

    public async Task RunAsync(
        TaskRequest request,
        IProgress<AgentMessage> progress,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Starting orchestration for task {TaskId}: {Prompt}", request.TaskId, request.Prompt);

        _currentProgress = progress;
        _currentCt = ct;

        var tools = BuildTools();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, BuildSystemPrompt(request)),
            new(ChatRole.User, request.Prompt)
        };

        Report(progress, "Orchestrator", "system",
            $"Task received: {request.Prompt}", round: 0);

        var submitted = false;

        for (int round = 1; round <= _maxRounds && !submitted && !ct.IsCancellationRequested; round++)
        {
            _logger.LogDebug("Orchestration round {Round}/{MaxRounds}", round, _maxRounds);

            ChatResponse response;
            try
            {
                response = await _orchestratorClient.GetResponseAsync(
                    messages,
                    new ChatOptions { Tools = tools },
                    ct);
            }
            catch (OperationCanceledException)
            {
                Report(progress, "Orchestrator", "system", "Task cancelled.", round);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LLM call failed in round {Round}", round);
                Report(progress, "Orchestrator", "system", $"LLM error: {ex.Message}", round);
                return;
            }

            if (!string.IsNullOrWhiteSpace(response.Text))
                Report(progress, "Orchestrator", "assistant", response.Text, round);

            var calls = response.Messages
                .SelectMany(message => message.Contents.OfType<FunctionCallContent>())
                .ToList();

            // Fallback for models (e.g. phi-3.5-mini via ONNX) that output JSON tool calls
            // in their text instead of native function-call tokens.
            var textBasedCall = false;
            if (calls.Count == 0 && !string.IsNullOrWhiteSpace(response.Text))
            {
                var textCall = TryParseTextToolCall(response.Text);
                if (textCall is not null)
                {
                    _logger.LogDebug("Text-based tool call parsed: {Name}", textCall.Name);
                    calls.Add(textCall);
                    textBasedCall = true;
                }
            }

            if (calls.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(response.Text))
                    submitted = true;

                break;
            }

            // For native function calls, add the response messages (with FunctionCallContent)
            // to the conversation history. For text-based calls the response is plain text —
            // we manage history manually below to keep it compatible with small models.
            if (!textBasedCall)
                messages.AddRange(response.Messages);

            var resultContents = new List<AIContent>();

            foreach (var call in calls)
            {
                ct.ThrowIfCancellationRequested();

                if (call.Name == "Submit")
                {
                    submitted = true;
                    var submitArg = call.Arguments?.TryGetValue("result", out var r) == true
                        ? r?.ToString() ?? string.Empty
                        : string.Empty;
                    if (!string.IsNullOrWhiteSpace(submitArg))
                        Report(progress, "Orchestrator", "assistant", submitArg, round);
                    resultContents.Add(new FunctionResultContent(call.CallId, "Submitted successfully."));
                    continue;
                }

                Report(progress, "Orchestrator", "tool",
                    $"Calling {call.Name}({FormatArgs(call.Arguments)})", round);

                var tool = tools.OfType<AIFunction>().FirstOrDefault(function => function.Name == call.Name);
                object? result;
                if (tool is not null)
                {
                    try
                    {
                        result = await tool.InvokeAsync(
                            call.Arguments is null
                                ? null
                                : new AIFunctionArguments(call.Arguments),
                            ct);
                    }
                    catch (Exception ex)
                    {
                        result = $"Tool error: {ex.Message}";
                        _logger.LogWarning(ex, "Tool '{Tool}' threw in round {Round}", call.Name, round);
                    }
                }
                else
                {
                    result = $"Unknown tool: {call.Name}";
                }

                var resultStr = result?.ToString() ?? string.Empty;
                Report(progress, DetermineAgentName(call.Name), "tool",
                    resultStr.Length > 2000 ? resultStr[..2000] + "...[truncated]" : resultStr, round);

                resultContents.Add(new FunctionResultContent(call.CallId, resultStr));
            }

            if (textBasedCall)
            {
                // Small models understand "user" messages with tool results better than
                // the native Tool role, so feed the result back as a user message.
                messages.Add(new ChatMessage(ChatRole.Assistant, response.Text!));
                var toolSummary = string.Join("\n\n", resultContents
                    .OfType<FunctionResultContent>()
                    .Select(r => $"Tool output:\n{r.Result}"));
                messages.Add(new ChatMessage(ChatRole.User, toolSummary));
            }
            else
            {
                messages.Add(new ChatMessage(ChatRole.Tool, resultContents));
            }
        }

        if (!submitted)
        {
            Report(progress, "Orchestrator", "system",
                $"Reached maximum rounds ({_maxRounds}) without a final answer.", _maxRounds);
        }
    }

    private List<AITool> BuildTools() =>
    [
        AIFunctionFactory.Create(_fileSurfer.ReadFile,
            name: "FileSurfer_ReadFile",
            description: "Reads a file from the sandboxed working directory."),
        AIFunctionFactory.Create(ListDirectoryDelegate,
            name: "FileSurfer_ListDirectory",
            description: "Lists files and directories in the sandboxed working directory."),
        AIFunctionFactory.Create(WriteFileDelegate,
            name: "FileSurfer_WriteFile",
            description: "Writes text to a file in the sandboxed working directory."),
        AIFunctionFactory.Create(_webFetcher.FetchUrl,
            name: "WebFetcher_FetchUrl",
            description: "Fetches a web page and returns its Markdown or plain-text content."),
        AIFunctionFactory.Create(ExecuteCodeDelegate,
            name: "Coder_ExecuteCode",
            description: "Executes Python code via WSL2."),
        AIFunctionFactory.Create(RequestClarificationDelegate,
            name: "UserProxy_RequestClarification",
            description: "Request clarification from the human user. Use when the task is ambiguous or needs confirmation."),
        AIFunctionFactory.Create(Submit,
            name: "Submit",
            description: "Submit the final answer. Call this when you have completed the task.")
    ];

    [Description("Lists files and directories in the working directory.")]
    private string ListDirectoryDelegate(
        [Description("Relative path to list, or empty for the root")] string? relativePath = null) =>
        _fileSurfer.ListDirectory(relativePath);

    [Description("Writes text content to a file in the working directory.")]
    private void WriteFileDelegate(
        [Description("Relative path to the file")] string relativePath,
        [Description("Content to write")] string content) =>
        _fileSurfer.WriteFile(relativePath, content);

    [Description("Executes code.")]
    private Task<string> ExecuteCodeDelegate(
        [Description("Source code")] string code,
        [Description("Language")] string language = "python") =>
        _coder.ExecuteCode(code, language).ContinueWith(task => task.Result.Output, TaskScheduler.Default);

    [Description("Requests clarification from the user.")]
    private Task<string> RequestClarificationDelegate(
        [Description("The question or clarification request to present to the user")] string question) =>
        _userProxy.ExecuteAsync(question, _currentProgress!, _currentCt);

    [Description("Delivers the final answer and ends the agentic loop.")]
    private static string Submit(
        [Description("The final answer or summary")] string result) =>
        $"Result received: {result}";

    private static string BuildSystemPrompt(TaskRequest request)
    {
        const string staticPart =
            """
            You are MagenticUI Orchestrator. You complete tasks by calling tools.

            TOOLS — call them by outputting ONLY the JSON object shown, nothing else:

              Fetch a web page:
                {"name":"WebFetcher_FetchUrl","arguments":{"url":"<URL>"}}

              Read a file:
                {"name":"FileSurfer_ReadFile","arguments":{"relativePath":"<path>"}}

              List directory:
                {"name":"FileSurfer_ListDirectory","arguments":{"relativePath":"<path>"}}

              Write a file:
                {"name":"FileSurfer_WriteFile","arguments":{"relativePath":"<path>","content":"<text>"}}

              Run Python code:
                {"name":"Coder_ExecuteCode","arguments":{"code":"<python code>","language":"python"}}

              Ask user for clarification:
                {"name":"UserProxy_RequestClarification","arguments":{"question":"<question>"}}

              Deliver final answer (ALWAYS end with this):
                {"name":"Submit","arguments":{"result":"<your complete answer>"}}

            STRICT RULES:
            1. When you need to call a tool, output ONLY the JSON object — no explanation, no markdown fences, no other text.
            2. After receiving the tool output, call the next tool OR call Submit with your final answer.
            3. You MUST always finish by calling Submit.
            4. NEVER say you cannot use tools or that you cannot access the internet. You have WebFetcher_FetchUrl — use it.
            5. Do NOT explain your plan. Just call the tool.

            """;

        return staticPart
            + $"Working directory: {request.WorkingDirectory ?? "(temp)"}\n"
            + $"Task: {request.TaskId}";
    }

    /// <summary>
    /// Parses a JSON tool call that a model emitted in its response text instead of
    /// using native function-call tokens.  Handles plain JSON and ```json fenced blocks.
    /// Expected shape: {{"name":"ToolName","arguments":{{...}}}}
    /// </summary>
    private static FunctionCallContent? TryParseTextToolCall(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Strip markdown code fences (```json ... ``` or ``` ... ```)
        var fenceMatch = Regex.Match(text, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```",
            RegexOptions.IgnoreCase);
        var candidate = fenceMatch.Success ? fenceMatch.Groups[1].Value : text;

        // If the text is not purely JSON, try to find the outermost {...} that
        // contains a "name" key.
        if (!candidate.TrimStart().StartsWith('{'))
        {
            var jsonMatch = Regex.Match(candidate,
                @"\{[^{}]*""name""[^{}]*(?:\{[^{}]*\}[^{}]*)?\}",
                RegexOptions.Singleline);
            if (!jsonMatch.Success) return null;
            candidate = jsonMatch.Value;
        }

        try
        {
            using var doc = JsonDocument.Parse(candidate.Trim());
            var root = doc.RootElement;

            if (!root.TryGetProperty("name", out var nameProp)) return null;
            var name = nameProp.GetString();
            if (string.IsNullOrWhiteSpace(name)) return null;

            var args = new Dictionary<string, object?>();
            if (root.TryGetProperty("arguments", out var argsProp) &&
                argsProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in argsProp.EnumerateObject())
                {
                    args[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString()
                        : prop.Value.ToString();
                }
            }

            return new FunctionCallContent(
                callId: Guid.NewGuid().ToString("N"),
                name: name,
                arguments: args);
        }
        catch
        {
            return null;
        }
    }

    private static void Report(
        IProgress<AgentMessage> progress,
        string agentName,
        string role,
        string text,
        int round) =>
        progress.Report(new AgentMessage(agentName, role, text, round, DateTimeOffset.UtcNow));

    private static string DetermineAgentName(string toolName) =>
        toolName.StartsWith("FileSurfer_", StringComparison.Ordinal) ? "FileSurfer" :
        toolName.StartsWith("WebFetcher_", StringComparison.Ordinal) ? "WebFetcher" :
        toolName.StartsWith("Coder_", StringComparison.Ordinal) ? "Coder" :
        toolName.StartsWith("UserProxy_", StringComparison.Ordinal) ? "UserProxy" :
        "Orchestrator";

    private static string FormatArgs(IDictionary<string, object?>? args) =>
        args is null ? string.Empty : string.Join(", ", args.Select(kv => $"{kv.Key}: \"{kv.Value}\""));
}
