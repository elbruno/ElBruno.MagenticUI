using ElBruno.LocalLLMs;

namespace ElBruno.MagenticUI.App.ModelSettings;

public interface IModelSettingsService
{
    IReadOnlyList<ModelSettingsEntry> GetModelEntries();
    ModelSettingsEntry GetModelEntry(ModelRole role);
    LocalLLMsOptions BuildLocalLlmOptions(ModelRole role);
    IReadOnlyList<string> GetModelStorageRoots();
    bool TryResolveSafeModelPath(string path, out string normalizedPath, out string statusText);
    ModelFileOperationResult OpenModelFolder(ModelRole role);
    ModelFileOperationResult DeleteModelFiles(ModelRole role, bool isConfirmed);
}
