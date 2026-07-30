using System.Text.Json;
using System.Text.Json.Nodes;
using ElBruno.MagenticUI.App.ModelSettings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ElBruno.MagenticUI.App.Configuration;

public sealed class AppRuntimeSettingsService : IAppRuntimeSettingsService
{
    private const string AppSettingsFileName = "appsettings.json";

    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IPathSafetyService _pathSafetyService;
    private readonly ILogger<AppRuntimeSettingsService> _logger;

    public AppRuntimeSettingsService(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        IPathSafetyService pathSafetyService,
        ILogger<AppRuntimeSettingsService> logger)
    {
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
        _pathSafetyService = pathSafetyService;
        _logger = logger;
    }

    public RuntimeSettingsSnapshot GetCurrentSettings() =>
        new(
            GetStringValue("LocalLLMs:Models:Orchestrator:ModelPath")
                ?? GetStringValue("LocalLLMs:ModelPath")
                ?? string.Empty,
            GetStringValue("LocalLLMs:Models:ComputerUse:ModelPath")
                ?? GetStringValue("LocalLLMs:ComputerModelPath")
                ?? string.Empty,
            GetIntValue("LocalLLMs:MaxRounds", 15),
            GetIntValue("LocalLLMs:TaskTimeoutSeconds", 0),
            GetIntValue("LocalLLMs:MaxOutputTokens", 256));

    public async Task<RuntimeSettingsUpdateResult> SaveAsync(
        RuntimeSettingsSnapshot settings,
        CancellationToken ct = default)
    {
        if (settings.MaxRounds < 1)
            return new(false, "Max rounds must be at least 1.");

        if (settings.TaskTimeoutSeconds < 0)
            return new(false, "Task timeout cannot be negative.");

        if (settings.MaxOutputTokens < 1)
            return new(false, "Max output tokens must be at least 1.");

        if (!TryNormalizeExistingFolder(settings.OrchestratorModelPath, "Orchestrator", out var orchestratorPath, out var orchestratorError))
            return new(false, orchestratorError);

        if (!TryNormalizeExistingFolder(settings.ComputerUseModelPath, "ComputerUse", out var computerUsePath, out var computerUseError))
            return new(false, computerUseError);

        var appSettingsPath = Path.Combine(_hostEnvironment.ContentRootPath, AppSettingsFileName);
        if (!File.Exists(appSettingsPath))
            return new(false, $"Cannot update settings because '{AppSettingsFileName}' was not found.");

        JsonObject? root;
        try
        {
            var json = await File.ReadAllTextAsync(appSettingsPath, ct);
            root = JsonNode.Parse(json) as JsonObject;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read appsettings.json");
            return new(false, $"Failed to read {AppSettingsFileName}: {ex.Message}");
        }

        root ??= new JsonObject();

        var localLlm = GetOrCreateObject(root, "LocalLLMs");
        var models = GetOrCreateObject(localLlm, "Models");
        var orchestrator = GetOrCreateObject(models, "Orchestrator");
        var computerUse = GetOrCreateObject(models, "ComputerUse");

        SetString(localLlm, "ModelPath", orchestratorPath);
        SetString(localLlm, "ComputerModelPath", computerUsePath);
        SetNumber(localLlm, "MaxRounds", settings.MaxRounds);
        SetNumber(localLlm, "TaskTimeoutSeconds", settings.TaskTimeoutSeconds);
        SetNumber(localLlm, "MaxOutputTokens", settings.MaxOutputTokens);

        SetString(orchestrator, "ModelPath", orchestratorPath);
        SetString(computerUse, "ModelPath", computerUsePath);

        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var serialized = root.ToJsonString(options) + Environment.NewLine;
            await File.WriteAllTextAsync(appSettingsPath, serialized, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write appsettings.json");
            return new(false, $"Failed to save {AppSettingsFileName}: {ex.Message}");
        }

        return new(true, $"Saved runtime settings to {AppSettingsFileName}.");
    }

    private bool TryNormalizeExistingFolder(
        string? path,
        string roleName,
        out string normalizedPath,
        out string errorMessage)
    {
        normalizedPath = string.Empty;
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(path))
            return true;

        var candidate = _pathSafetyService.NormalizeAbsolutePath(path);
        if (candidate is null)
        {
            errorMessage = $"The {roleName} model path is not a valid file system path.";
            return false;
        }

        if (!Directory.Exists(candidate))
        {
            errorMessage = $"The {roleName} model path does not exist: {candidate}";
            return false;
        }

        normalizedPath = candidate;
        return true;
    }

    private static JsonObject GetOrCreateObject(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is JsonObject existing)
            return existing;

        var created = new JsonObject();
        parent[propertyName] = created;
        return created;
    }

    private static void SetString(JsonObject parent, string propertyName, string value)
        => parent[propertyName] = value;

    private static void SetNumber(JsonObject parent, string propertyName, int value)
        => parent[propertyName] = value;

    private string? GetStringValue(string key)
        => _configuration[key];

    private int GetIntValue(string key, int defaultValue)
        => _configuration.GetValue(key, defaultValue);
}
