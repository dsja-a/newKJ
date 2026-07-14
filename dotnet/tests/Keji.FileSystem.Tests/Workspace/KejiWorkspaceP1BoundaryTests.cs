using Keji.FileSystem.Workspace;

namespace Keji.FileSystem.Tests.Workspace;

public sealed class KejiWorkspaceP1BoundaryTests
{
    private const string ValidUserId = "0123456789abcdef";
    private const string WorkspaceRoot = "C:\\workspace";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".\\relative")]
    [InlineData("\\\\.\\C:\\data")]
    [InlineData("\\??\\C:\\data")]
    [InlineData("é:\\data")]
    [InlineData("1:\\data")]
    public void InvalidWorkspaceRootsAreRejected(string? workspaceRoot)
    {
        Assert.Throws<ArgumentException>(() =>
            new KejiWorkspaceOptions(workspaceRoot!));
    }

    [Theory]
    [InlineData("C:/", "C:\\")]
    [InlineData("C:\\", "C:\\")]
    [InlineData("d:\\data\\keji\\", "d:\\data\\keji")]
    public void LocalDriveRootsAreNormalizedExactly(string workspaceRoot, string expected)
    {
        var options = new KejiWorkspaceOptions(workspaceRoot);

        Assert.Equal(expected, options.WorkspaceRoot);
        Assert.Equal(1024, options.MaxRelativePathLength);
        Assert.Equal(255, options.MaxSegmentLength);
    }

    [Theory]
    [InlineData(0, 255)]
    [InlineData(-1, 255)]
    [InlineData(1, 0)]
    [InlineData(1, 256)]
    public void InvalidLengthOptionsAreRejected(
        int maxRelativePathLength,
        int maxSegmentLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new KejiWorkspaceOptions(
                WorkspaceRoot,
                maxRelativePathLength,
                maxSegmentLength));
    }

    [Fact]
    public void ValidUserIdBuildsExpectedUserRoot()
    {
        var result = Resolve(
            KejiWorkspaceScope.User,
            ValidUserId,
            string.Empty);

        AssertSuccessfulCandidate(
            result,
            "C:\\workspace\\users\\0123456789abcdef",
            "C:\\workspace\\users\\0123456789abcdef",
            string.Empty);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(ValidUserId)]
    public void SharedScopeRejectsEveryNonNullUserId(string targetUserId)
    {
        var result = Resolve(
            KejiWorkspaceScope.Shared,
            targetUserId,
            "file.txt");

        AssertRejectedCandidate(
            result,
            KejiPathSandboxFailureReason.UnexpectedUserId);
    }

    [Theory]
    [InlineData("0123456789abcde")]
    [InlineData("0123456789abcdef0")]
    [InlineData("0123456789abcdeF")]
    [InlineData("01234567-9abcdef")]
    [InlineData("01234567_9abcdef")]
    [InlineData("01234567.9abcdef")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("01234567/9abcdef")]
    [InlineData("01234567\\9abcdef")]
    [InlineData("01234567:9abcdef")]
    [InlineData("用户用户用户用户用户用户用户用户")]
    [InlineData("0123456789abcde\u0001")]
    [InlineData(" 0123456789abcde")]
    [InlineData("0123456789abcde ")]
    public void UserScopeRejectsNonCanonicalUserIds(string targetUserId)
    {
        var result = Resolve(
            KejiWorkspaceScope.User,
            targetUserId,
            "file.txt");

        AssertRejectedCandidate(
            result,
            KejiPathSandboxFailureReason.InvalidUserId);
    }

    [Fact]
    public void UnknownScopeIsRejectedWithoutCandidatePaths()
    {
        var result = Resolve(
            KejiWorkspaceScope.Unknown,
            null,
            "file.txt");

        AssertRejectedCandidate(
            result,
            KejiPathSandboxFailureReason.InvalidScope);
    }

    [Theory]
    [InlineData("\\file.txt", KejiPathSandboxFailureReason.RootedPath)]
    [InlineData("/file.txt", KejiPathSandboxFailureReason.RootedPath)]
    [InlineData("C:\\file.txt", KejiPathSandboxFailureReason.DriveQualifiedPath)]
    [InlineData("C:/file.txt", KejiPathSandboxFailureReason.DriveQualifiedPath)]
    [InlineData("C:file.txt", KejiPathSandboxFailureReason.DriveQualifiedPath)]
    [InlineData("\\\\server\\share\\file.txt", KejiPathSandboxFailureReason.UncPath)]
    [InlineData("//server/share/file.txt", KejiPathSandboxFailureReason.UncPath)]
    [InlineData("\\\\?\\C:\\file.txt", KejiPathSandboxFailureReason.DevicePath)]
    [InlineData("\\\\.\\PhysicalDrive0", KejiPathSandboxFailureReason.DevicePath)]
    [InlineData("\\??\\C:\\file.txt", KejiPathSandboxFailureReason.DevicePath)]
    public void SpecialPathsHavePreciseFailureReasons(
        string relativePath,
        KejiPathSandboxFailureReason expectedReason)
    {
        var result = Resolve(
            KejiWorkspaceScope.Shared,
            null,
            relativePath);

        AssertRejectedCandidate(result, expectedReason);
    }

    [Theory]
    [InlineData("file.txt:secret")]
    [InlineData("folder/file.txt:stream")]
    public void AlternateDataStreamsAreRejected(string relativePath)
    {
        var result = Resolve(
            KejiWorkspaceScope.Shared,
            null,
            relativePath);

        AssertRejectedCandidate(
            result,
            KejiPathSandboxFailureReason.AlternateDataStream);
    }

    [Theory]
    [InlineData("file\0.txt")]
    [InlineData("file\u0001.txt")]
    [InlineData("file\u001F.txt")]
    public void ControlCharactersAreRejected(string relativePath)
    {
        var result = Resolve(
            KejiWorkspaceScope.Shared,
            null,
            relativePath);

        AssertRejectedCandidate(
            result,
            KejiPathSandboxFailureReason.ControlCharacter);
    }

    [Theory]
    [InlineData(" file.txt")]
    [InlineData("file.txt ")]
    [InlineData("file.txt.")]
    public void WindowsTrailingAliasesAreRejected(string relativePath)
    {
        var result = Resolve(
            KejiWorkspaceScope.Shared,
            null,
            relativePath);

        AssertRejectedCandidate(
            result,
            KejiPathSandboxFailureReason.TrailingDotOrSpace);
    }

    [Fact]
    public void OrdinaryMiddleSpaceIsPreserved()
    {
        var result = Resolve(
            KejiWorkspaceScope.Shared,
            null,
            "my file.txt");

        AssertSuccessfulCandidate(
            result,
            "C:\\workspace\\shared",
            "C:\\workspace\\shared\\my file.txt",
            "my file.txt");
    }

    [Theory]
    [InlineData("con")]
    [InlineData("CON.txt")]
    [InlineData("nul.json")]
    [InlineData("Com1.log")]
    [InlineData("LPT9.doc")]
    [InlineData("clock$")]
    public void ReservedDeviceNamesAreRejectedCaseInsensitively(string relativePath)
    {
        var result = Resolve(
            KejiWorkspaceScope.Shared,
            null,
            relativePath);

        AssertRejectedCandidate(
            result,
            KejiPathSandboxFailureReason.ReservedDeviceName);
    }

    [Fact]
    public void UserNestedPathReturnsCompleteCandidate()
    {
        var result = Resolve(
            KejiWorkspaceScope.User,
            ValidUserId,
            "folder/file.txt");

        AssertSuccessfulCandidate(
            result,
            "C:\\workspace\\users\\0123456789abcdef",
            "C:\\workspace\\users\\0123456789abcdef\\folder\\file.txt",
            "folder\\file.txt");
    }

    [Fact]
    public void ValidSurrogatePairIsPreserved()
    {
        var result = Resolve(
            KejiWorkspaceScope.Shared,
            null,
            "emoji-😀.txt");

        AssertSuccessfulCandidate(
            result,
            "C:\\workspace\\shared",
            "C:\\workspace\\shared\\emoji-😀.txt",
            "emoji-😀.txt");
    }

    [Fact]
    public void SuccessFactoryRejectsNullNormalizedRelativePath()
    {
        Assert.Throws<ArgumentNullException>(() =>
            KejiWorkspacePathCandidateResult.Success(
                "root",
                "full",
                null!));
    }

    [Fact]
    public void RejectFactoryRejectsNoneReason()
    {
        Assert.Throws<ArgumentException>(() =>
            KejiWorkspacePathCandidateResult.Reject(
                KejiPathSandboxFailureReason.None));
    }

    [Fact]
    public void SuccessFactoryProducesCompleteCandidateState()
    {
        var result = KejiWorkspacePathCandidateResult.Success(
            "root",
            "full",
            "relative");

        Assert.True(result.IsValid);
        Assert.Equal(KejiPathSandboxFailureReason.None, result.FailureReason);
        Assert.Equal("root", result.RootPath);
        Assert.Equal("full", result.FullPath);
        Assert.Equal("relative", result.NormalizedRelativePath);
    }

    [Fact]
    public void RejectFactoryClearsEveryCandidatePath()
    {
        var result = KejiWorkspacePathCandidateResult.Reject(
            KejiPathSandboxFailureReason.RootedPath);

        AssertRejectedCandidate(
            result,
            KejiPathSandboxFailureReason.RootedPath);
    }

    private static KejiWorkspacePathCandidateResult Resolve(
        KejiWorkspaceScope scope,
        string? targetUserId,
        string? relativePath)
    {
        var options = new KejiWorkspaceOptions(WorkspaceRoot);
        var resolver = new KejiWorkspacePathCandidateResolver(options);

        return resolver.Resolve(
            new KejiWorkspacePathRequest(
                scope,
                targetUserId,
                relativePath));
    }

    private static void AssertSuccessfulCandidate(
        KejiWorkspacePathCandidateResult result,
        string expectedRootPath,
        string expectedFullPath,
        string expectedRelativePath)
    {
        Assert.True(result.IsValid);
        Assert.Equal(KejiPathSandboxFailureReason.None, result.FailureReason);
        Assert.Equal(expectedRootPath, result.RootPath);
        Assert.Equal(expectedFullPath, result.FullPath);
        Assert.Equal(expectedRelativePath, result.NormalizedRelativePath);
    }

    private static void AssertRejectedCandidate(
        KejiWorkspacePathCandidateResult result,
        KejiPathSandboxFailureReason expectedReason)
    {
        Assert.False(result.IsValid);
        Assert.Equal(expectedReason, result.FailureReason);
        Assert.Null(result.RootPath);
        Assert.Null(result.FullPath);
        Assert.Null(result.NormalizedRelativePath);
    }
}
