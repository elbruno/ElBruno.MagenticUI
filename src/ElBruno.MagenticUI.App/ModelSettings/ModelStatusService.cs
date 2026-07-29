using ElBruno.MagenticUI.App.ModelDownloadProgress;

namespace ElBruno.MagenticUI.App.ModelSettings;

public sealed class ModelStatusService : IModelStatusService
{
    private readonly IModelSettingsService _modelSettingsService;
    private readonly IModelDownloadProgressStateService _modelDownloadProgressStateService;

    public ModelStatusService(
        IModelSettingsService modelSettingsService,
        IModelDownloadProgressStateService modelDownloadProgressStateService)
    {
        _modelSettingsService = modelSettingsService;
        _modelDownloadProgressStateService = modelDownloadProgressStateService;
    }

    public IReadOnlyList<ModelStatusSnapshot> GetStatuses()
    {
        var entries = _modelSettingsService.GetModelEntries();
        var statesByRole = _modelDownloadProgressStateService.GetStates()
            .ToDictionary(state => state.Role, state => state);

        var statuses = new List<ModelStatusSnapshot>(entries.Count);
        foreach (var entry in entries.OrderBy(entry => entry.Role))
        {
            if (!statesByRole.TryGetValue(entry.Role, out var downloadState))
                downloadState = _modelDownloadProgressStateService.GetState(entry.Role);

            statuses.Add(CreateSnapshot(entry, downloadState));
        }

        return statuses;
    }

    private static ModelStatusSnapshot CreateSnapshot(ModelSettingsEntry entry, ModelDownloadState state)
    {
        var canDownload = !entry.IsPresent && !entry.UsesExplicitPath;
        var effectivePhase = GetEffectivePhase(entry, state);
        var displayPercent = GetDisplayPercent(effectivePhase, state.PercentComplete);
        var statusText = GetStatusText(entry, state, effectivePhase, canDownload);

        return new ModelStatusSnapshot(
            Entry: entry,
            DownloadState: state,
            EffectivePhase: effectivePhase,
            CanDownload: canDownload,
            DisplayPercent: displayPercent,
            StatusText: statusText);
    }

    private static ModelDownloadPhase GetEffectivePhase(ModelSettingsEntry entry, ModelDownloadState state)
    {
        if (state.Phase == ModelDownloadPhase.Downloading)
            return ModelDownloadPhase.Downloading;

        if (state.Phase == ModelDownloadPhase.Failed)
            return ModelDownloadPhase.Failed;

        if (entry.IsPresent)
            return ModelDownloadPhase.Completed;

        return ModelDownloadPhase.Idle;
    }

    private static double GetDisplayPercent(ModelDownloadPhase phase, double rawPercent)
        => phase switch
        {
            ModelDownloadPhase.Completed => 100d,
            ModelDownloadPhase.Downloading => Math.Clamp(rawPercent, 0d, 100d),
            _ => 0d
        };

    private static string GetStatusText(
        ModelSettingsEntry entry,
        ModelDownloadState state,
        ModelDownloadPhase effectivePhase,
        bool canDownload)
    {
        if (effectivePhase == ModelDownloadPhase.Downloading)
        {
            if (!string.IsNullOrWhiteSpace(state.CurrentFileName))
                return $"{state.StatusText} · {state.CurrentFileName}";

            return state.StatusText;
        }

        if (effectivePhase == ModelDownloadPhase.Failed)
            return state.Error ?? state.StatusText;

        if (effectivePhase == ModelDownloadPhase.Completed)
            return "Model is present in the local cache.";

        return canDownload
            ? "Model is missing. Click Download model to fetch it now."
            : "Model is missing and cannot be auto-downloaded because an explicit ModelPath is configured.";
    }
}
