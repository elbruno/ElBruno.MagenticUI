namespace ElBruno.MagenticUI.App.ModelSettings;

public sealed record ModelSettingsEntry(
    ModelRole Role,
    string RoleDisplayName,
    string ModelId,
    string ModelName,
    string EffectiveModelPath,
    bool UsesExplicitPath,
    bool IsPresent,
    string StatusText,
    string CacheDirectory);
