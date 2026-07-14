namespace Keji.FileSystem.Workspace;

public interface IKejiWindowsPathInspector
{
    KejiPathInspectionResult Inspect(
        KejiWorkspacePathCandidateResult candidate,
        KejiFileSystemOperation operation);

    bool Revalidate(KejiPathInspectionResult inspection);
}
