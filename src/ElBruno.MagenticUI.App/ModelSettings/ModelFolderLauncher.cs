using System.Diagnostics;

namespace ElBruno.MagenticUI.App.ModelSettings;

public sealed class ModelFolderLauncher : IModelFolderLauncher
{
    public bool TryOpen(string folderPath, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (!OperatingSystem.IsWindows())
        {
            errorMessage = "Open folder is only supported on Windows.";
            return false;
        }

        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = true
            });

            if (process is null)
            {
                errorMessage = "Windows Explorer could not be started.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Unable to open folder: {ex.Message}";
            return false;
        }
    }
}
