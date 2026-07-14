namespace Keji.FileSystem.Workspace;

public sealed class KejiWorkspacePathRequest
{
    public KejiWorkspaceScope Scope { get; }
    public string? TargetUserId { get; }
    public string? RelativePath { get; }

    public KejiWorkspacePathRequest(
        KejiWorkspaceScope scope,
        string? targetUserId,
        string? relativePath)
    {
        Scope = scope;
        TargetUserId = targetUserId;
        RelativePath = relativePath;
    }
}
