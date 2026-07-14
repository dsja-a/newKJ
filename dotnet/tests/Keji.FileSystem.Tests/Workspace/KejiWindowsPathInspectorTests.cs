using Keji.FileSystem.Workspace;

namespace Keji.FileSystem.Tests.Workspace;

public sealed class KejiWindowsPathInspectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"keji-inspector-{Guid.NewGuid():N}");

    [Fact]
    public void ExistingRegularFileIsSafe()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        File.WriteAllText(Path.Combine(_root, "shared", "file.txt"), "content");
        var (inspector, candidate) = Create("file.txt");
        using var result = inspector.Inspect(candidate, KejiFileSystemOperation.Read);
        Assert.True(result.IsSafe);
        Assert.True(result.TargetExists);
        Assert.False(result.TargetIsDirectory);
    }

    [Fact]
    public void ExistingDirectoryIsSafe()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared", "folder"));
        var (inspector, candidate) = Create("folder");
        using var result = inspector.Inspect(candidate, KejiFileSystemOperation.Read);
        Assert.True(result.IsSafe);
        Assert.True(result.TargetExists);
        Assert.True(result.TargetIsDirectory);
    }

    [Fact]
    public void MissingTargetWithExistingParentIsSafe()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        var (inspector, candidate) = Create("new.txt");
        using var result = inspector.Inspect(candidate, KejiFileSystemOperation.Read);
        Assert.True(result.IsSafe);
        Assert.False(result.TargetExists);
        Assert.False(result.TargetIsDirectory);
    }

    [Fact]
    public void MissingWorkspaceRootIsDenied()
    {
        var (inspector, candidate) = Create("file.txt");
        using var result = inspector.Inspect(candidate, KejiFileSystemOperation.Read);
        Assert.False(result.IsSafe);
        Assert.Equal(KejiWorkspaceAccessFailureReason.WorkspaceRootMissing, result.FailureReason);
    }

    [Fact]
    public void IntermediateFileIsDenied()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        File.WriteAllText(Path.Combine(_root, "shared", "parent"), "not a directory");
        var (inspector, candidate) = Create("parent/child.txt");
        using var result = inspector.Inspect(candidate, KejiFileSystemOperation.Read);
        Assert.False(result.IsSafe);
        Assert.Equal(KejiWorkspaceAccessFailureReason.ParentNotDirectory, result.FailureReason);
    }

    private (KejiWindowsPathInspector Inspector, KejiWorkspacePathCandidateResult Candidate) Create(string relative)
    {
        var options = new KejiWorkspaceOptions(_root);
        var candidate = new KejiWorkspacePathCandidateResolver(options)
            .Resolve(new(KejiWorkspaceScope.Shared, null, relative));
        Assert.True(candidate.IsValid);
        return (new KejiWindowsPathInspector(options), candidate);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
