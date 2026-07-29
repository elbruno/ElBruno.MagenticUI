using ElBruno.MagenticUI.App.ModelSettings;

namespace ElBruno.MagenticUI.App.ModelDownloadProgress;

public sealed class ModelDownloadProgressStateService : IModelDownloadProgressStateService
{
    private readonly object _stateLock = new();
    private readonly Dictionary<ModelRole, ModelDownloadState> _states = [];

    public event Func<Task>? OnChanged;

    public IReadOnlyList<ModelDownloadState> GetStates()
    {
        lock (_stateLock)
        {
            return _states.Values.OrderBy(state => state.Role).ToArray();
        }
    }

    public ModelDownloadState GetState(ModelRole role)
    {
        lock (_stateLock)
        {
            if (!_states.TryGetValue(role, out var state))
            {
                state = CreateIdleState(role, string.Empty);
                _states[role] = state;
            }

            return state;
        }
    }

    public void Initialize(ModelRole role, string modelId)
    {
        UpdateState(role, _ =>
            CreateIdleState(role, modelId));
    }

    public IProgress<ElBruno.LocalLLMs.ModelDownloadProgress> CreateProgressReporter(ModelRole role, string modelId)
    {
        Initialize(role, modelId);
        return new Progress<ElBruno.LocalLLMs.ModelDownloadProgress>(progress => UpdateState(role, current =>
        {
            var normalizedCurrent = EnsureModelId(current, role, modelId);
            var fileName = progress.FileName ?? string.Empty;
            var totalBytes = progress.TotalBytes;
            var downloadedBytes = progress.BytesDownloaded;
            var percent = ClampPercent(progress.PercentComplete);
            var statusText = string.IsNullOrWhiteSpace(fileName)
                ? $"Downloading {percent:0.##}%"
                : $"Downloading {fileName} ({percent:0.##}%)";

            return normalizedCurrent with
            {
                CurrentFileName = fileName,
                DownloadedBytes = downloadedBytes,
                TotalBytes = totalBytes,
                PercentComplete = percent,
                Phase = ModelDownloadPhase.Downloading,
                StatusText = statusText,
                LastUpdated = DateTimeOffset.UtcNow,
                Error = null
            };
        }));
    }

    public void MarkCompleted(ModelRole role, string modelId)
    {
        UpdateState(role, current =>
        {
            var normalizedCurrent = EnsureModelId(current, role, modelId);
            return normalizedCurrent with
            {
                PercentComplete = 100d,
                Phase = ModelDownloadPhase.Completed,
                StatusText = "Completed",
                LastUpdated = DateTimeOffset.UtcNow,
                Error = null
            };
        });
    }

    public void MarkFailed(ModelRole role, string modelId, string error)
    {
        UpdateState(role, current =>
        {
            var normalizedCurrent = EnsureModelId(current, role, modelId);
            return normalizedCurrent with
            {
                Phase = ModelDownloadPhase.Failed,
                StatusText = "Failed",
                LastUpdated = DateTimeOffset.UtcNow,
                Error = error
            };
        });
    }

    private void UpdateState(ModelRole role, Func<ModelDownloadState, ModelDownloadState> update)
    {
        lock (_stateLock)
        {
            _states.TryGetValue(role, out var currentState);
            currentState ??= CreateIdleState(role, string.Empty);
            _states[role] = update(currentState);
        }

        _ = NotifyChanged();
    }

    private Task NotifyChanged()
    {
        var handler = OnChanged;
        return handler is not null ? handler() : Task.CompletedTask;
    }

    private static ModelDownloadState CreateIdleState(ModelRole role, string modelId)
        => new(
            Role: role,
            ModelId: modelId,
            CurrentFileName: string.Empty,
            DownloadedBytes: 0,
            TotalBytes: 0,
            PercentComplete: 0d,
            Phase: ModelDownloadPhase.Idle,
            StatusText: "Idle",
            LastUpdated: DateTimeOffset.UtcNow,
            Error: null);

    private static ModelDownloadState EnsureModelId(ModelDownloadState state, ModelRole role, string modelId)
        => state with
        {
            Role = role,
            ModelId = string.IsNullOrWhiteSpace(modelId) ? state.ModelId : modelId
        };

    private static double ClampPercent(double value)
        => double.IsFinite(value)
            ? Math.Clamp(value, 0d, 100d)
            : 0d;
}
