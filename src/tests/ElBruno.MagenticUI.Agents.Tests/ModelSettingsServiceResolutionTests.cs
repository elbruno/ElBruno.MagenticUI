using ElBruno.LocalLLMs;
using ElBruno.MagenticUI.App.ModelDownloadProgress;
using ElBruno.MagenticUI.App.ModelSettings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElBruno.MagenticUI.Agents.Tests;

public sealed class ModelSettingsServiceResolutionTests
{
    [Fact]
    public void BuildLocalLlmOptions_UsesExplicitPath_WhenConfigured()
    {
        // Arrange
        var cacheRoot = CreateDirectoryPath("cache");
        var explicitModelPath = Path.Combine(cacheRoot, "orchestrator-model");
        Directory.CreateDirectory(explicitModelPath);
        using var context = CreateContext(cacheRoot, explicitModelPath, explicitModelPath);

        // Act
        var options = context.Service.BuildLocalLlmOptions(ModelRole.Orchestrator);

        // Assert
        Assert.Equal(Path.GetFullPath(explicitModelPath), options.ModelPath);
    }

    [Fact]
    public void BuildLocalLlmOptions_UsesConfiguredModelAndCache_WhenPathIsMissing()
    {
        // Arrange
        var cacheRoot = CreateDirectoryPath("cache");
        using var context = CreateContext(cacheRoot, string.Empty, string.Empty);

        // Act
        var options = context.Service.BuildLocalLlmOptions(ModelRole.Orchestrator);

        // Assert
        Assert.True(options.EnsureModelDownloaded);
        Assert.NotNull(options.Model);
        Assert.Equal(KnownModels.MagenticBrain.Id, options.Model.Id);
        Assert.Equal(Path.GetFullPath(cacheRoot), options.CacheDirectory);
    }

    [Fact]
    public void GetModelEntry_UsesFallbackPathKeys_WhenRoleSectionPathIsEmpty()
    {
        // Arrange
        var cacheRoot = CreateDirectoryPath("cache");
        var fallbackModelPath = Path.Combine(cacheRoot, "fallback-model");
        Directory.CreateDirectory(fallbackModelPath);
        using var context = CreateContext(cacheRoot, null, fallbackModelPath);

        // Act
        var entry = context.Service.GetModelEntry(ModelRole.Orchestrator);

        // Assert
        Assert.True(entry.UsesExplicitPath);
        Assert.Equal(Path.GetFullPath(fallbackModelPath), entry.EffectiveModelPath);
        Assert.True(entry.IsPresent);
    }

    [Fact]
    public void TryResolveSafeModelPath_RejectsPathOutsideModelStorageRoots()
    {
        // Arrange
        var cacheRoot = CreateDirectoryPath("cache");
        var modelPath = Path.Combine(cacheRoot, "orchestrator-model");
        var outsidePath = CreateDirectoryPath("outside");
        Directory.CreateDirectory(modelPath);
        using var context = CreateContext(cacheRoot, modelPath, modelPath, outsidePath);

        // Act
        var resolved = context.Service.TryResolveSafeModelPath(outsidePath, out var normalizedPath, out var statusText);

        // Assert
        Assert.False(resolved);
        Assert.Equal(string.Empty, normalizedPath);
        Assert.Contains("outside allowed roots", statusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetModelEntry_UsesNonEmptyDefaultCacheDirectory_WhenCacheConfigIsBlank()
    {
        // Arrange
        var settings = new Dictionary<string, string?>
        {
            ["LocalLLMs:CacheDirectory"] = string.Empty,
            ["LocalLLMs:Models:Orchestrator:ModelPath"] = string.Empty,
            ["LocalLLMs:Models:Orchestrator:ModelName"] = KnownModels.MagenticBrain.Id,
            ["LocalLLMs:Models:ComputerUse:ModelPath"] = string.Empty,
            ["LocalLLMs:Models:ComputerUse:ModelName"] = KnownModels.Fara15_9B.Id
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var service = new ModelSettingsService(
            configuration,
            new TestHostEnvironment(),
            new PathSafetyService(),
            new ModelDownloadProgressStateService(),
            new TestModelFolderLauncher(),
            LoggerFactory.Create(_ => { }));

        // Act
        var entry = service.GetModelEntry(ModelRole.Orchestrator);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(entry.CacheDirectory));
        Assert.True(Path.IsPathRooted(entry.CacheDirectory));
    }

    private static TestContext CreateContext(
        string cacheDirectory,
        string? roleSectionModelPath,
        string? fallbackModelPath,
        params string[] additionalCleanupPaths)
    {
        var settings = new Dictionary<string, string?>
        {
            ["LocalLLMs:CacheDirectory"] = cacheDirectory,
            ["LocalLLMs:Models:Orchestrator:ModelName"] = KnownModels.MagenticBrain.Id,
            ["LocalLLMs:Models:ComputerUse:ModelPath"] = Path.Combine(cacheDirectory, "computer-model"),
            ["LocalLLMs:Models:ComputerUse:ModelName"] = KnownModels.Fara15_9B.Id
        };
        if (roleSectionModelPath is not null)
            settings["LocalLLMs:Models:Orchestrator:ModelPath"] = roleSectionModelPath;
        if (fallbackModelPath is not null)
            settings["LocalLLMs:ModelPath"] = fallbackModelPath;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var service = new ModelSettingsService(
            configuration,
            new TestHostEnvironment(),
            new PathSafetyService(),
            new ModelDownloadProgressStateService(),
            new TestModelFolderLauncher(),
            LoggerFactory.Create(_ => { }));

        return new TestContext(service, [cacheDirectory, ..additionalCleanupPaths]);
    }

    private static string CreateDirectoryPath(string prefix)
    {
        var directoryPath = Path.Combine(
            Environment.CurrentDirectory,
            "test-artifacts",
            $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "ElBruno.MagenticUI.Tests";
        public string ContentRootPath { get; set; } = Environment.CurrentDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class TestModelFolderLauncher : IModelFolderLauncher
    {
        public bool TryOpen(string folderPath, out string errorMessage)
        {
            errorMessage = string.Empty;
            return true;
        }
    }

    private sealed class TestContext : IDisposable
    {
        public TestContext(ModelSettingsService service, IReadOnlyList<string> rootPaths)
        {
            Service = service;
            RootPaths = rootPaths;
        }

        public ModelSettingsService Service { get; }
        private IReadOnlyList<string> RootPaths { get; }

        public void Dispose()
        {
            foreach (var rootPath in RootPaths)
            {
                if (Directory.Exists(rootPath))
                    Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
