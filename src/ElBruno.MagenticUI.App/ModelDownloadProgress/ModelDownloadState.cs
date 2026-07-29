using ElBruno.MagenticUI.App.ModelSettings;

namespace ElBruno.MagenticUI.App.ModelDownloadProgress;

public sealed record ModelDownloadState(
    ModelRole Role,
    string ModelId,
    string CurrentFileName,
    long DownloadedBytes,
    long TotalBytes,
    double PercentComplete,
    ModelDownloadPhase Phase,
    string StatusText,
    DateTimeOffset LastUpdated,
    string? Error);
