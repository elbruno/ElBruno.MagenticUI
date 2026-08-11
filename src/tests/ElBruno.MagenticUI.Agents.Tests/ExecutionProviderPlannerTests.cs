using ElBruno.LocalLLMs;
using ElBruno.LocalLLMs.Diagnostics;
using ElBruno.MagenticUI.Agents.Configuration;

namespace ElBruno.MagenticUI.Agents.Tests;

public sealed class ExecutionProviderPlannerTests
{
    [Fact]
    public void UnavailableProvider_FallsBackToAutoAndExplainsWhy()
    {
        // Arrange
        var diagnostics = BuildDiagnostics(new ExecutionProviderDiagnostic
        {
            Provider = ExecutionProvider.Cuda,
            Status = ExecutionProviderDiagnosticStatus.Unavailable,
            IsAvailable = false,
            Reason = "CUDA 13 runtime was not found.",
            Suggestion = "Install the CUDA 13 runtime."
        });

        // Act
        var plan = ExecutionProviderPlanner.Plan(ExecutionProvider.Cuda, diagnostics);

        // Assert
        Assert.True(plan.Fallback);
        Assert.Equal(ExecutionProvider.Cuda, plan.Requested);
        Assert.Equal(ExecutionProvider.Auto, plan.Effective);
        Assert.Contains("CUDA 13 runtime was not found.", plan.Detail);
        Assert.Contains("Install the CUDA 13 runtime.", plan.Detail);
    }

    [Fact]
    public void AvailableProvider_IsUsedAsRequested()
    {
        // Arrange
        var diagnostics = BuildDiagnostics(new ExecutionProviderDiagnostic
        {
            Provider = ExecutionProvider.Cuda,
            Status = ExecutionProviderDiagnosticStatus.Available,
            IsAvailable = true,
            Reason = "CUDA 13 runtime detected."
        });

        // Act
        var plan = ExecutionProviderPlanner.Plan(ExecutionProvider.Cuda, diagnostics);

        // Assert
        Assert.False(plan.Fallback);
        Assert.Equal(ExecutionProvider.Cuda, plan.Effective);
    }

    [Fact]
    public void UnknownProviderStatus_DoesNotForceFallback()
    {
        // Arrange
        // Unknown means the library could not prove the provider is broken. It preflights again
        // at load time and degrades safely, so downgrading here would needlessly lose the GPU.
        var diagnostics = BuildDiagnostics(new ExecutionProviderDiagnostic
        {
            Provider = ExecutionProvider.DirectML,
            Status = ExecutionProviderDiagnosticStatus.Unknown,
            IsAvailable = false
        });

        // Act
        var plan = ExecutionProviderPlanner.Plan(ExecutionProvider.DirectML, diagnostics);

        // Assert
        Assert.False(plan.Fallback);
        Assert.Equal(ExecutionProvider.DirectML, plan.Effective);
    }

    [Fact]
    public void MissingDiagnostic_KeepsRequestedProvider()
    {
        // Arrange
        var diagnostics = BuildDiagnostics();

        // Act
        var plan = ExecutionProviderPlanner.Plan(ExecutionProvider.Cuda, diagnostics);

        // Assert
        Assert.False(plan.Fallback);
        Assert.Equal(ExecutionProvider.Cuda, plan.Effective);
    }

    [Fact]
    public void Auto_UsesTheLibraryResolvedProvider()
    {
        // Arrange
        var diagnostics = new EnvironmentDiagnostics
        {
            AutoResolvedExecutionProviderKnown = true,
            AutoResolvedExecutionProvider = ExecutionProvider.Cpu,
            AutoResolvedExecutionDetails = "No accelerated provider passed preflight."
        };

        // Act
        var plan = ExecutionProviderPlanner.Plan(ExecutionProvider.Auto, diagnostics);

        // Assert
        Assert.False(plan.Fallback);
        Assert.Equal(ExecutionProvider.Cpu, plan.Effective);
        Assert.Contains("No accelerated provider passed preflight.", plan.Detail);
    }

    [Fact]
    public void Auto_WithUnknownResolution_StaysAuto()
    {
        // Arrange
        var diagnostics = new EnvironmentDiagnostics { AutoResolvedExecutionProviderKnown = false };

        // Act
        var plan = ExecutionProviderPlanner.Plan(ExecutionProvider.Auto, diagnostics);

        // Assert
        Assert.Equal(ExecutionProvider.Auto, plan.Effective);
        Assert.False(plan.Fallback);
    }

    private static EnvironmentDiagnostics BuildDiagnostics(
        params ExecutionProviderDiagnostic[] providers) =>
        new() { ProviderDiagnostics = providers };
}
