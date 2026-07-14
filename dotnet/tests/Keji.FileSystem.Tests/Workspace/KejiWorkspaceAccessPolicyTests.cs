using Keji.FileSystem.Workspace;
using Keji.Security.Auth;

namespace Keji.FileSystem.Tests.Workspace;

public sealed class KejiWorkspaceAccessPolicyTests
{
    private const string OwnId = "0123456789abcdef";
    private const string OtherId = "fedcba9876543210";

    [Theory]
    [InlineData("admin", KejiFileSystemOperation.Read)]
    [InlineData("admin", KejiFileSystemOperation.Write)]
    [InlineData("admin", KejiFileSystemOperation.Create)]
    [InlineData("admin", KejiFileSystemOperation.Delete)]
    [InlineData("admin", KejiFileSystemOperation.Enumerate)]
    [InlineData("member", KejiFileSystemOperation.Read)]
    [InlineData("member", KejiFileSystemOperation.Write)]
    [InlineData("member", KejiFileSystemOperation.Create)]
    [InlineData("member", KejiFileSystemOperation.Delete)]
    [InlineData("member", KejiFileSystemOperation.Enumerate)]
    [InlineData("readonly", KejiFileSystemOperation.Read)]
    [InlineData("readonly", KejiFileSystemOperation.Enumerate)]
    public void SharedAllowedOperationsFollowRoleMatrix(string role, KejiFileSystemOperation operation)
    {
        var decision = CreatePolicy(User(role)).Authorize(
            new(new(KejiWorkspaceScope.Shared, null, "file.txt"), operation));
        Assert.True(decision.IsAllowed);
        Assert.Equal(KejiWorkspaceAccessFailureReason.None, decision.FailureReason);
    }

    [Theory]
    [InlineData(KejiFileSystemOperation.Write)]
    [InlineData(KejiFileSystemOperation.Create)]
    [InlineData(KejiFileSystemOperation.Delete)]
    public void ReadonlySharedMutationsAreDenied(KejiFileSystemOperation operation)
    {
        var decision = CreatePolicy(User("readonly")).Authorize(
            new(new(KejiWorkspaceScope.Shared, null, "file.txt"), operation));
        Assert.False(decision.IsAllowed);
        Assert.Equal(KejiWorkspaceAccessFailureReason.ReadonlyWriteDenied, decision.FailureReason);
    }

    [Theory]
    [InlineData("admin", KejiFileSystemOperation.Read)]
    [InlineData("admin", KejiFileSystemOperation.Write)]
    [InlineData("member", KejiFileSystemOperation.Read)]
    [InlineData("member", KejiFileSystemOperation.Write)]
    [InlineData("readonly", KejiFileSystemOperation.Read)]
    [InlineData("readonly", KejiFileSystemOperation.Enumerate)]
    public void OwnWorkspaceAllowedOperationsFollowRoleMatrix(string role, KejiFileSystemOperation operation)
    {
        var decision = CreatePolicy(User(role)).Authorize(
            new(new(KejiWorkspaceScope.User, OwnId, "file.txt"), operation));
        Assert.True(decision.IsAllowed);
    }

    [Theory]
    [InlineData("member")]
    [InlineData("readonly")]
    public void NonAdminCannotAccessOtherWorkspace(string role)
    {
        var decision = CreatePolicy(User(role)).Authorize(
            new(new(KejiWorkspaceScope.User, OtherId, "file.txt"), KejiFileSystemOperation.Read));
        Assert.False(decision.IsAllowed);
        Assert.Equal(KejiWorkspaceAccessFailureReason.OtherUserWorkspaceDenied, decision.FailureReason);
    }

    [Fact]
    public void AdminCanAccessOtherWorkspace()
    {
        var decision = CreatePolicy(User("admin")).Authorize(
            new(new(KejiWorkspaceScope.User, OtherId, "file.txt"), KejiFileSystemOperation.Delete));
        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void NullCurrentUserIsDenied()
    {
        var decision = CreatePolicy(null).Authorize(
            new(new(KejiWorkspaceScope.Shared, null, "file.txt"), KejiFileSystemOperation.Read));
        Assert.False(decision.IsAllowed);
        Assert.Equal(KejiWorkspaceAccessFailureReason.Unauthenticated, decision.FailureReason);
    }

    [Fact]
    public void UnknownRoleIsDenied()
    {
        var decision = CreatePolicy(User("ADMIN")).Authorize(
            new(new(KejiWorkspaceScope.Shared, null, "file.txt"), KejiFileSystemOperation.Read));
        Assert.False(decision.IsAllowed);
        Assert.Equal(KejiWorkspaceAccessFailureReason.UnknownRole, decision.FailureReason);
    }

    [Fact]
    public void InvalidActorIdIsDenied()
    {
        var user = new CurrentUser("service", "service", "admin", "Service", KejiAuthenticationKind.ApiKey);
        var decision = CreatePolicy(user).Authorize(
            new(new(KejiWorkspaceScope.Shared, null, "file.txt"), KejiFileSystemOperation.Read));
        Assert.False(decision.IsAllowed);
        Assert.Equal(KejiWorkspaceAccessFailureReason.InvalidActorId, decision.FailureReason);
    }

    [Fact]
    public void InvalidOperationIsDenied()
    {
        var decision = CreatePolicy(User("admin")).Authorize(
            new(new(KejiWorkspaceScope.Shared, null, "file.txt"), (KejiFileSystemOperation)999));
        Assert.False(decision.IsAllowed);
        Assert.Equal(KejiWorkspaceAccessFailureReason.InvalidOperation, decision.FailureReason);
    }

    [Fact]
    public void InvalidPathIsDeniedWithExactPathReason()
    {
        var decision = CreatePolicy(User("admin")).Authorize(
            new(new(KejiWorkspaceScope.Shared, null, "../file.txt"), KejiFileSystemOperation.Read));
        Assert.False(decision.IsAllowed);
        Assert.Equal(KejiWorkspaceAccessFailureReason.CandidateRejected, decision.FailureReason);
        Assert.Equal(KejiPathSandboxFailureReason.TraversalSegment, decision.PathFailureReason);
    }

    private static KejiWorkspaceAccessPolicy CreatePolicy(CurrentUser? user)
    {
        var options = new KejiWorkspaceOptions(Path.Combine(Path.GetTempPath(), "keji-policy-tests"));
        return new(new KejiWorkspacePathCandidateResolver(options), new Accessor(user));
    }

    private static CurrentUser User(string role) =>
        new(OwnId, $"{role}-user", role, role, KejiAuthenticationKind.Jwt);

    private sealed class Accessor(CurrentUser? user) : ICurrentUserAccessor
    {
        public CurrentUser? CurrentUser => user;
    }
}
