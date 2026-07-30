namespace ElBruno.MagenticUI.App.Configuration;

public sealed record RuntimeSettingsSnapshot(
    string OrchestratorModelPath,
    string ComputerUseModelPath,
    int MaxRounds,
    int TaskTimeoutSeconds,
    int MaxOutputTokens);
