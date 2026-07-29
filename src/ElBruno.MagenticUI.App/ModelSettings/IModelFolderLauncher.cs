namespace ElBruno.MagenticUI.App.ModelSettings;

public interface IModelFolderLauncher
{
    bool TryOpen(string folderPath, out string errorMessage);
}
