namespace ElBruno.MagenticUI.Agents.Models;

public sealed record CodeExecutionResult(
    bool Success,
    string Output,
    string? Error = null);
