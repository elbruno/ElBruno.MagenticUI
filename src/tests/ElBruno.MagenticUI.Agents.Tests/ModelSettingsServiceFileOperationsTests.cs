using ElBruno.MagenticUI.App.ModelDownloadProgress;
using ElBruno.MagenticUI.App.ModelSettings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace ElBruno.MagenticUI.Agents.Tests;

public sealed class ModelSettingsServiceFileOperationsTests
{
    [Fact]
    public void DeleteModelFiles_RequiresExplicitConfirmation()
    {
        // Arrange
        var cacheRoot = CreateDirectoryPath("cache");
        var modelPath = Path.Combine(cacheRoot, "orchestrator-model");
        Directory.CreateDirectory(modelPath);
        using var context = CreateContext(cacheRoot, modelPath);

        // Act
        var result = context.Service.DeleteModelFiles(ModelRole.Orchestrator, isConfirmed: false);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("confirmation", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(modelPath));
    }

    [Fact]
    public void DeleteModelFiles_BlocksWhileModelIsDownloading()
    {
        // Arrange
        var cacheRoot = CreateDirectoryPath("cache");
        var modelPath = Path.Combine(cacheRoot, "orchestrator-model");
        Directory.CreateDirectory(modelPath);
        using var context = CreateContext(cacheRoot, modelPath);
        var reporter = context.DownloadStateService.CreateProgressReporter(ModelRole.Orchestrator, "orchestrator-model");
        reporter.Report(new ElBruno.LocalLLMs.ModelDownloadProgress("weights.onnx", 1, 10, 10));
        SpinWait.SpinUntil(
            () => context.DownloadStateService.GetState(ModelRole.Orchestrator).Phase == ModelDownloadPhase.Downloading,
            TimeSpan.FromSeconds(1));

        // Act
        var result = context.Service.DeleteModelFiles(ModelRole.Orchestrator, isConfirmed: true);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("currently downloading", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(modelPath));
    }

    [Fact]
    public void DeleteModelFiles_RefusesCacheRootDeletion()
    {
        // Arrange
        var cacheRoot = CreateDirectoryPath("cache-root-model");
        Directory.CreateDirectory(cacheRoot);
        using var context = CreateContext(cacheRoot, cacheRoot);

        // Act
        var result = context.Service.DeleteModelFiles(ModelRole.Orchestrator, isConfirmed: true);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("cache root", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(cacheRoot));
    }

    [Fact]
    public void OpenModelFolder_UsesLauncherWhenPathIsSafe()
    {
        // Arrange
        var cacheRoot = CreateDirectoryPath("cache");
        var modelPath = Path.Combine(cacheRoot, "orchestrator-model");
        Directory.CreateDirectory(modelPath);
        using var context = CreateContext(
            cacheRoot,
            modelPath,
            launcher: new TestModelFolderLauncher(shouldSucceed: true),
            computerUsePath: null);

        // Act
        var result = context.Service.OpenModelFolder(ModelRole.Orchestrator);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(Path.GetFullPath(modelPath), context.FolderLauncher.LastPath);
    }

    [Fact]
    public void DeleteModelFiles_DeletesModelDirectory_WhenSafeAndConfirmed()
    {
        // Arrange
        var cacheRoot = CreateDirectoryPath("cache");
        var orchestratorPath = Path.Combine(cacheRoot, "orchestrator-model");
        var computerUsePath = Path.Combine(cacheRoot, "computer-model");
        Directory.CreateDirectory(orchestratorPath);
        Directory.CreateDirectory(computerUsePath);
        File.WriteAllText(Path.Combine(orchestratorPath, "weights.onnx"), "stub");
        using var context = CreateContext(cacheRoot, orchestratorPath, computerUsePath);

        // Act
        var result = context.Service.DeleteModelFiles(ModelRole.Orchestrator, isConfirmed: true);

        // Assert
        Assert.True(result.Succeeded);
        Assert.False(Directory.Exists(orchestratorPath));
        Assert.True(Directory.Exists(computerUsePath));
    }

    [Fact]
    public void DeleteModelFiles_RefusesWhenDirectoryOverlapsAnotherModelPath()
    {
        // Arrange
        var cacheRoot = CreateDirectoryPath("cache");
        var sharedRoot = Path.Combine(cacheRoot, "shared");
        var orchestratorPath = Path.Combine(sharedRoot, "orchestrator");
        var computerPath = Path.Combine(orchestratorPath, "computer");
        Directory.CreateDirectory(computerPath);
        using var context = CreateContext(cacheRoot, orchestratorPath, computerPath);

        // Act
        var result = context.Service.DeleteModelFiles(ModelRole.Orchestrator, isConfirmed: true);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("overlaps with another model path", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(orchestratorPath));
        Assert.True(Directory.Exists(computerPath));
    }

    private static TestContext CreateContext(string cacheDirectory, string orchestratorPath, string? computerUsePath = null)
        => CreateContext(cacheDirectory, orchestratorPath, new TestModelFolderLauncher(shouldSucceed: true), computerUsePath);

    private static TestContext CreateContext(
        string cacheDirectory,
        string orchestratorPath,
        TestModelFolderLauncher launcher,
        string? computerUsePath = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["LocalLLMs:CacheDirectory"] = cacheDirectory,
            ["LocalLLMs:Models:Orchestrator:ModelPath"] = orchestratorPath,
            ["LocalLLMs:Models:ComputerUse:ModelPath"] = computerUsePath ?? Path.Combine(cacheDirectory, "computer-model")
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var downloadService = new ModelDownloadProgressStateService();
        var service = new ModelSettingsService(
            configuration,
            new TestHostEnvironment(),
            new PathSafetyService(),
            downloadService,
            launcher);

        return new TestContext(service, downloadService, launcher, cacheDirectory);
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
        private readonly bool _shouldSucceed;

        public TestModelFolderLauncher(bool shouldSucceed)
            => _shouldSucceed = shouldSucceed;

        public string? LastPath { get; private set; }

        public bool TryOpen(string folderPath, out string errorMessage)
        {
            LastPath = folderPath;
            errorMessage = _shouldSucceed ? string.Empty : "launcher failure";
            return _shouldSucceed;
        }
    }

    private sealed class TestContext : IDisposable
    {
        public TestContext(
            ModelSettingsService service,
            ModelDownloadProgressStateService downloadStateService,
            TestModelFolderLauncher folderLauncher,
            string rootPath)
        {
            Service = service;
            DownloadStateService = downloadStateService;
            FolderLauncher = folderLauncher;
            RootPath = rootPath;
        }

        public ModelSettingsService Service { get; }
        public ModelDownloadProgressStateService DownloadStateService { get; }
        public TestModelFolderLauncher FolderLauncher { get; }
        private string RootPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
    }
}
