using ElBruno.MagenticUI.Agents.Models;

namespace ElBruno.MagenticUI.App;

public sealed record ScreenshotPredictionRequest(
    byte[] ImageBytes,
    string ContentType,
    string Goal,
    bool GenerateAnnotatedOverlay = true);

public sealed record CoordinatePrediction(
    string Label,
    double X,
    double Y,
    double Width,
    double Height,
    double Confidence);

public sealed record ScreenshotPredictionResult(
    IReadOnlyList<CoordinatePrediction> Predictions,
    IReadOnlyList<string> Diagnostics,
    string? Summary = null,
    FaraAction? Action = null,
    string? RawResponse = null,
    byte[]? AnnotatedImage = null,
    string? AnnotatedImageContentType = null);

/// <summary>
/// Reports whether the Fara model is already cached locally so the UI can
/// warn the user before a first-run download blocks a prediction.
/// </summary>
public sealed record FaraModelCacheStatus(bool IsCached, long CachedBytes);

/// <summary>
/// Describes which ONNX Runtime execution provider Fara is actually running on,
/// so the UI can make a silent CPU fallback obvious instead of leaving the user
/// wondering why predictions take minutes.
/// </summary>
/// <param name="Provider">The active provider name (for example <c>Cuda</c> or <c>Cpu</c>).</param>
/// <param name="IsGpu">True when the active provider is GPU-accelerated.</param>
/// <param name="Detail">Diagnostic detail explaining how the provider was selected.</param>
public sealed record FaraExecutionProviderStatus(string Provider, bool IsGpu, string Detail);

public interface IScreenshotPredictionService
{
    Task<ScreenshotPredictionResult> PredictAsync(
        ScreenshotPredictionRequest request,
        CancellationToken cancellationToken = default);

    FaraModelCacheStatus GetModelCacheStatus();

    /// <summary>
    /// Returns the active execution provider. Resolving this forces the model to load,
    /// so it reports the configured intent until the first prediction has run.
    /// </summary>
    FaraExecutionProviderStatus GetExecutionProviderStatus();
}

/// <summary>
/// Safe integration seam until a parser-backed provider is registered.
/// This provider never launches a browser or executes generated code.
/// </summary>
public sealed class UnconfiguredScreenshotPredictionService : IScreenshotPredictionService
{
    public Task<ScreenshotPredictionResult> PredictAsync(
        ScreenshotPredictionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ScreenshotPredictionResult(
            [],
            ["No screenshot prediction provider is configured. Register an IScreenshotPredictionService implementation to connect the approved parser."],
            "Prediction-only mode is active; no browser execution was performed."));
    }

    public FaraModelCacheStatus GetModelCacheStatus() => new(false, 0);

    public FaraExecutionProviderStatus GetExecutionProviderStatus() =>
        new("None", false, "No screenshot prediction provider is configured.");
}
