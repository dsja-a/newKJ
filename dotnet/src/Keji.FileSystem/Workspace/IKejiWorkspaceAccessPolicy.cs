namespace Keji.FileSystem.Workspace;

public interface IKejiWorkspaceAccessPolicy
{
    KejiWorkspaceAccessDecision Authorize(KejiWorkspaceAccessRequest? request);
}
