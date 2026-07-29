using ElBruno.MagenticUI.App.ModelDownloadProgress;

namespace ElBruno.MagenticUI.App.ModelSettings;

public sealed record ModelStatusSnapshot(
    ModelSettingsEntry Entry,
    ModelDownloadState DownloadState,
    ModelDownloadPhase EffectivePhase,
    bool CanDownload,
    double DisplayPercent,
    string StatusText);
