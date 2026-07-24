namespace ElBruno.MagenticUI.Agents.Models;

public sealed record TaskRequest(
    string TaskId,
    string Prompt,
    string? WorkingDirectory = null);
