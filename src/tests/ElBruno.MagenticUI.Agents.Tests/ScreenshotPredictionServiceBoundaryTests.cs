using ElBruno.MagenticUI.App;

namespace ElBruno.MagenticUI.Agents.Tests;

public sealed class ScreenshotPredictionServiceBoundaryTests
{
    [Fact]
    public async Task UnconfiguredServiceDoesNotExecuteExternalActions()
    {
        var service = new UnconfiguredScreenshotPredictionService();
        var request = new ScreenshotPredictionRequest([1, 2, 3], "image/png", "Find the safe demo button.");

        var result = await service.PredictAsync(request);

        Assert.Empty(result.Predictions);
        Assert.Contains(result.Diagnostics, item => item.Contains("No screenshot prediction provider", StringComparison.Ordinal));
        Assert.Contains("no browser execution", result.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnconfiguredServiceHonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new UnconfiguredScreenshotPredictionService().PredictAsync(
                new ScreenshotPredictionRequest([], "image/png", "safe goal"),
                cancellation.Token));
    }

    [Fact]
    public async Task FakeProviderBoundaryCarriesScreenshotAndGoal()
    {
        var fake = new FakePredictionService();
        var request = new ScreenshotPredictionRequest([9, 8], "image/jpeg", "Locate the button.");

        var result = await fake.PredictAsync(request);

        Assert.Same(request, fake.Received);
        Assert.Single(result.Predictions);
        Assert.Equal("fake result", result.Summary);
    }

    private sealed class FakePredictionService : IScreenshotPredictionService
    {
        public ScreenshotPredictionRequest? Received { get; private set; }

        public Task<ScreenshotPredictionResult> PredictAsync(
            ScreenshotPredictionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Received = request;
            return Task.FromResult(new ScreenshotPredictionResult(
                [new CoordinatePrediction("button", 50, 25, 10, 5, 0.9)],
                [],
                "fake result"));
        }

        public FaraModelCacheStatus GetModelCacheStatus() => new(true, 0);

        public FaraExecutionProviderStatus GetExecutionProviderStatus() =>
            new("Cpu", false, "Test double.");
    }
}
