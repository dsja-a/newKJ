using Keji.FileSystem.Workspace;

namespace Keji.FileSystem.Tests.Workspace;

public sealed class KejiWorkspaceIdentityRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void UserMissingIdsHaveMissingReason(string? id)
    {
        var result = new KejiWorkspacePathCandidateResolver(new KejiWorkspaceOptions("C:\\workspace"))
            .Resolve(new(KejiWorkspaceScope.User, id, "file.txt"));
        Assert.False(result.IsValid);
        Assert.Equal(KejiPathSandboxFailureReason.MissingUserId, result.FailureReason);
    }
}
