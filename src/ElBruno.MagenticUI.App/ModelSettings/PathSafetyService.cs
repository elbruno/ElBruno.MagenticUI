namespace ElBruno.MagenticUI.App.ModelSettings;

public sealed class PathSafetyService : IPathSafetyService
{
    public string? NormalizeAbsolutePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    public bool IsPathUnderRoot(string path, string rootPath)
    {
        var normalizedPath = NormalizeAbsolutePath(path);
        var normalizedRoot = NormalizeAbsolutePath(rootPath);

        if (normalizedPath is null || normalizedRoot is null)
            return false;

        return IsPathUnderNormalizedRoot(normalizedPath, normalizedRoot);
    }

    public bool TryResolveSafePath(
        string path,
        IEnumerable<string> allowedRoots,
        out string normalizedPath,
        out string statusText)
    {
        normalizedPath = string.Empty;
        statusText = "Invalid path.";

        var candidatePath = NormalizeAbsolutePath(path);
        if (candidatePath is null)
            return false;

        foreach (var allowedRoot in allowedRoots)
        {
            var normalizedRoot = NormalizeAbsolutePath(allowedRoot);
            if (normalizedRoot is null)
                continue;

            if (!IsPathUnderNormalizedRoot(candidatePath, normalizedRoot))
                continue;

            normalizedPath = candidatePath;
            statusText = "Path is inside allowed roots.";
            return true;
        }

        statusText = "Path is outside allowed roots.";
        return false;
    }

    private static bool IsPathUnderNormalizedRoot(string normalizedPath, string normalizedRoot)
    {
        var rootWithSeparator = normalizedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var pathWithSeparator = normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return pathWithSeparator.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            || string.Equals(pathWithSeparator, rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }
}
