using System.ComponentModel;
using System.Text.Json;
using ElBruno.MagenticUI.Agents.Agents;
using ElBruno.MagenticUI.Agents.Models;
using ElBruno.MagenticUI.Agents.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ElBruno.MagenticUI.Agents.Orchestrator;

public sealed class MagenticUIOrchestrator
{
    private const int MaxModelToolResultCharacters = 400;
    private const int MaxScreenshotBytes = 1_500_000;
    private readonly IChatClient _orchestratorClient;
    private readonly FileSurferTool _fileSurfer;
    private readonly WebFetchTool _webFetcher;
    private readonly CodeExecutorTool _coder;
    private readonly ComputerUseTool _computerUse;
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
        ComputerUseTool computerUse,
        UserProxyAgent userProxy,
        int maxRounds = 15,
        ILogger<MagenticUIOrchestrator>? logger = null)
    {
        _orchestratorClient = orchestratorClient;
        _fileSurfer = fileSurfer;
        _webFetcher = webFetcher;
        _coder = coder;
        _computerUse = computerUse;
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
        var hasToolResult = false;

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
                throw;
            }

            var calls = response.Messages
                .SelectMany(message => message.Contents.OfType<FunctionCallContent>())
                .ToList();

            // Fallback for models (e.g. phi-3.5-mini via ONNX) that output JSON tool calls
            // in their text instead of native function-call tokens.
            var textBasedCall = false;
            if (calls.Count == 0 && !string.IsNullOrWhiteSpace(response.Text))
            {
                var textCalls = TryParseTextToolCalls(response.Text);
                if (textCalls.Count > 0)
                {
                    calls.Add(SelectNextTextToolCall(textCalls));
                    textBasedCall = true;
                }
            }

            if (calls.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(response.Text))
                {
                    if (hasToolResult && TryExtractSubmitResult(response.Text, out var embeddedResult))
                    {
                        Report(progress, "Orchestrator", "submit", embeddedResult, round);
                        submitted = true;
                        break;
                    }

                    if (response.Text.TrimStart().StartsWith('{'))
                        throw new InvalidOperationException("The model returned an incomplete tool call.");

                    Report(progress, "Orchestrator", "assistant", response.Text, round);
                    submitted = true;
                }

                break;
            }

            // For native function calls, add the response messages (with FunctionCallContent)
            // to the conversation history. For text-based calls the response is plain text —
            // we manage history manually below to keep it compatible with small models.
            if (!textBasedCall)
                messages.AddRange(response.Messages);

            var resultContents = new List<AIContent>();
            var textToolResults = new List<(string ToolName, string Result)>();

            foreach (var call in calls)
            {
                ct.ThrowIfCancellationRequested();

                if (call.Name == "Submit")
                {
                    var submitArg = call.Arguments?.TryGetValue("result", out var r) == true
                        ? r?.ToString() ?? string.Empty
                        : string.Empty;
                    if (IsInvalidSubmitResult(submitArg))
                    {
                        const string rejection =
                            "Submit rejected. Write the actual answer using the tool output; do not include tool names or orchestration instructions.";
                        resultContents.Add(new FunctionResultContent(call.CallId, rejection));
                        textToolResults.Add((call.Name, rejection));
                        continue;
                    }

                    submitted = true;
                    Report(progress, "Orchestrator", "submit", submitArg, round);
                    resultContents.Add(new FunctionResultContent(call.CallId, "Submitted successfully."));
                    continue;
                }

                Report(progress, "Orchestrator", "tool",
                    $"Calling {call.Name}({FormatArgs(call.Arguments)})", round);
                ReportComputerAction(progress, call, round);

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
                ReportComputerScreenshot(progress, call, round);

                resultContents.Add(new FunctionResultContent(call.CallId, TruncateToolResult(resultStr)));
                textToolResults.Add((call.Name, TruncateToolResult(resultStr)));
                hasToolResult = true;
            }

            if (textBasedCall && !submitted)
            {
                var toolSummary = FormatTextToolResults(textToolResults);
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
        AIFunctionFactory.Create(DescribeImageDelegate,
            name: "Computer_DescribeImage",
            description: "Analyzes an image file from the working directory using the computer-use vision model."),
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
    private async Task<string> ExecuteCodeDelegate(
        [Description("Source code")] string code,
        [Description("Language")] string language = "python")
    {
        var execution = await _coder.ExecuteCode(code, language);
        if (!execution.Success)
            return execution.Error ?? "Code execution failed.";

        return string.IsNullOrWhiteSpace(execution.Output)
            ? "Code completed with no output. Use print(...) to return computed values; do not repeat assignments without printing."
            : execution.Output;
    }

    [Description("Analyzes a sandboxed image with the computer-use model.")]
    private Task<string> DescribeImageDelegate(
        [Description("Relative image path in working directory")] string relativePath,
        [Description("Question/instruction for the computer-use model")] string prompt = "Describe what is visible in this image.") =>
        _computerUse.DescribeImage(relativePath, prompt);

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
            You are MagenticUI Orchestrator. Complete the task with the supplied tools.
            To call a tool, output only one JSON object:
            {"name":"ToolName","arguments":{"argument":"value"}}
            Never explain a tool call or wrap it in Markdown. Use WebFetcher_FetchUrl for URLs.
            After each tool result, call the next tool. Always finish by calling Submit with the complete answer.
            The Submit result must contain only the answer for the user, never tool names or orchestration instructions.

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
    internal static FunctionCallContent? TryParseTextToolCall(string text)
        => TryParseTextToolCalls(text).FirstOrDefault();

    internal static FunctionCallContent SelectNextTextToolCall(
        IReadOnlyList<FunctionCallContent> calls) =>
        calls.FirstOrDefault(call => call.Name != "Submit") ?? calls[0];

    internal static List<FunctionCallContent> TryParseTextToolCalls(string text)
    {
        var calls = new List<FunctionCallContent>();
        if (string.IsNullOrWhiteSpace(text)) return calls;

        foreach (var candidate in ExtractJsonObjects(text))
        {
            try
            {
                using var doc = JsonDocument.Parse(candidate);
                var root = doc.RootElement;
                if (!root.TryGetProperty("name", out var nameProp)) continue;
                var name = nameProp.GetString();
                if (string.IsNullOrWhiteSpace(name)) continue;

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

                calls.Add(new FunctionCallContent(
                    Guid.NewGuid().ToString("N"), name, args));
            }
            catch (JsonException)
            {
                // Ignore malformed objects and continue with later valid calls.
            }
        }

        return calls;
    }

    private static IEnumerable<string> ExtractJsonObjects(string text)
    {
        for (var start = text.IndexOf('{'); start >= 0 && start < text.Length;)
        {
            var depth = 0;
            var inString = false;
            var escaped = false;
            var end = -1;

            for (var index = start; index < text.Length; index++)
            {
                var character = text[index];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (character == '\\') escaped = true;
                    else if (character == '"') inString = false;
                    continue;
                }

                if (character == '"') inString = true;
                else if (character == '{') depth++;
                else if (character == '}' && --depth == 0)
                {
                    end = index;
                    break;
                }
            }

            if (end < 0) yield break;
            yield return text[start..(end + 1)];
            start = text.IndexOf('{', end + 1);
        }
    }

    internal static string TruncateToolResult(string result) =>
        result.Length > MaxModelToolResultCharacters
            ? result[..MaxModelToolResultCharacters] + "...[truncated]"
            : result;

    internal static string FormatTextToolResults(
        IEnumerable<(string ToolName, string Result)> results)
    {
        var entries = results.ToList();
        var output = string.Join("\n\n", entries.Select(entry =>
            $"Result from {entry.ToolName}:\n{entry.Result}"));
        return output +
            "\n\nUse the results above to answer the original task. " +
            "Call another tool only if necessary; otherwise call Submit with only the final answer.";
    }

    internal static bool IsInvalidSubmitResult(string result) =>
        string.IsNullOrWhiteSpace(result) ||
        result.Contains("do not repeat", StringComparison.OrdinalIgnoreCase) ||
        result.Contains("use this output", StringComparison.OrdinalIgnoreCase) ||
        result.Contains("call another", StringComparison.OrdinalIgnoreCase) ||
        result.Contains("Submit the final answer", StringComparison.OrdinalIgnoreCase);

    internal static bool TryExtractSubmitResult(string text, out string result)
    {
        result = string.Empty;
        var submitIndex = text.IndexOf("\"Submit\"", StringComparison.OrdinalIgnoreCase);
        if (submitIndex < 0) return false;

        var resultKeyIndex = text.IndexOf("\"result\"", submitIndex, StringComparison.OrdinalIgnoreCase);
        if (resultKeyIndex < 0) return false;

        var colonIndex = text.IndexOf(':', resultKeyIndex + 8);
        var quoteIndex = colonIndex >= 0 ? text.IndexOf('"', colonIndex + 1) : -1;
        if (quoteIndex < 0) return false;

        var escaped = false;
        for (var index = quoteIndex + 1; index < text.Length; index++)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (text[index] == '\\')
            {
                escaped = true;
            }
            else if (text[index] == '"')
            {
                var nextIndex = index + 1;
                while (nextIndex < text.Length && char.IsWhiteSpace(text[nextIndex]))
                    nextIndex++;
                if (nextIndex >= text.Length || text[nextIndex] != '}')
                    return false;

                var jsonString = text[quoteIndex..(index + 1)];
                result = JsonSerializer.Deserialize<string>(jsonString) ?? string.Empty;
                return !string.IsNullOrWhiteSpace(result);
            }
        }

        return false;
    }

    private static void Report(
        IProgress<AgentMessage> progress,
        string agentName,
        string role,
        string text,
        int round) =>
        progress.Report(new AgentMessage(agentName, role, text, round, DateTimeOffset.UtcNow));

    private static void ReportComputerAction(
        IProgress<AgentMessage> progress,
        FunctionCallContent call,
        int round)
    {
        if (!IsComputerAction(call.Name))
            return;

        Report(
            progress,
            "Computer",
            "browser_action",
            $"{call.Name}: {FormatArgs(call.Arguments)}",
            round);
    }

    private void ReportComputerScreenshot(
        IProgress<AgentMessage> progress,
        FunctionCallContent call,
        int round)
    {
        if (!IsComputerScreenshotAction(call.Name))
            return;
        if (!TryGetRelativePath(call.Arguments, out var relativePath))
            return;

        string fullPath;
        try
        {
            fullPath = _fileSurfer.ResolvePath(relativePath);
        }
        catch
        {
            return;
        }

        if (!TryBuildImageDataUri(fullPath, out var dataUri))
            return;

        Report(progress, "Computer", "browser_screenshot", dataUri, round);
    }

    internal static bool TryGetRelativePath(
        IDictionary<string, object?>? args,
        out string relativePath)
    {
        relativePath = string.Empty;
        if (args is null) return false;

        var keys = new[] { "relativePath", "path", "imagePath", "image_path" };
        object? value = null;
        foreach (var key in keys)
        {
            if (args.TryGetValue(key, out value) && value is not null)
                break;

            var found = args.FirstOrDefault(kv =>
                kv.Value is not null &&
                kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(found.Key))
            {
                value = found.Value;
                break;
            }
        }

        if (value is null) return false;

        string? text = value switch
        {
            JsonElement { ValueKind: JsonValueKind.String } jsonValue => jsonValue.GetString(),
            JsonElement jsonValue => jsonValue.ToString(),
            _ => value.ToString()
        };
        if (string.IsNullOrWhiteSpace(text)) return false;

        text = text.Trim();
        if (text.Length > 1 && text[0] == '"' && text[^1] == '"')
        {
            try
            {
                text = JsonSerializer.Deserialize<string>(text);
            }
            catch (JsonException)
            {
                // Fall back to trimmed raw value.
            }
        }

        if (string.IsNullOrWhiteSpace(text)) return false;

        relativePath = text;
        return true;
    }

    internal static bool IsComputerAction(string? toolName) =>
        !string.IsNullOrWhiteSpace(toolName) &&
        toolName.StartsWith("Computer_", StringComparison.Ordinal);

    internal static bool IsComputerScreenshotAction(string? toolName) =>
        string.Equals(toolName, "Computer_DescribeImage", StringComparison.Ordinal);

    internal static bool TryBuildImageDataUri(
        string filePath,
        out string dataUri)
    {
        dataUri = string.Empty;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        FileInfo fileInfo;
        try
        {
            fileInfo = new FileInfo(filePath);
        }
        catch
        {
            return false;
        }

        if (fileInfo.Length <= 0 || fileInfo.Length > MaxScreenshotBytes)
            return false;

        var mime = Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(mime))
            return false;

        try
        {
            var bytes = File.ReadAllBytes(filePath);
            dataUri = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string DetermineAgentName(string toolName) =>
        toolName.StartsWith("FileSurfer_", StringComparison.Ordinal) ? "FileSurfer" :
        toolName.StartsWith("WebFetcher_", StringComparison.Ordinal) ? "WebFetcher" :
        toolName.StartsWith("Coder_", StringComparison.Ordinal) ? "Coder" :
        toolName.StartsWith("Computer_", StringComparison.Ordinal) ? "Computer" :
        toolName.StartsWith("UserProxy_", StringComparison.Ordinal) ? "UserProxy" :
        "Orchestrator";

    private static string FormatArgs(IDictionary<string, object?>? args) =>
        args is null ? string.Empty : string.Join(", ", args.Select(kv => $"{kv.Key}: \"{kv.Value}\""));
}
