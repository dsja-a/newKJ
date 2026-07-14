namespace Keji.FileSystem.Workspace;

internal static class KejiWorkspaceRootLayout
{
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(root))
            throw new ArgumentException("WorkspaceRoot has no path root.", nameof(path));
        return Path.TrimEndingDirectorySeparator(full);
    }

    public static string ScopeRoot(string workspaceRoot, KejiWorkspaceScope scope, string? userId)
    {
        var shared = Path.Combine(workspaceRoot, "shared");
        return scope == KejiWorkspaceScope.Shared
            ? shared
            : Path.Combine(Path.Combine(workspaceRoot, "users"), userId!);
    }

    internal static bool IsWithinRoot(string rootPath, string fullPath)
    {
        var rootPrefix = Path.EndsInDirectorySeparator(rootPath)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;
        return string.Equals(fullPath, rootPath, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsAllowedScopeRoot(string workspaceRoot, string scopeRoot)
    {
        if (!IsWithinRoot(workspaceRoot, scopeRoot))
            return false;

        var relative = Path.GetRelativePath(workspaceRoot, scopeRoot);
        var segments = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.None);
        return segments.Length == 1 &&
                string.Equals(segments[0], "shared", StringComparison.OrdinalIgnoreCase) ||
            segments.Length == 2 &&
                string.Equals(segments[0], "users", StringComparison.OrdinalIgnoreCase) &&
                KejiWorkspaceIdentityRules.IsValidUserId(segments[1]);
    }
}
