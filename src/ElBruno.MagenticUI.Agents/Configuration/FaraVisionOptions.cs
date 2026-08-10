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
    /// Optional directory containing the CUDA 12 / cuDNN 9 native libraries
    /// (<c>cudart64_12.dll</c>, <c>cublas64_12.dll</c>, <c>cublasLt64_12.dll</c>,
    /// <c>cudnn64_9.dll</c>). ONNX Runtime does not redistribute these. When empty,
    /// well-known install locations are probed automatically.
    /// </summary>
    public string? CudaDependencyPath { get; set; }

    /// <summary>
    /// Maximum time allowed for a single Fara prediction, including first-use
    /// model load/download and CPU inference for the 9B model. Defaults to
    /// 180 seconds because the first prediction after startup can take much
    /// longer than typical text-model inference.
    /// </summary>
    public int PredictionTimeoutSeconds { get; set; } = 180;
}
