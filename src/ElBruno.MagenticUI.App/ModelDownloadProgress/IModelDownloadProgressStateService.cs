using ElBruno.MagenticUI.App.ModelSettings;

namespace ElBruno.MagenticUI.App.ModelDownloadProgress;

public interface IModelDownloadProgressStateService
{
    event Func<Task>? OnChanged;

    IReadOnlyList<ModelDownloadState> GetStates();
    ModelDownloadState GetState(ModelRole role);
    void Initialize(ModelRole role, string modelId);
    IProgress<ElBruno.LocalLLMs.ModelDownloadProgress> CreateProgressReporter(ModelRole role, string modelId);
    void MarkCompleted(ModelRole role, string modelId);
    void MarkFailed(ModelRole role, string modelId, string error);
}
