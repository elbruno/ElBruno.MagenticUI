using ElBruno.MagenticUI.Agents.Models;
using ElBruno.MagenticUI.Agents.Tools;

namespace ElBruno.MagenticUI.Agents.Agents;

public sealed class CoderAgent
{
    private readonly CodeExecutorTool _tool;
    private const string AgentName = "Coder";

    public CoderAgent(CodeExecutorTool tool)
    {
        _tool = tool;
    }

    public async Task<CodeExecutionResult> ExecuteAsync(
        string code,
        string language,
        IProgress<AgentMessage> progress,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        Report(progress, "tool",
            $"Code execution requested for language '{language}'.", round: 0);

        var result = await _tool.ExecuteCode(code, language);

        Report(progress, "tool", result.Output, round: 0);
        return result;
    }

    private static void Report(IProgress<AgentMessage> progress, string role, string text, int round) =>
        progress.Report(new AgentMessage(AgentName, role, text, round, DateTimeOffset.UtcNow));
}
