using ElBruno.LocalLLMs;
using ElBruno.MagenticUI.App.ModelDownloadProgress;
using ElBruno.MagenticUI.App.ModelSettings;

namespace ElBruno.MagenticUI.Agents.Tests;

public sealed class ModelDownloadProgressStateServiceTests
{
    [Fact]
    public async Task CreateProgressReporter_MapsDownloadProgressToState()
    {
        // Arrange
        var service = new ModelDownloadProgressStateService();
        var reporter = service.CreateProgressReporter(ModelRole.Orchestrator, "model-id");
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.OnChanged += () =>
        {
            if (service.GetState(ModelRole.Orchestrator).Phase == ModelDownloadPhase.Downloading)
                changed.TrySetResult();
            return Task.CompletedTask;
        };

        // Act
        reporter.Report(new ModelDownloadProgress("weights.onnx", 250, 1_000, 25));
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var state = service.GetState(ModelRole.Orchestrator);

        // Assert
        Assert.Equal(ModelRole.Orchestrator, state.Role);
        Assert.Equal("model-id", state.ModelId);
        Assert.Equal("weights.onnx", state.CurrentFileName);
        Assert.Equal(250, state.DownloadedBytes);
        Assert.Equal(1_000, state.TotalBytes);
        Assert.Equal(25d, state.PercentComplete);
        Assert.Equal(ModelDownloadPhase.Downloading, state.Phase);
        Assert.Contains("weights.onnx", state.StatusText, StringComparison.Ordinal);
        Assert.Null(state.Error);
    }

    [Fact]
    public void MarkCompletedAndFailed_UpdatesTerminalStates()
    {
        // Arrange
        var service = new ModelDownloadProgressStateService();
        service.Initialize(ModelRole.ComputerUse, "computer-model");

        // Act
        service.MarkCompleted(ModelRole.ComputerUse, "computer-model");
        var completed = service.GetState(ModelRole.ComputerUse);
        service.MarkFailed(ModelRole.ComputerUse, "computer-model", "network error");
        var failed = service.GetState(ModelRole.ComputerUse);

        // Assert
        Assert.Equal(ModelDownloadPhase.Completed, completed.Phase);
        Assert.Equal(100d, completed.PercentComplete);
        Assert.Equal("Completed", completed.StatusText);

        Assert.Equal(ModelDownloadPhase.Failed, failed.Phase);
        Assert.Equal("Failed", failed.StatusText);
        Assert.Equal("network error", failed.Error);
    }

    [Fact]
    public void CreateProgressReporter_TransitionsFailedStateBackToDownloading_AndClearsError()
    {
        // Arrange
        var service = new ModelDownloadProgressStateService();
        service.MarkFailed(ModelRole.Orchestrator, "model-id", "network error");
        var reporter = service.CreateProgressReporter(ModelRole.Orchestrator, "model-id");

        // Act
        reporter.Report(new ModelDownloadProgress("weights.onnx", 300, 1_000, 30));
        SpinWait.SpinUntil(
            () => service.GetState(ModelRole.Orchestrator).Phase == ModelDownloadPhase.Downloading,
            TimeSpan.FromSeconds(1));
        var state = service.GetState(ModelRole.Orchestrator);

        // Assert
        Assert.Equal(ModelDownloadPhase.Downloading, state.Phase);
        Assert.Equal(30d, state.PercentComplete);
        Assert.Null(state.Error);
    }

    [Theory]
    [InlineData(-10d, 0d)]
    [InlineData(120d, 100d)]
    [InlineData(double.PositiveInfinity, 0d)]
    [InlineData(double.NaN, 0d)]
    public void CreateProgressReporter_ClampsPercentToValidRange(double reportedPercent, double expectedPercent)
    {
        // Arrange
        var service = new ModelDownloadProgressStateService();
        var reporter = service.CreateProgressReporter(ModelRole.ComputerUse, "computer-model");

        // Act
        reporter.Report(new ModelDownloadProgress("model.onnx", 500, 1_000, reportedPercent));
        SpinWait.SpinUntil(
            () => service.GetState(ModelRole.ComputerUse).Phase == ModelDownloadPhase.Downloading,
            TimeSpan.FromSeconds(1));
        var state = service.GetState(ModelRole.ComputerUse);

        // Assert
        Assert.Equal(expectedPercent, state.PercentComplete);
        Assert.Equal(ModelDownloadPhase.Downloading, state.Phase);
    }
}
