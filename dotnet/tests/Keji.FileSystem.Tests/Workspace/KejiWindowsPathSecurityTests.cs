using System.Diagnostics;
using System.Runtime.InteropServices;
using Keji.FileSystem.Workspace;

namespace Keji.FileSystem.Tests.Workspace;

public sealed class KejiWindowsPathSecurityTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"keji-path-security-{Guid.NewGuid():N}");

    private readonly string _outsideRoot = Path.Combine(
        Path.GetTempPath(),
        $"keji-path-security-outside-{Guid.NewGuid():N}");

    private readonly List<string> _junctions = [];

    [Fact]
    public void WorkspaceRootJunctionIsDeniedAsReparsePoint()
    {
        Directory.CreateDirectory(Path.Combine(_outsideRoot, "shared"));
        CreateJunction(_root, _outsideRoot);

        var (inspector, candidate) = CreateSharedCandidate("file.txt");

        using var result = inspector.Inspect(candidate, KejiFileSystemOperation.Read);

        Assert.False(result.IsSafe);
        Assert.Equal(
            KejiWorkspaceAccessFailureReason.ReparsePointDenied,
            result.FailureReason);
        Assert.False(result.TargetExists);
        Assert.False(result.TargetIsDirectory);
    }

    [Fact]
    public void ParentDirectoryJunctionIsDeniedAsReparsePoint()
    {
        var sharedRoot = Path.Combine(_root, "shared");
        var outsideParent = Path.Combine(_outsideRoot, "outside-parent");
        Directory.CreateDirectory(sharedRoot);
        Directory.CreateDirectory(outsideParent);
        File.WriteAllText(Path.Combine(outsideParent, "file.txt"), "outside");
        CreateJunction(Path.Combine(sharedRoot, "linked-parent"), outsideParent);

        var (inspector, candidate) = CreateSharedCandidate("linked-parent/file.txt");

        using var result = inspector.Inspect(candidate, KejiFileSystemOperation.Read);

        Assert.False(result.IsSafe);
        Assert.Equal(
            KejiWorkspaceAccessFailureReason.ReparsePointDenied,
            result.FailureReason);
        Assert.False(result.TargetExists);
        Assert.False(result.TargetIsDirectory);
    }

    [Fact]
    public void TargetDirectoryJunctionIsDeniedAsReparsePoint()
    {
        var sharedRoot = Path.Combine(_root, "shared");
        var outsideTarget = Path.Combine(_outsideRoot, "outside-target");
        Directory.CreateDirectory(sharedRoot);
        Directory.CreateDirectory(outsideTarget);
        CreateJunction(Path.Combine(sharedRoot, "linked-target"), outsideTarget);

        var (inspector, candidate) = CreateSharedCandidate("linked-target");

        using var result = inspector.Inspect(candidate, KejiFileSystemOperation.Read);

        Assert.False(result.IsSafe);
        Assert.Equal(
            KejiWorkspaceAccessFailureReason.ReparsePointDenied,
            result.FailureReason);
        Assert.False(result.TargetExists);
        Assert.False(result.TargetIsDirectory);
    }

    [Fact]
    public void HardLinkedFileIsDeniedWithExactReason()
    {
        Assert.True(OperatingSystem.IsWindows(), "This test requires Windows.");
        var sharedRoot = Path.Combine(_root, "shared");
        var originalPath = Path.Combine(sharedRoot, "original.txt");
        var hardLinkPath = Path.Combine(sharedRoot, "hard-link.txt");
        Directory.CreateDirectory(sharedRoot);
        File.WriteAllText(originalPath, "content");
        CreateHardLink(hardLinkPath, originalPath);

        var (inspector, candidate) = CreateSharedCandidate("hard-link.txt");

        using var result = inspector.Inspect(candidate, KejiFileSystemOperation.Read);

        Assert.False(result.IsSafe);
        Assert.Equal(
            KejiWorkspaceAccessFailureReason.HardLinkDenied,
            result.FailureReason);
        Assert.False(result.TargetExists);
        Assert.False(result.TargetIsDirectory);
    }

    [Fact]
    public void MissingNestedTargetPinsNearestExistingParent()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared", "existing"));
        var (inspector, candidate) = CreateSharedCandidate(
            "existing/first/second/file.txt");

        using var result = inspector.Inspect(candidate, KejiFileSystemOperation.Read);

        Assert.True(result.IsSafe);
        Assert.Equal(KejiWorkspaceAccessFailureReason.None, result.FailureReason);
        Assert.False(result.TargetExists);
        Assert.False(result.TargetIsDirectory);
        Assert.NotNull(result.Lease);
        Assert.Equal(3, result.Lease.MissingSegmentCount);
        Assert.True(inspector.Revalidate(result));
    }

    [Fact]
    public void RejectedCandidateIsDeniedAsPathInspectionFailure()
    {
        var inspector = new KejiWindowsPathInspector(new KejiWorkspaceOptions(_root));
        var candidate = KejiWorkspacePathCandidateResult.Reject(
            KejiPathSandboxFailureReason.InvalidScope);

        using var result = inspector.Inspect(candidate, KejiFileSystemOperation.Read);

        Assert.False(result.IsSafe);
        Assert.Equal(
            KejiWorkspaceAccessFailureReason.PathInspectionFailed,
            result.FailureReason);
    }

    [Fact]
    public void CandidateWithDisallowedScopeRootIsDeniedAsPathInspectionFailure()
    {
        var inspector = new KejiWindowsPathInspector(new KejiWorkspaceOptions(_root));
        var disallowedRoot = Path.Combine(_root, "other");
        var candidate = KejiWorkspacePathCandidateResult.Success(
            disallowedRoot,
            Path.Combine(disallowedRoot, "file.txt"),
            "file.txt");

        using var result = inspector.Inspect(candidate, KejiFileSystemOperation.Read);

        Assert.False(result.IsSafe);
        Assert.Equal(
            KejiWorkspaceAccessFailureReason.PathInspectionFailed,
            result.FailureReason);
    }

    [Fact]
    public void UnknownOperationIsDeniedAsInvalidOperation()
    {
        var (inspector, candidate) = CreateSharedCandidate("file.txt");

        using var result = inspector.Inspect(candidate, KejiFileSystemOperation.Unknown);

        Assert.False(result.IsSafe);
        Assert.Equal(
            KejiWorkspaceAccessFailureReason.InvalidOperation,
            result.FailureReason);
    }

    [Fact]
    public void UndefinedOperationIsDeniedAsInvalidOperation()
    {
        var (inspector, candidate) = CreateSharedCandidate("file.txt");

        using var result = inspector.Inspect(candidate, (KejiFileSystemOperation)int.MaxValue);

        Assert.False(result.IsSafe);
        Assert.Equal(
            KejiWorkspaceAccessFailureReason.InvalidOperation,
            result.FailureReason);
    }

    [Fact]
    public void ExistingFileLeaseBlocksRenameUntilDisposed()
    {
        Assert.True(OperatingSystem.IsWindows(), "This test requires Windows.");
        var sharedRoot = Path.Combine(_root, "shared");
        var filePath = Path.Combine(sharedRoot, "file.txt");
        var movedPath = Path.Combine(sharedRoot, "moved.txt");
        Directory.CreateDirectory(sharedRoot);
        File.WriteAllText(filePath, "content");
        var (inspector, candidate) = CreateSharedCandidate("file.txt");
        var result = inspector.Inspect(candidate, KejiFileSystemOperation.Read);

        try
        {
            Assert.True(result.IsSafe);
            Assert.True(result.TargetExists);
            Assert.NotNull(result.Lease);
            Assert.True(inspector.Revalidate(result));
            Assert.Throws<IOException>(() => File.Move(filePath, movedPath));
        }
        finally
        {
            result.Dispose();
        }

        Assert.False(inspector.Revalidate(result));
        result.Dispose();
        File.Move(filePath, movedPath);
        Assert.True(File.Exists(movedPath));
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public void MissingTargetLeaseBlocksParentRenameUntilDisposed()
    {
        Assert.True(OperatingSystem.IsWindows(), "This test requires Windows.");
        var existingParent = Path.Combine(_root, "shared", "existing");
        var movedParent = Path.Combine(_root, "shared", "moved");
        Directory.CreateDirectory(existingParent);
        var (inspector, candidate) = CreateSharedCandidate("existing/new.txt");
        var result = inspector.Inspect(candidate, KejiFileSystemOperation.Create);

        try
        {
            Assert.True(result.IsSafe);
            Assert.False(result.TargetExists);
            Assert.NotNull(result.Lease);
            Assert.Equal(1, result.Lease.MissingSegmentCount);
            Assert.True(inspector.Revalidate(result));
            Assert.Throws<IOException>(() => Directory.Move(existingParent, movedParent));
        }
        finally
        {
            result.Dispose();
        }

        Assert.False(inspector.Revalidate(result));
        Directory.Move(existingParent, movedParent);
        Assert.True(Directory.Exists(movedParent));
        Assert.False(Directory.Exists(existingParent));
    }

    [Fact]
    public void LeaseLessSafeResultCannotBeRevalidated()
    {
        var inspector = new KejiWindowsPathInspector(new KejiWorkspaceOptions(_root));
        using var result = KejiPathInspectionResult.Safe(
            targetExists: true,
            targetIsDirectory: false);

        Assert.True(result.IsSafe);
        Assert.Equal(KejiWorkspaceAccessFailureReason.None, result.FailureReason);
        Assert.Null(result.Lease);
        Assert.False(inspector.Revalidate(result));
    }

    private (KejiWindowsPathInspector Inspector, KejiWorkspacePathCandidateResult Candidate)
        CreateSharedCandidate(string relativePath)
    {
        var options = new KejiWorkspaceOptions(_root);
        var candidate = new KejiWorkspacePathCandidateResolver(options).Resolve(
            new KejiWorkspacePathRequest(
                KejiWorkspaceScope.Shared,
                targetUserId: null,
                relativePath));

        Assert.True(candidate.IsValid);
        return (new KejiWindowsPathInspector(options), candidate);
    }

    private void CreateJunction(string junctionPath, string targetPath)
    {
        Assert.True(OperatingSystem.IsWindows(), "This test requires Windows.");
        Directory.CreateDirectory(targetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(junctionPath)!);

        var commandInterpreter = Environment.GetEnvironmentVariable("ComSpec");
        Assert.False(string.IsNullOrWhiteSpace(commandInterpreter));

        var startInfo = new ProcessStartInfo
        {
            FileName = commandInterpreter,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(junctionPath);
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"mklink /J failed with exit code {process.ExitCode}: " +
            standardOutput + standardError);

        _junctions.Add(junctionPath);
        Assert.True(
            File.GetAttributes(junctionPath).HasFlag(FileAttributes.ReparsePoint),
            "The created junction must have the ReparsePoint attribute.");
    }

    private static void CreateHardLink(string hardLinkPath, string existingPath)
    {
        if (CreateHardLinkW(hardLinkPath, existingPath, IntPtr.Zero))
            return;

        var error = Marshal.GetLastWin32Error();
        Assert.Fail($"CreateHardLinkW failed with Win32 error {error}.");
    }

    public void Dispose()
    {
        foreach (var junction in _junctions.OrderByDescending(path => path.Length))
        {
            if (Directory.Exists(junction))
                Directory.Delete(junction, recursive: false);
        }

        DeleteTreeWithoutFollowingRootJunction(_root);
        DeleteTreeWithoutFollowingRootJunction(_outsideRoot);
    }

    private static void DeleteTreeWithoutFollowingRootJunction(string path)
    {
        if (!Directory.Exists(path))
            return;

        var isReparsePoint = File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint);
        Directory.Delete(path, recursive: !isReparsePoint);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(
        string fileName,
        string existingFileName,
        IntPtr securityAttributes);
}
