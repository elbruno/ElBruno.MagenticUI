namespace ElBruno.MagenticUI.Agents.Configuration;

/// <summary>
/// Configuration for the optional Fara1.5-9B vision client.
/// </summary>
public sealed class FaraVisionOptions
{
    public const string SectionName = "LocalLLMs:FaraVision";

    public string? ModelPath { get; set; }
    public string? CacheDirectory { get; set; }
    public bool EnsureModelDownloaded { get; set; } = true;
    public int MaxSequenceLength { get; set; } = 4096;
    public float Temperature { get; set; } = 0.1f;
    public float TopP { get; set; } = 0.9f;
    public int GpuDeviceId { get; set; }
    public string ExecutionProvider { get; set; } = "Auto";

    /// <summary>
    /// Maximum tokens Fara may generate for a single prediction. A prediction is a short
    /// JSON action, so a small budget keeps CPU inference from running to
    /// <see cref="MaxSequenceLength"/> and repeating itself.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 128;

    /// <summary>
    /// Maximum time allowed for a single Fara prediction, including first-use
    /// model load/download and CPU inference for the 9B model. Defaults to
    /// 180 seconds because the first prediction after startup can take much
    /// longer than typical text-model inference.
    /// </summary>
    public int PredictionTimeoutSeconds { get; set; } = 180;
}
