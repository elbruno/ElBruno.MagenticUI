using ElBruno.MagenticUI.Agents.Models;
using ElBruno.MagenticUI.Agents.Tools;

namespace ElBruno.MagenticUI.Agents.Agents;

public sealed class FileSurferAgent
{
    private readonly FileSurferTool _tool;
    private const string AgentName = "FileSurfer";

    public FileSurferAgent(FileSurferTool tool)
    {
        _tool = tool;
    }

    public async Task<string> ExecuteAsync(
        string instruction,
        IProgress<AgentMessage> progress,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Report(progress, "tool", $"Instruction: {instruction}", round: 0);

        var lower = instruction.ToLowerInvariant();

        string result;
        if (lower.StartsWith("read") || lower.StartsWith("open"))
        {
            var path = ExtractFirstArg(instruction);
            result = _tool.ReadFile(path);
        }
        else if (lower.StartsWith("write") || lower.StartsWith("save"))
        {
            var parts = instruction.Split("|||", 2);
            var path = ExtractFirstArg(parts[0]);
            var content = parts.Length > 1 ? parts[1] : string.Empty;
            _tool.WriteFile(path, content);
            result = $"Written: {path}";
        }
        else
        {
            var dir = ExtractFirstArg(instruction);
            result = _tool.ListDirectory(string.IsNullOrWhiteSpace(dir) ? null : dir);
        }

        await Task.CompletedTask;
        Report(progress, "tool", result, round: 0);
        return result;
    }

    private static void Report(IProgress<AgentMessage> progress, string role, string text, int round) =>
        progress.Report(new AgentMessage(AgentName, role, text, round, DateTimeOffset.UtcNow));

    private static string ExtractFirstArg(string instruction)
    {
        var parts = instruction.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1].Trim() : string.Empty;
    }
}
