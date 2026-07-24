using System.ComponentModel;
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

            if (calls.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(response.Text))
                    submitted = true;

                break;
            }

            messages.AddRange(response.Messages);
            var resultContents = new List<AIContent>();

            foreach (var call in calls)
            {
                ct.ThrowIfCancellationRequested();

                if (call.Name == "Submit")
                {
                    submitted = true;
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
                    resultStr.Length > 500 ? resultStr[..500] + "...[truncated]" : resultStr, round);

                resultContents.Add(new FunctionResultContent(call.CallId, resultStr));
            }

            messages.Add(new ChatMessage(ChatRole.Tool, resultContents));
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

    private static string BuildSystemPrompt(TaskRequest request) =>
        $"""
        You are the MagenticUI Orchestrator - a multi-agent coordinator powered by a local LLM.
        You have access to the following participants and their tools:

          - FileSurfer: reads, writes, and lists files in the working directory ({request.WorkingDirectory ?? "."})
          - WebFetcher: fetches web pages and returns their content as Markdown
          - Coder: executes Python code via WSL2
          - UserProxy: pauses for human clarification input from the browser

        Strategy:
        1. Break the task into steps and identify which participant should act first.
        2. Call that participant's tool(s).
        3. Analyze the results and decide the next step.
        4. When you have a complete answer, call Submit with your final result.

        Working directory: {request.WorkingDirectory ?? "(not set)"}
        Task ID: {request.TaskId}
        """;

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
