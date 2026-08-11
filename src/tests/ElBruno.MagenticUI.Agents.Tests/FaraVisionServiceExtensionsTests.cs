using ElBruno.LocalLLMs;
using ElBruno.MagenticUI.Agents.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ElBruno.MagenticUI.Agents.Tests;

public sealed class FaraVisionServiceExtensionsTests
{
    [Fact]
    public void Defaults_EnableAutoDownloadForSupportedLocalLLMsVersion()
    {
        var options = new FaraVisionOptions();

        Assert.True(options.EnsureModelDownloaded);
    }

    [Fact]
    public void EmptyModelPath_UsesAutoDownloadWithoutPassingEmptyPath()
    {
        var services = new ServiceCollection();

        services.AddFaraVisionLLM(new FaraVisionOptions
        {
            ModelPath = "  ",
            EnsureModelDownloaded = true
        });

        var options = services.BuildServiceProvider()
            .GetRequiredKeyedService<LocalLLMsOptions>(FaraVisionServiceExtensions.ServiceKey);

        Assert.Null(options.ModelPath);
        Assert.True(options.EnsureModelDownloaded);
        Assert.Equal(KnownModels.Fara15_9B, options.Model);
    }

    [Fact]
    public void EmptyModelPath_PreservesAutoDownloadSetting()
    {
        var services = new ServiceCollection();

        services.AddFaraVisionLLM(new FaraVisionOptions
        {
            ModelPath = string.Empty,
            EnsureModelDownloaded = true
        });

        var options = services.BuildServiceProvider()
            .GetRequiredKeyedService<LocalLLMsOptions>(FaraVisionServiceExtensions.ServiceKey);

        Assert.Null(options.ModelPath);
        Assert.True(options.EnsureModelDownloaded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyCacheDirectory_IsNormalizedToNull(string configured)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddFaraVisionLLM(new FaraVisionOptions
        {
            CacheDirectory = configured,
            EnsureModelDownloaded = true
        });

        var options = services.BuildServiceProvider()
            .GetRequiredKeyedService<LocalLLMsOptions>(FaraVisionServiceExtensions.ServiceKey);

        // Assert
        // An empty string makes the downloader build a *relative* cache path next to the app,
        // which re-downloads the ~10 GB model on every request instead of reusing the cache.
        Assert.Null(options.CacheDirectory);
    }

    [Fact]
    public void ExplicitCacheDirectory_IsTrimmedAndPreserved()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddFaraVisionLLM(new FaraVisionOptions
        {
            CacheDirectory = @"  C:\Models\cache  ",
            EnsureModelDownloaded = true
        });

        var options = services.BuildServiceProvider()
            .GetRequiredKeyedService<LocalLLMsOptions>(FaraVisionServiceExtensions.ServiceKey);

        // Assert
        Assert.Equal(@"C:\Models\cache", options.CacheDirectory);
    }

    [Fact]
    public void ExplicitModelPath_IsTrimmedAndPreserved()
    {
        var services = new ServiceCollection();

        services.AddFaraVisionLLM(new FaraVisionOptions
        {
            ModelPath = @"  C:\Models\Fara1.5-9B  ",
            EnsureModelDownloaded = false
        });

        var options = services.BuildServiceProvider()
            .GetRequiredKeyedService<LocalLLMsOptions>(FaraVisionServiceExtensions.ServiceKey);

        Assert.Equal(@"C:\Models\Fara1.5-9B", options.ModelPath);
        Assert.False(options.EnsureModelDownloaded);
    }

    [Fact]
    public void ExecutionProviderPlan_IsRegisteredForTheUi()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddFaraVisionLLM(new FaraVisionOptions { ExecutionProvider = "Cuda" });

        var provider = services.BuildServiceProvider();
        var plan = provider.GetRequiredKeyedService<ExecutionProviderPlan>(
            FaraVisionServiceExtensions.ServiceKey);
        var options = provider.GetRequiredKeyedService<LocalLLMsOptions>(
            FaraVisionServiceExtensions.ServiceKey);

        // Assert
        Assert.Equal(ExecutionProvider.Cuda, plan.Requested);
        Assert.Equal(plan.Effective, options.ExecutionProvider);
        Assert.False(string.IsNullOrWhiteSpace(plan.Detail));
    }

    [Fact]
    public void UnrecognizedExecutionProvider_FallsBackToAuto()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddFaraVisionLLM(new FaraVisionOptions { ExecutionProvider = "not-a-provider" });

        var plan = services.BuildServiceProvider()
            .GetRequiredKeyedService<ExecutionProviderPlan>(FaraVisionServiceExtensions.ServiceKey);

        // Assert
        Assert.Equal(ExecutionProvider.Auto, plan.Requested);
    }

    [Fact]
    public void MaxOutputTokens_DefaultsToASmallActionBudget()
    {
        // Arrange & Act
        var options = new FaraVisionOptions();

        // Assert
        // Predictions are a single short JSON action; a small budget keeps CPU inference from
        // generating up to MaxSequenceLength. Safe again since ElBruno.LocalLLMs 0.20.11.
        Assert.Equal(128, options.MaxOutputTokens);
    }
}
