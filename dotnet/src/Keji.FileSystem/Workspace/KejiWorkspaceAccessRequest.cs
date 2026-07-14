namespace Keji.FileSystem.Workspace;

public sealed class KejiWorkspaceAccessRequest
{
    public KejiWorkspacePathRequest? Path { get; }
    public KejiFileSystemOperation Operation { get; }

    public KejiWorkspaceAccessRequest(
        KejiWorkspacePathRequest? path,
        KejiFileSystemOperation operation)
    {
        Path = path;
        Operation = operation;
    }
}
