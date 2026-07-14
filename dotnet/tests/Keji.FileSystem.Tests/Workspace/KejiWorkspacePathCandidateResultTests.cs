using Keji.FileSystem.Workspace;

namespace Keji.FileSystem.Tests.Workspace;

public sealed class KejiWorkspacePathCandidateResultTests
{
    [Theory]
    [InlineData(null, "full", "relative")]
    [InlineData("", "full", "relative")]
    [InlineData(" ", "full", "relative")]
    [InlineData("root", null, "relative")]
    [InlineData("root", "", "relative")]
    [InlineData("root", " ", "relative")]
    public void SuccessRejectsInvalidPaths(string? root, string? full, string? relative)
    {
        Assert.ThrowsAny<ArgumentException>(() => KejiWorkspacePathCandidateResult.Success(root!, full!, relative!));
    }

    [Fact]
    public void SuccessAllowsEmptyRelativePath()
    {
        var result = KejiWorkspacePathCandidateResult.Success("root", "full", string.Empty);
        Assert.True(result.IsValid);
        Assert.Equal(string.Empty, result.NormalizedRelativePath);
    }

    [Fact]
    public void RejectUnknownReasonThrows()
    {
        Assert.Throws<ArgumentException>(() => KejiWorkspacePathCandidateResult.Reject((KejiPathSandboxFailureReason)999));
    }
}
