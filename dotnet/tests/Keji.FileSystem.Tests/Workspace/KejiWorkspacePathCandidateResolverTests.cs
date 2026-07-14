using Keji.FileSystem.Workspace;

namespace Keji.FileSystem.Tests.Workspace;

public sealed class KejiWorkspacePathCandidateResolverTests
{
    private const string UserId = "0123456789abcdef";

    [Fact]
    public void SharedEmptyPathReturnsSharedRoot()
    {
        var resolver = CreateResolver();
        var result = resolver.Resolve(new(KejiWorkspaceScope.Shared, null, string.Empty));
        AssertSuccess(result, string.Empty);
        Assert.EndsWith("shared", result.RootPath!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(result.RootPath, result.FullPath);
    }

    [Fact]
    public void UserEmptyPathReturnsUserRoot()
    {
        var result = CreateResolver().Resolve(new(KejiWorkspaceScope.User, UserId, string.Empty));
        AssertSuccess(result, string.Empty);
        Assert.EndsWith(Path.Combine("users", UserId), result.RootPath!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NestedPathNormalizesMixedSeparators()
    {
        var result = CreateResolver().Resolve(new(KejiWorkspaceScope.Shared, null, "a/b\\c.txt"));
        AssertSuccess(result, "a\\b\\c.txt");
    }

    [Fact]
    public void UnicodeIsNormalizedToFormC()
    {
        var result = CreateResolver().Resolve(new(KejiWorkspaceScope.Shared, null, "e\u0301.txt"));
        AssertSuccess(result, "é.txt");
    }

    [Fact]
    public void NullRequestIsRejectedWithoutPaths()
    {
        AssertRejected(CreateResolver().Resolve(null), KejiPathSandboxFailureReason.InvalidScope);
    }

    [Fact]
    public void NullRelativePathIsRejected()
    {
        var result = CreateResolver().Resolve(new(KejiWorkspaceScope.Shared, null, null));
        AssertRejected(result, KejiPathSandboxFailureReason.MissingRelativePath);
    }

    [Fact]
    public void WhitespaceRelativePathIsRejectedWithoutTrimming()
    {
        var result = CreateResolver().Resolve(new(KejiWorkspaceScope.Shared, null, "   "));
        AssertRejected(result, KejiPathSandboxFailureReason.WhitespaceRelativePath);
    }

    [Theory]
    [InlineData(".", KejiPathSandboxFailureReason.CurrentDirectorySegment)]
    [InlineData("..", KejiPathSandboxFailureReason.TraversalSegment)]
    [InlineData("a/./b", KejiPathSandboxFailureReason.CurrentDirectorySegment)]
    [InlineData("a/../b", KejiPathSandboxFailureReason.TraversalSegment)]
    public void DotSegmentsAreRejected(string path, KejiPathSandboxFailureReason reason)
    {
        AssertRejected(CreateResolver().Resolve(new(KejiWorkspaceScope.Shared, null, path)), reason);
    }

    [Theory]
    [InlineData("a//b")]
    [InlineData("a\\\\b")]
    [InlineData("a\\/b")]
    [InlineData("a/")]
    public void EmptySegmentsAreRejected(string path)
    {
        AssertRejected(CreateResolver().Resolve(new(KejiWorkspaceScope.Shared, null, path)), KejiPathSandboxFailureReason.EmptySegment);
    }

    [Fact]
    public void SegmentAtLimitIsAccepted()
    {
        var result = CreateResolver(maxSegmentLength: 3).Resolve(new(KejiWorkspaceScope.Shared, null, "abc"));
        AssertSuccess(result, "abc");
    }

    [Fact]
    public void SegmentOverLimitIsRejected()
    {
        var result = CreateResolver(maxSegmentLength: 3).Resolve(new(KejiWorkspaceScope.Shared, null, "abcd"));
        AssertRejected(result, KejiPathSandboxFailureReason.SegmentTooLong);
    }

    [Fact]
    public void PathAtLimitIsAccepted()
    {
        var result = CreateResolver(maxPathLength: 5).Resolve(new(KejiWorkspaceScope.Shared, null, "abcde"));
        AssertSuccess(result, "abcde");
    }

    [Fact]
    public void PathOverLimitIsRejected()
    {
        var result = CreateResolver(maxPathLength: 5).Resolve(new(KejiWorkspaceScope.Shared, null, "abcdef"));
        AssertRejected(result, KejiPathSandboxFailureReason.PathTooLong);
    }

    [Fact]
    public void SameInputProducesEquivalentResults()
    {
        var resolver = CreateResolver();
        var request = new KejiWorkspacePathRequest(KejiWorkspaceScope.User, UserId, "folder/file.txt");
        var first = resolver.Resolve(request);
        var second = resolver.Resolve(request);
        Assert.Equal(first.IsValid, second.IsValid);
        Assert.Equal(first.FailureReason, second.FailureReason);
        Assert.Equal(first.RootPath, second.RootPath);
        Assert.Equal(first.FullPath, second.FullPath);
        Assert.Equal(first.NormalizedRelativePath, second.NormalizedRelativePath);
    }

    [Fact]
    public void ResolveDoesNotMutateRequest()
    {
        var request = new KejiWorkspacePathRequest(KejiWorkspaceScope.User, UserId, "folder/file.txt");
        _ = CreateResolver().Resolve(request);
        Assert.Equal(KejiWorkspaceScope.User, request.Scope);
        Assert.Equal(UserId, request.TargetUserId);
        Assert.Equal("folder/file.txt", request.RelativePath);
    }

    [Fact]
    public void ResolverDoesNotCreateWorkspaceRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"keji-never-create-{Guid.NewGuid():N}");
        Assert.False(Directory.Exists(root));
        var resolver = new KejiWorkspacePathCandidateResolver(new KejiWorkspaceOptions(root));
        var result = resolver.Resolve(new(KejiWorkspaceScope.Shared, null, "file.txt"));
        Assert.True(result.IsValid);
        Assert.False(Directory.Exists(root));
    }

    private static KejiWorkspacePathCandidateResolver CreateResolver(
        int maxPathLength = 1024,
        int maxSegmentLength = 255)
    {
        var root = Path.Combine(Path.GetTempPath(), "keji-p1-tests");
        return new(new KejiWorkspaceOptions(root, maxPathLength, maxSegmentLength));
    }

    private static void AssertSuccess(KejiWorkspacePathCandidateResult result, string relativePath)
    {
        Assert.True(result.IsValid);
        Assert.Equal(KejiPathSandboxFailureReason.None, result.FailureReason);
        Assert.NotNull(result.RootPath);
        Assert.NotNull(result.FullPath);
        Assert.Equal(relativePath, result.NormalizedRelativePath);
    }

    private static void AssertRejected(
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
