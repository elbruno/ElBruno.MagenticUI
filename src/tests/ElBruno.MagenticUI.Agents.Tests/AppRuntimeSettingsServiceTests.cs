using System.Text.Json;
using ElBruno.MagenticUI.App.Configuration;
using ElBruno.MagenticUI.App.ModelSettings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElBruno.MagenticUI.Agents.Tests;

public sealed class AppRuntimeSettingsServiceTests
{
    [Fact]
    public void GetCurrentSettings_UsesConfiguredValues()
    {
        // Arrange
        var root = CreateDirectoryPath("runtime-settings");
        using var context = CreateContext(root, orchestratorPath: root, computerUsePath: root);

        // Act
        var settings = context.Service.GetCurrentSettings();

        // Assert
        Assert.Equal(Path.GetFullPath(root), settings.OrchestratorModelPath);
        Assert.Equal(Path.GetFullPath(root), settings.ComputerUseModelPath);
        Assert.Equal(15, settings.MaxRounds);
        Assert.Equal(0, settings.TaskTimeoutSeconds);
        Assert.Equal(256, settings.MaxOutputTokens);
    }

    [Fact]
    public async Task SaveAsync_UpdatesAppSettingsFile()
    {
        // Arrange
        var root = CreateDirectoryPath("runtime-settings-save");
        var orchestratorPath = Path.Combine(root, "orchestrator");
        var computerPath = Path.Combine(root, "computer");
        Directory.CreateDirectory(orchestratorPath);
        Directory.CreateDirectory(computerPath);

        using var context = CreateContext(root, orchestratorPath: string.Empty, computerUsePath: string.Empty);

        // Act
        var result = await context.Service.SaveAsync(
            new RuntimeSettingsSnapshot(orchestratorPath, computerPath, MaxRounds: 20, TaskTimeoutSeconds: 120, MaxOutputTokens: 384));

        // Assert
        Assert.True(result.Succeeded);

        var json = await File.ReadAllTextAsync(context.AppSettingsPath);
        using var document = JsonDocument.Parse(json);
        var localLlm = document.RootElement.GetProperty("LocalLLMs");
        Assert.Equal(Path.GetFullPath(orchestratorPath), localLlm.GetProperty("ModelPath").GetString());
        Assert.Equal(Path.GetFullPath(computerPath), localLlm.GetProperty("ComputerModelPath").GetString());
        Assert.Equal(20, localLlm.GetProperty("MaxRounds").GetInt32());
        Assert.Equal(120, localLlm.GetProperty("TaskTimeoutSeconds").GetInt32());
        Assert.Equal(384, localLlm.GetProperty("MaxOutputTokens").GetInt32());

        var nestedModels = localLlm.GetProperty("Models");
        Assert.Equal(Path.GetFullPath(orchestratorPath), nestedModels.GetProperty("Orchestrator").GetProperty("ModelPath").GetString());
        Assert.Equal(Path.GetFullPath(computerPath), nestedModels.GetProperty("ComputerUse").GetProperty("ModelPath").GetString());
    }

    [Fact]
    public async Task SaveAsync_RejectsMissingModelPath()
    {
        // Arrange
        var root = CreateDirectoryPath("runtime-settings-invalid");
        using var context = CreateContext(root, orchestratorPath: string.Empty, computerUsePath: string.Empty);

        // Act
        var result = await context.Service.SaveAsync(
            new RuntimeSettingsSnapshot(
                Path.Combine(root, "missing-orchestrator"),
                string.Empty,
                MaxRounds: 15,
                TaskTimeoutSeconds: 0,
                MaxOutputTokens: 256));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains("does not exist", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static TestContext CreateContext(string root, string orchestratorPath, string computerUsePath)
    {
        var appSettingsPath = Path.Combine(root, "appsettings.json");
        File.WriteAllText(
            appSettingsPath,
            """
            {
              "LocalLLMs": {
                "ModelPath": "",
                "ComputerModelPath": "",
                "MaxRounds": 15,
                "TaskTimeoutSeconds": 0,
                "MaxOutputTokens": 256,
                "Models": {
                  "Orchestrator": {
                    "ModelPath": "",
                    "ModelName": "magentic-brain"
                  },
                  "ComputerUse": {
                    "ModelPath": "",
                    "ModelName": "fara1.5-9b"
                  }
                }
              }
            }
            """);

        var settings = new Dictionary<string, string?>
        {
            ["LocalLLMs:Models:Orchestrator:ModelPath"] = orchestratorPath,
            ["LocalLLMs:Models:ComputerUse:ModelPath"] = computerUsePath,
            ["LocalLLMs:MaxRounds"] = "15",
            ["LocalLLMs:TaskTimeoutSeconds"] = "0",
            ["LocalLLMs:MaxOutputTokens"] = "256"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var service = new AppRuntimeSettingsService(
            configuration,
            new TestHostEnvironment(root),
            new PathSafetyService(),
            LoggerFactory.Create(_ => { }).CreateLogger<AppRuntimeSettingsService>());

        return new TestContext(service, appSettingsPath, root);
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

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "ElBruno.MagenticUI.Tests";
        public string ContentRootPath { get; set; } = contentRootPath;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class TestContext(AppRuntimeSettingsService service, string appSettingsPath, string rootPath) : IDisposable
    {
        public AppRuntimeSettingsService Service { get; } = service;
        public string AppSettingsPath { get; } = appSettingsPath;
        private string RootPath { get; } = rootPath;

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
                Directory.Delete(RootPath, recursive: true);
        }
    }
}
