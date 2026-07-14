namespace Keji.FileSystem.Workspace;

public sealed class KejiWorkspaceOptions
{
    public string WorkspaceRoot { get; }
    public int MaxRelativePathLength { get; }
    public int MaxSegmentLength { get; }

    public KejiWorkspaceOptions(
        string workspaceRoot,
        int maxRelativePathLength = 1024,
        int maxSegmentLength = 255)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
            throw new ArgumentException("WorkspaceRoot is required.", nameof(workspaceRoot));
        if (!KejiWindowsPathRules.IsLocalAbsolutePath(workspaceRoot))
            throw new ArgumentException("WorkspaceRoot must be fully qualified.", nameof(workspaceRoot));
        if (maxRelativePathLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRelativePathLength));
        if (maxSegmentLength is < 1 or > 255)
            throw new ArgumentOutOfRangeException(nameof(maxSegmentLength));

        try
        {
            WorkspaceRoot = KejiWorkspaceRootLayout.Normalize(workspaceRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            throw new ArgumentException("WorkspaceRoot is invalid.", nameof(workspaceRoot), ex);
        }

        MaxRelativePathLength = maxRelativePathLength;
        MaxSegmentLength = maxSegmentLength;
    }
}
