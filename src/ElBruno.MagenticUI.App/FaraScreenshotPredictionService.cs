using ElBruno.LocalLLMs;
using ElBruno.MagenticUI.Agents.Configuration;
using ElBruno.MagenticUI.Agents.Models;
using ElBruno.MagenticUI.Agents.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace ElBruno.MagenticUI.App;

/// <summary>
/// Runs Fara against a staged screenshot and converts its action into the
/// prediction-only UI contract. No browser actions are executed.
/// </summary>
public sealed class FaraScreenshotPredictionService : IScreenshotPredictionService
{
    private const int CoordinateSpaceSize = 1000;
    private const int MaxImageBytes = 10 * 1024 * 1024;
    private readonly TimeSpan _predictionTimeout;
    private readonly int _maxOutputTokens;
    private readonly IChatClient _client;
    private readonly LocalLLMsOptions _options;
    private readonly FaraActionParser _parser;
    private readonly ExecutionProviderPlan _providerPlan;

    public FaraScreenshotPredictionService(
        [FromKeyedServices(FaraVisionServiceExtensions.ServiceKey)] IChatClient client,
        [FromKeyedServices(FaraVisionServiceExtensions.ServiceKey)] LocalLLMsOptions options,
        [FromKeyedServices(FaraVisionServiceExtensions.ServiceKey)] FaraVisionOptions visionOptions,
        [FromKeyedServices(FaraVisionServiceExtensions.ServiceKey)] ExecutionProviderPlan providerPlan,
        FaraActionParser parser)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _providerPlan = providerPlan ?? throw new ArgumentNullException(nameof(providerPlan));
        ArgumentNullException.ThrowIfNull(visionOptions);
        _predictionTimeout = TimeSpan.FromSeconds(Math.Max(1, visionOptions.PredictionTimeoutSeconds));
        _maxOutputTokens = Math.Max(1, visionOptions.MaxOutputTokens);
    }

    public async Task<ScreenshotPredictionResult> PredictAsync(
        ScreenshotPredictionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        EnsureLocalModelConfigured();

        var stagingDirectory = Path.Combine(Path.GetTempPath(), "magentic-ui-fara");
        Directory.CreateDirectory(stagingDirectory);
        var extension = request.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
            ? ".png"
            : request.ContentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase) ? ".webp" : ".jpg";
        var imagePath = Path.Combine(stagingDirectory, $"{Guid.NewGuid():N}{extension}");

        try
        {
            await File.WriteAllBytesAsync(imagePath, request.ImageBytes, cancellationToken);

            using var timeout = new CancellationTokenSource(_predictionTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, timeout.Token);

            var response = await _client.GetResponseAsync(
                [new ChatMessage(
                    ChatRole.User,
                    $"Inspect the screenshot and predict the next safe UI action for this goal: {request.Goal.Trim()}. " +
                    "Return exactly one JSON object using a supported action (left_click, right_click, double_click, " +
                    "left_click_drag, type, key, scroll, or visit_url). Use coordinate [x,y] in the 0-1000 coordinate " +
                    "space for coordinate actions and include the relevant text, keys, pixels, or url argument otherwise. " +
                    "Do not return markdown or explanations.")],
                new VisionChatOptions
                {
                    ImagePaths = [imagePath],
                    // Safe since ElBruno.LocalLLMs 0.20.11: max_length is now derived from the
                    // full multimodal input_ids (text + vision tokens), so a small output budget
                    // no longer trips "input_ids size (N) exceeds max length".
                    MaxOutputTokens = _maxOutputTokens,
                    Temperature = 0.1f
                },
                linked.Token);

            linked.Token.ThrowIfCancellationRequested();
            var parsed = _parser.Parse(response.Text ?? string.Empty);
            if (!parsed.Success || parsed.Action is null)
            {
                return new ScreenshotPredictionResult(
                    [], 
                    [$"Fara response could not be parsed: {parsed.Error ?? "unknown parser error"}"],
                    "Fara returned no usable action.",
                    RawResponse: response.Text);
            }

            var action = parsed.Action;
            var predictions = action.Coordinate is null
                ? Array.Empty<CoordinatePrediction>()
                : [CreateCoordinatePrediction(action)];

            return new ScreenshotPredictionResult(
                predictions,
                action.Coordinate is null
                    ? ["Non-coordinate action displayed for inspection only.",
                       "Prediction-only mode: no browser action was executed."]
                    : ["Coordinates are normalized from Fara's 0-1000 action coordinate space.",
                       "Prediction-only mode: no browser action was executed."],
                DescribeAction(action),
                action,
                response.Text);
        }

        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Fara screenshot prediction timed out after {_predictionTimeout.TotalSeconds:0} seconds. " +
                "The first prediction after startup can be slow while Fara downloads/loads the model; " +
                "try again, or increase LocalLLMs:FaraVision:PredictionTimeoutSeconds in appsettings.json.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException
            and not TimeoutException
            and not FaraVisionConfigurationException)
        {
            // ElBruno.LocalLLMs 0.20.11 probes the multimodal input length with a generator
            // built at max_length = int.MaxValue, which ONNX Runtime GenAI rejects for any model
            // that declares a context_length. It aborts the request before generation starts.
            // Tracked in elbruno/ElBruno.LocalLLMs#51. Remove once a fixed package ships.
            if (ex.Message.Contains("context_length", StringComparison.OrdinalIgnoreCase) &&
                ex.Message.Contains("max_length", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Fara prediction is blocked by a known defect in ElBruno.LocalLLMs 0.20.11: its " +
                    "vision input-length probe requests max_length=int.MaxValue, which ONNX Runtime " +
                    "rejects because the model declares a smaller context_length. Downgrade to " +
                    "0.20.10 or wait for the fix tracked in elbruno/ElBruno.LocalLLMs#51. " +
                    $"Original error: {ex.Message}",
                    ex);
            }

            throw new InvalidOperationException($"Fara screenshot prediction failed: {ex.Message}", ex);
        }

        finally
        {
            try
            {
                if (File.Exists(imagePath))
                    File.Delete(imagePath);
            }
            catch
            {
                // Cleanup must not hide the prediction result or cancellation.
            }
        }
    }

    private void EnsureLocalModelConfigured()
    {
        if (!string.IsNullOrWhiteSpace(_options.ModelPath) || _options.EnsureModelDownloaded)
            return;

        throw new FaraVisionConfigurationException(
            "Fara visual prediction needs either a local multimodal ONNX model directory " +
            "or auto-download enabled. Set LocalLLMs:FaraVision:ModelPath to a converted " +
            "Fara1.5-9B folder, or set EnsureModelDownloaded to true, then restart Aspire. " +
            "See docs/fara-onnx-setup.md for setup and cache details.");
    }

    public FaraModelCacheStatus GetModelCacheStatus()
    {
        if (!string.IsNullOrWhiteSpace(_options.ModelPath))
        {
            // A local model path bypasses the download cache entirely.
            return new FaraModelCacheStatus(IsCached: true, CachedBytes: 0);
        }

        var cachedBytes = LocalVisionChatClient.GetModelCacheSize(_options.Model, _options.CacheDirectory);
        return new FaraModelCacheStatus(IsCached: cachedBytes > 0, CachedBytes: cachedBytes);
    }

    public FaraExecutionProviderStatus GetExecutionProviderStatus()
    {
        // ActiveExecutionProvider is only meaningful once the model has been created; before
        // that it reports the configured request. Reading it never forces a load.
        var active = _client is LocalVisionChatClient vision
            ? vision.ActiveExecutionProvider
            : _options.ExecutionProvider;

        var isGpu = active is ExecutionProvider.Cuda or ExecutionProvider.DirectML;

        var detail = isGpu
            ? $"Fara is running on the {active} execution provider. {_providerPlan.Detail}".Trim()
            : active is ExecutionProvider.Auto
                ? $"The execution provider is selected when the model is first loaded. {_providerPlan.Detail}".Trim()
                : $"Fara is running on {active}. {_providerPlan.Detail}".Trim();

        return new FaraExecutionProviderStatus(active.ToString(), isGpu, detail);
    }

    private static CoordinatePrediction CreateCoordinatePrediction(FaraAction action)
    {
        var coordinate = action.Coordinate!;
        const double markerSize = 4;
        var x = Math.Clamp(coordinate.X / 10d, 0, 100);
        var y = Math.Clamp(coordinate.Y / 10d, 0, 100);
        return new CoordinatePrediction(
            action.Type.ToString(),
            Math.Clamp(x - markerSize / 2, 0, 100 - markerSize),
            Math.Clamp(y - markerSize / 2, 0, 100 - markerSize),
            markerSize,
            markerSize,
            0.5);
    }

    private static string DescribeAction(FaraAction action) =>
        action.Type switch
        {
            FaraActionType.Type => $"Fara predicted type: \"{action.Text}\".",
            FaraActionType.Key => $"Fara predicted key sequence: {string.Join(" + ", action.Keys ?? [])}.",
            FaraActionType.Scroll => $"Fara predicted scroll: {action.Pixels:0.##} pixels.",
            FaraActionType.VisitUrl => $"Fara predicted visit URL: {action.Url}.",
            _ when action.Coordinate is not null =>
                $"Fara predicted {action.Type} at ({action.Coordinate.X}, {action.Coordinate.Y}).",
            _ => $"Fara predicted {action.Type}."
        };

    private static void ValidateRequest(ScreenshotPredictionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ImageBytes is null || request.ImageBytes.Length == 0)
            throw new ArgumentException("Screenshot image data is required.", nameof(request));
        if (request.ImageBytes.Length > MaxImageBytes)
            throw new ArgumentException("Screenshot image must be 10 MB or smaller.", nameof(request));
        if (!request.ContentType.Equals("image/png", StringComparison.OrdinalIgnoreCase) &&
            !request.ContentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) &&
            !request.ContentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Screenshot must be PNG, JPEG, or WebP.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Goal))
            throw new ArgumentException("A prediction goal is required.", nameof(request));
    }
}

public sealed class FaraVisionConfigurationException : InvalidOperationException
{
    public FaraVisionConfigurationException(string message)
        : base(message)
    {
    }
}
