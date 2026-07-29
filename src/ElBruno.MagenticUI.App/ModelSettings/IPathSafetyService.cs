namespace ElBruno.MagenticUI.App.ModelSettings;

public interface IPathSafetyService
{
    string? NormalizeAbsolutePath(string? path);
    bool IsPathUnderRoot(string path, string rootPath);
    bool TryResolveSafePath(string path, IEnumerable<string> allowedRoots, out string normalizedPath, out string statusText);
}
