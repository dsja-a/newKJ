namespace Keji.FileSystem.Workspace;

public interface IKejiWorkspacePathCandidateResolver
{
    KejiWorkspacePathCandidateResult Resolve(KejiWorkspacePathRequest? request);
}
