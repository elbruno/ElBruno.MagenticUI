using ElBruno.LocalLLMs;
using ElBruno.LocalLLMs.Diagnostics;
using ElBruno.MagenticUI.App.ModelDownloadProgress;
using Microsoft.Extensions.Logging;

namespace ElBruno.MagenticUI.App.ModelSettings;

public sealed class ModelSettingsService : IModelSettingsService
{
    private static readonly RoleConfig[] RoleConfigs =
    [
        new(
            ModelRole.Orchestrator,
            "Orchestrator",
            "LocalLLMs:Models:Orchestrator",
            "LocalLLMs:ModelPath",
            "LocalLLMs:ModelName",
            KnownModels.MagenticBrain.Id),
        new(
            ModelRole.ComputerUse,
            "ComputerUse",
            "LocalLLMs:Models:ComputerUse",
            "LocalLLMs:ComputerModelPath",
            "LocalLLMs:ComputerModelName",
            KnownModels.Fara15_9B.Id)
    ];

    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IPathSafetyService _pathSafetyService;
    private readonly IModelDownloadProgressStateService _modelDownloadProgressStateService;
    private readonly IModelFolderLauncher _modelFolderLauncher;
    private readonly ILoggerFactory _loggerFactory;

    public ModelSettingsService(
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        IPathSafetyService pathSafetyService,
        IModelDownloadProgressStateService modelDownloadProgressStateService,
        IModelFolderLauncher modelFolderLauncher,
        ILoggerFactory loggerFactory)
    {
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
        _pathSafetyService = pathSafetyService;
        _modelDownloadProgressStateService = modelDownloadProgressStateService;
        _modelFolderLauncher = modelFolderLauncher;
        _loggerFactory = loggerFactory;
    }

    public IReadOnlyList<ModelSettingsEntry> GetModelEntries()
    {
        var cacheDirectory = ResolveCacheDirectory();
        return RoleConfigs.Select(config => BuildModelSettingsEntry(config, cacheDirectory)).ToArray();
    }

    public ModelSettingsEntry GetModelEntry(ModelRole role)
    {
        var roleConfig = GetRoleConfig(role);
        return BuildModelSettingsEntry(roleConfig, ResolveCacheDirectory());
    }

    public LocalLLMsOptions BuildLocalLlmOptions(ModelRole role)
    {
        var roleConfig = GetRoleConfig(role);
        var options = new LocalLLMsOptions
        {
            ExecutionProvider = _configuration.GetValue("LocalLLMs:ExecutionProvider", ExecutionProvider.Cpu),
            CaptureTelemetryContent = _hostEnvironment.IsDevelopment()
        };

        var explicitModelPath = ResolveConfiguredModelPath(roleConfig);
        if (!string.IsNullOrWhiteSpace(explicitModelPath))
        {
            options.ModelPath = explicitModelPath;
            return options;
        }

        var requestedModelId = ResolveConfiguredModelId(roleConfig);
        options.Model = KnownModels.FindById(requestedModelId)
            ?? throw new InvalidOperationException($"Unknown LocalLLMs model '{requestedModelId}'.");
        options.EnsureModelDownloaded = true;

        var cacheDirectory = ResolveCacheDirectory();
        if (!string.IsNullOrWhiteSpace(cacheDirectory))
            options.CacheDirectory = cacheDirectory;

        return options;
    }

    public IReadOnlyList<string> GetModelStorageRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var cacheDirectory = _pathSafetyService.NormalizeAbsolutePath(ResolveCacheDirectory());
        if (cacheDirectory is not null)
            roots.Add(cacheDirectory);

        foreach (var roleConfig in RoleConfigs)
        {
            var explicitPath = _pathSafetyService.NormalizeAbsolutePath(ResolveConfiguredModelPath(roleConfig));
            if (explicitPath is not null)
                roots.Add(explicitPath);
        }

