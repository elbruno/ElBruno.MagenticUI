using ElBruno.MagenticUI.App.ModelDownloadProgress;
using ElBruno.MagenticUI.App.ModelSettings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElBruno.MagenticUI.Agents.Tests;

public sealed class ModelStatusServiceTests
{
    [Fact]
    public void GetStatuses_UsesSharedPresenceLogic_ForBothRoles()
    {
        // Arrange
        var root = CreateDirectoryPath("model-status");
        var orchestratorPath = Path.Combine(root, "magentic-brain");
        var computerPath = Path.Combine(root, "fara1.5-9b");
        Directory.CreateDirectory(orchestratorPath);
        Directory.CreateDirectory(computerPath);

        using var context = CreateContext(
            cacheDirectory: root,
            orchestratorModelPath: orchestratorPath,
            computerUseModelPath: computerPath);

        // Act
        var statuses = context.StatusService.GetStatuses();

        // Assert
        Assert.Equal(2, statuses.Count);
        Assert.All(statuses, status =>
        {
            Assert.True(status.Entry.IsPresent);
            Assert.Equal(ModelDownloadPhase.Completed, status.EffectivePhase);
            Assert.Equal(100d, status.DisplayPercent);
            Assert.False(status.CanDownload);
        });
    }

    [Fact]
    public void GetStatuses_ResetsCompletedToIdle_WhenFilesAreMissing()
    {
        // Arrange
        var root = CreateDirectoryPath("model-status-missing");
        var orchestratorPath = Path.Combine(root, "magentic-brain");
        var computerPath = Path.Combine(root, "fara1.5-9b");
        using var context = CreateContext(
            cacheDirectory: root,
            orchestratorModelPath: orchestratorPath,
            computerUseModelPath: computerPath);

        context.DownloadStateService.MarkCompleted(ModelRole.Orchestrator, "magentic-brain");

        // Act
        var status = context.StatusService.GetStatuses()
            .Single(current => current.Entry.Role == ModelRole.Orchestrator);

        // Assert
        Assert.False(status.Entry.IsPresent);
        Assert.Equal(ModelDownloadPhase.Idle, status.EffectivePhase);
        Assert.Equal(0d, status.DisplayPercent);
    }

    private static TestContext CreateContext(
        string cacheDirectory,
        string orchestratorModelPath,
        string computerUseModelPath)
    {
        var settings = new Dictionary<string, string?>
        {
            ["LocalLLMs:CacheDirectory"] = cacheDirectory,
            ["LocalLLMs:Models:Orchestrator:ModelPath"] = orchestratorModelPath,
            ["LocalLLMs:Models:ComputerUse:ModelPath"] = computerUseModelPath,
            ["LocalLLMs:Models:Orchestrator:ModelName"] = "magentic-brain",
            ["LocalLLMs:Models:ComputerUse:ModelName"] = "fara1.5-9b"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var downloadStateService = new ModelDownloadProgressStateService();
        var modelSettingsService = new ModelSettingsService(
            configuration,
            new TestHostEnvironment(),
            new PathSafetyService(),
            downloadStateService,
            new TestModelFolderLauncher(),
            LoggerFactory.Create(_ => { }));

        var statusService = new ModelStatusService(modelSettingsService, downloadStateService);
        return new TestContext(statusService, downloadStateService, cacheDirectory);
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
        public TestContext(
            ModelStatusService statusService,
            ModelDownloadProgressStateService downloadStateService,
            string rootPath)
        {
            StatusService = statusService;
            DownloadStateService = downloadStateService;
            RootPath = rootPath;
        }

        public ModelStatusService StatusService { get; }
        public ModelDownloadProgressStateService DownloadStateService { get; }
        private string RootPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
    }
}
