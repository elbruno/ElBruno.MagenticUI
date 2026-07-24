namespace ElBruno.MagenticUI.Agents.Models;

public sealed record AgentMessage(
    string AgentName,
    string Role,
    string Text,
    int Round,
    DateTimeOffset Timestamp);