        return roots.ToArray();
    }

    public bool TryResolveSafeModelPath(string path, out string normalizedPath, out string statusText)
        => _pathSafetyService.TryResolveSafePath(path, GetModelStorageRoots(), out normalizedPath, out statusText);

    public ModelFileOperationResult OpenModelFolder(ModelRole role)
    {
        if (!TryResolveModelPathForRole(role, out var entry, out var normalizedPath, out var failureResult))
            return failureResult;

        if (!Directory.Exists(normalizedPath))
            return new(false, $"Cannot open folder for {entry.RoleDisplayName}: model path does not exist.");

        if (!_modelFolderLauncher.TryOpen(normalizedPath, out var errorMessage))
            return new(false, $"Cannot open folder for {entry.RoleDisplayName}: {errorMessage}");

        return new(true, $"Opened folder for {entry.RoleDisplayName}: {normalizedPath}");
    }

    public ModelFileOperationResult DeleteModelFiles(ModelRole role, bool isConfirmed)
    {
        if (!isConfirmed)
            return new(false, "Delete cancelled: explicit confirmation is required.");

        var entry = GetModelEntry(role);
        var downloadState = _modelDownloadProgressStateService.GetState(role);
        if (downloadState.Phase == ModelDownloadPhase.Downloading)
            return new(false, $"Cannot delete {entry.RoleDisplayName}: model is currently downloading.");

        if (!TryResolveModelPathForRole(role, out entry, out var normalizedPath, out var failureResult))
            return failureResult;

        if (!Directory.Exists(normalizedPath))
            return new(false, $"Cannot delete {entry.RoleDisplayName}: model path does not exist.");

        if (IsDeleteTargetCacheRoot(entry.CacheDirectory, normalizedPath))
            return new(false, $"Cannot delete {entry.RoleDisplayName}: refusing to delete the configured cache root.");

        if (IsDeleteTargetSharedWithOtherRoles(role, normalizedPath))
            return new(false, $"Cannot delete {entry.RoleDisplayName}: directory overlaps with another model path.");

        try
        {
            RemoveReadOnlyAttributes(normalizedPath);
            Directory.Delete(normalizedPath, recursive: true);
            _modelDownloadProgressStateService.Initialize(role, entry.ModelId);
            return new(true, $"Deleted model files for {entry.RoleDisplayName}: {normalizedPath}");
        }
        catch (Exception ex)
        {
            return new(false, $"Failed to delete model files for {entry.RoleDisplayName}: {ex.Message}");
        }
    }

    public async Task<ModelFileOperationResult> DownloadModelAsync(
        ModelRole role,
        CancellationToken cancellationToken = default)
    {
        var entry = GetModelEntry(role);
        if (entry.IsPresent)
            return new(true, $"{entry.RoleDisplayName} model is already present.");

        if (entry.UsesExplicitPath)
            return new(false, $"Cannot download {entry.RoleDisplayName}: an explicit model path is configured. Add files to that folder or clear ModelPath.");

        var currentState = _modelDownloadProgressStateService.GetState(role);
        if (currentState.Phase == ModelDownloadPhase.Downloading)
            return new(false, $"{entry.RoleDisplayName} model download is already in progress.");

        LocalLLMsOptions options;
        try
        {
            options = BuildLocalLlmOptions(role);
        }
        catch (Exception ex)
        {
            return new(false, $"Cannot start download for {entry.RoleDisplayName}: {ex.Message}");
        }

        if (!options.EnsureModelDownloaded || options.Model is null)
            return new(false, $"Cannot start download for {entry.RoleDisplayName}: no downloadable model is configured.");

        _modelDownloadProgressStateService.Initialize(role, entry.ModelId);
        var progress = _modelDownloadProgressStateService.CreateProgressReporter(role, entry.ModelId);
        progress.Report(new ElBruno.LocalLLMs.ModelDownloadProgress(string.Empty, 0, 0, 0));

        try
        {
            switch (role)
            {
                case ModelRole.Orchestrator:
                    await using (await LocalChatClient.CreateAsync(options, progress, _loggerFactory, cancellationToken))
                    {
                    }
                    break;
                case ModelRole.ComputerUse:
                    await using (await LocalVisionChatClient.CreateAsync(options, progress, cancellationToken))
                    {
                    }
                    break;
                default:
                    return new(false, $"Cannot start download for {entry.RoleDisplayName}: unsupported model role.");
            }

            _modelDownloadProgressStateService.MarkCompleted(role, entry.ModelId);
            var refreshedEntry = GetModelEntry(role);
            if (!refreshedEntry.IsPresent)
            {
                _modelDownloadProgressStateService.MarkFailed(
                    role,
                    entry.ModelId,
                    $"Model files were not found after download. Expected path: {refreshedEntry.EffectiveModelPath}");
                return new(false, $"Download finished but model files were not found for {entry.RoleDisplayName}. Expected path: {refreshedEntry.EffectiveModelPath}");
            }

            return new(true, $"Downloaded model for {entry.RoleDisplayName}.");
        }
        catch (OperationCanceledException)
        {
            _modelDownloadProgressStateService.MarkFailed(role, entry.ModelId, "Download cancelled.");
            return new(false, $"Download cancelled for {entry.RoleDisplayName}.");
        }
        catch (Exception ex)
        {
            _modelDownloadProgressStateService.MarkFailed(role, entry.ModelId, ex.Message);
            return new(false, $"Failed to download model for {entry.RoleDisplayName}: {ex.Message}");
        }
    }

    private ModelSettingsEntry BuildModelSettingsEntry(RoleConfig roleConfig, string cacheDirectory)
    {
        var configuredPath = ResolveConfiguredModelPath(roleConfig);
        var requestedModelId = ResolveConfiguredModelId(roleConfig);
        var modelDefinition = KnownModels.FindById(requestedModelId);

        var usesExplicitPath = !string.IsNullOrWhiteSpace(configuredPath);
        var explicitPath = _pathSafetyService.NormalizeAbsolutePath(configuredPath);
        var effectiveModelPath = usesExplicitPath
            ? explicitPath ?? configuredPath ?? string.Empty
            : ResolveCachedModelPath(modelDefinition, cacheDirectory);

        var isPresent = Directory.Exists(effectiveModelPath);
        var statusText = BuildStatusText(usesExplicitPath, modelDefinition, isPresent);

        return new ModelSettingsEntry(
            roleConfig.Role,
            roleConfig.DisplayName,
            modelDefinition?.Id ?? requestedModelId,
            modelDefinition?.DisplayName ?? requestedModelId,
            effectiveModelPath,
            usesExplicitPath,
            isPresent,
            statusText,
            cacheDirectory);
    }

    private string ResolveCacheDirectory()
    {
        var configuredCacheDirectory = _configuration["LocalLLMs:CacheDirectory"];
        if (!string.IsNullOrWhiteSpace(configuredCacheDirectory))
            return _pathSafetyService.NormalizeAbsolutePath(configuredCacheDirectory) ?? configuredCacheDirectory;

        var diagnosticsCacheDirectory = new EnvironmentDiagnostics().CacheDirectory;
        if (!string.IsNullOrWhiteSpace(diagnosticsCacheDirectory))
            return _pathSafetyService.NormalizeAbsolutePath(diagnosticsCacheDirectory) ?? diagnosticsCacheDirectory;

        var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var fallbackCacheDirectory = string.IsNullOrWhiteSpace(localAppDataPath)
            ? string.Empty
            : Path.Combine(localAppDataPath, "ElBruno", "LocalLLMs", "models");

        return _pathSafetyService.NormalizeAbsolutePath(fallbackCacheDirectory)
            ?? fallbackCacheDirectory
            ?? string.Empty;
    }

    private string ResolveConfiguredModelPath(RoleConfig roleConfig)
        => _configuration[$"{roleConfig.SectionKey}:ModelPath"]
            ?? _configuration[roleConfig.FallbackModelPathKey]
            ?? string.Empty;

    private string ResolveConfiguredModelId(RoleConfig roleConfig)
        => _configuration[$"{roleConfig.SectionKey}:ModelName"]
            ?? _configuration[roleConfig.FallbackModelNameKey]
            ?? roleConfig.DefaultModelId;

    private bool TryResolveModelPathForRole(
        ModelRole role,
        out ModelSettingsEntry entry,
        out string normalizedPath,
        out ModelFileOperationResult failureResult)
    {
        entry = GetModelEntry(role);
        normalizedPath = string.Empty;
        failureResult = new(false, string.Empty);

        if (string.IsNullOrWhiteSpace(entry.EffectiveModelPath))
        {
            failureResult = new(false, $"Cannot resolve model path for {entry.RoleDisplayName}: effective model path is empty.");
            return false;
        }

        if (!TryResolveSafeModelPath(entry.EffectiveModelPath, out normalizedPath, out var statusText))
        {
            failureResult = new(false, $"Cannot access path for {entry.RoleDisplayName}: {statusText}");
            return false;
        }

        return true;
    }

    private bool IsDeleteTargetCacheRoot(string cacheDirectory, string deleteTargetPath)
    {
        var normalizedCacheDirectory = _pathSafetyService.NormalizeAbsolutePath(cacheDirectory);
        if (normalizedCacheDirectory is null)
            return false;

        return string.Equals(normalizedCacheDirectory, deleteTargetPath, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsDeleteTargetSharedWithOtherRoles(ModelRole role, string deleteTargetPath)
    {
        foreach (var otherEntry in GetModelEntries().Where(entry => entry.Role != role))
        {
            if (!_pathSafetyService.TryResolveSafePath(otherEntry.EffectiveModelPath, GetModelStorageRoots(), out var otherPath, out _))
                continue;

            if (_pathSafetyService.IsPathUnderRoot(otherPath, deleteTargetPath))
                return true;
        }

        return false;
    }

    private static void RemoveReadOnlyAttributes(string path)
    {
        foreach (var filePath in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(filePath, FileAttributes.Normal);

        foreach (var directoryPath in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                     .OrderByDescending(currentPath => currentPath.Length))
            File.SetAttributes(directoryPath, FileAttributes.Normal);

        File.SetAttributes(path, FileAttributes.Normal);
    }

    private string ResolveCachedModelPath(ModelDefinition? modelDefinition, string cacheDirectory)
    {
        if (modelDefinition is null)
            return cacheDirectory;

        var candidates = new List<string>();
        void AddCandidate(string candidate)
        {
            var normalized = _pathSafetyService.NormalizeAbsolutePath(candidate);
            if (normalized is not null && !candidates.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                candidates.Add(normalized);
        }

        var modelSubPath = modelDefinition.ModelSubPath ?? string.Empty;
        AddCandidate(Path.Combine(cacheDirectory, modelDefinition.HuggingFaceRepoId.Replace('/', Path.DirectorySeparatorChar), modelSubPath));
        AddCandidate(Path.Combine(cacheDirectory, modelDefinition.HuggingFaceRepoId.Replace('/', '_'), modelSubPath));
        AddCandidate(Path.Combine(cacheDirectory, modelDefinition.HuggingFaceRepoId.Replace('/', '-'), modelSubPath));
        AddCandidate(Path.Combine(cacheDirectory, modelDefinition.Id, modelSubPath));
        AddCandidate(Path.Combine(cacheDirectory, modelDefinition.HuggingFaceRepoId.Split('/').Last(), modelSubPath));

        return candidates.FirstOrDefault(Directory.Exists)
            ?? candidates.FirstOrDefault()
            ?? cacheDirectory;
    }

    private static string BuildStatusText(bool usesExplicitPath, ModelDefinition? modelDefinition, bool isPresent)
    {
        if (usesExplicitPath)
            return isPresent
                ? "Configured path is available."
                : "Configured path was not found.";

        if (modelDefinition is null)
            return "Configured model name is not recognized.";

        return isPresent
            ? "Model is present in the local cache."
            : "Model is not present yet; it will download on first use.";
    }

    private static RoleConfig GetRoleConfig(ModelRole role)
        => RoleConfigs.First(config => config.Role == role);

    private sealed record RoleConfig(
        ModelRole Role,
        string DisplayName,
        string SectionKey,
        string FallbackModelPathKey,
        string FallbackModelNameKey,
        string DefaultModelId);
}
