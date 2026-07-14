namespace Keji.FileSystem.Workspace;

public sealed class KejiWorkspaceFileSystemOptions
{
    public int MaxContentBytes { get; }
    public int MaxEntries { get; }

    public KejiWorkspaceFileSystemOptions(int maxContentBytes = 1_048_576, int maxEntries = 1_000)
    {
        if (maxContentBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxContentBytes));
        if (maxEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        MaxContentBytes = maxContentBytes;
        MaxEntries = maxEntries;
    }
}
