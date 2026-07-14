using Keji.FileSystem.Workspace;
using Keji.Security.Auth;

namespace Keji.FileSystem.Tests.Workspace;

public sealed class KejiWorkspaceAccessPolicyMatrixTests
{
    private const string OwnId = "0123456789abcdef";
    private const string OtherId = "fedcba9876543210";

    [Theory]
    [InlineData("admin", "shared", KejiFileSystemOperation.Read, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("admin", "shared", KejiFileSystemOperation.Write, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("admin", "shared", KejiFileSystemOperation.Create, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("admin", "shared", KejiFileSystemOperation.Delete, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("admin", "shared", KejiFileSystemOperation.Enumerate, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("admin", "own", KejiFileSystemOperation.Read, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("admin", "own", KejiFileSystemOperation.Write, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("admin", "own", KejiFileSystemOperation.Create, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("admin", "own", KejiFileSystemOperation.Delete, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("admin", "own", KejiFileSystemOperation.Enumerate, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("admin", "other", KejiFileSystemOperation.Read, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("admin", "other", KejiFileSystemOperation.Write, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("admin", "other", KejiFileSystemOperation.Create, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("admin", "other", KejiFileSystemOperation.Delete, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("admin", "other", KejiFileSystemOperation.Enumerate, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("member", "shared", KejiFileSystemOperation.Read, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("member", "shared", KejiFileSystemOperation.Write, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("member", "shared", KejiFileSystemOperation.Create, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("member", "shared", KejiFileSystemOperation.Delete, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("member", "shared", KejiFileSystemOperation.Enumerate, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("member", "own", KejiFileSystemOperation.Read, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("member", "own", KejiFileSystemOperation.Write, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("member", "own", KejiFileSystemOperation.Create, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("member", "own", KejiFileSystemOperation.Delete, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("member", "own", KejiFileSystemOperation.Enumerate, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("member", "other", KejiFileSystemOperation.Read, false, KejiWorkspaceAccessFailureReason.OtherUserWorkspaceDenied)]
    [InlineData("member", "other", KejiFileSystemOperation.Write, false, KejiWorkspaceAccessFailureReason.OtherUserWorkspaceDenied)]
    [InlineData("member", "other", KejiFileSystemOperation.Create, false, KejiWorkspaceAccessFailureReason.OtherUserWorkspaceDenied)]
    [InlineData("member", "other", KejiFileSystemOperation.Delete, false, KejiWorkspaceAccessFailureReason.OtherUserWorkspaceDenied)]
    [InlineData("member", "other", KejiFileSystemOperation.Enumerate, false, KejiWorkspaceAccessFailureReason.OtherUserWorkspaceDenied)]
    [InlineData("readonly", "shared", KejiFileSystemOperation.Read, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("readonly", "shared", KejiFileSystemOperation.Write, false, KejiWorkspaceAccessFailureReason.ReadonlyWriteDenied)]
    [InlineData("readonly", "shared", KejiFileSystemOperation.Create, false, KejiWorkspaceAccessFailureReason.ReadonlyWriteDenied)]
    [InlineData("readonly", "shared", KejiFileSystemOperation.Delete, false, KejiWorkspaceAccessFailureReason.ReadonlyWriteDenied)]
    [InlineData("readonly", "shared", KejiFileSystemOperation.Enumerate, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("readonly", "own", KejiFileSystemOperation.Read, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("readonly", "own", KejiFileSystemOperation.Write, false, KejiWorkspaceAccessFailureReason.ReadonlyWriteDenied)]
    [InlineData("readonly", "own", KejiFileSystemOperation.Create, false, KejiWorkspaceAccessFailureReason.ReadonlyWriteDenied)]
    [InlineData("readonly", "own", KejiFileSystemOperation.Delete, false, KejiWorkspaceAccessFailureReason.ReadonlyWriteDenied)]
    [InlineData("readonly", "own", KejiFileSystemOperation.Enumerate, true, KejiWorkspaceAccessFailureReason.None)]
    [InlineData("readonly", "other", KejiFileSystemOperation.Read, false, KejiWorkspaceAccessFailureReason.OtherUserWorkspaceDenied)]
    [InlineData("readonly", "other", KejiFileSystemOperation.Write, false, KejiWorkspaceAccessFailureReason.OtherUserWorkspaceDenied)]
    [InlineData("readonly", "other", KejiFileSystemOperation.Create, false, KejiWorkspaceAccessFailureReason.OtherUserWorkspaceDenied)]
    [InlineData("readonly", "other", KejiFileSystemOperation.Delete, false, KejiWorkspaceAccessFailureReason.OtherUserWorkspaceDenied)]
    [InlineData("readonly", "other", KejiFileSystemOperation.Enumerate, false, KejiWorkspaceAccessFailureReason.OtherUserWorkspaceDenied)]
    public void CompleteRoleScopeOperationMatrixReturnsExactDecision(
        string role,
        string target,
        KejiFileSystemOperation operation,
        bool expectedAllowed,
        KejiWorkspaceAccessFailureReason expectedFailureReason)
    {
        var policy = CreatePolicy(new FixedCurrentUserAccessor(User(role)));
        var request = new KejiWorkspaceAccessRequest(PathFor(target), operation);

        var decision = policy.Authorize(request);

        Assert.Equal(expectedAllowed, decision.IsAllowed);
        Assert.Equal(expectedFailureReason, decision.FailureReason);
        Assert.Equal(KejiPathSandboxFailureReason.None, decision.PathFailureReason);
        if (expectedAllowed)
            Assert.NotNull(decision.Candidate);
        else
            Assert.Null(decision.Candidate);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("member")]
    [InlineData("readonly")]
    public void UppercaseTargetUserIdIsRejectedForEveryKnownRole(string role)
    {
        var policy = CreatePolicy(new FixedCurrentUserAccessor(User(role)));
        var path = new KejiWorkspacePathRequest(
            KejiWorkspaceScope.User,
            OwnId.ToUpperInvariant(),
            "file.txt");

        var decision = policy.Authorize(
            new KejiWorkspaceAccessRequest(path, KejiFileSystemOperation.Read));

        Assert.False(decision.IsAllowed);
        Assert.Equal(KejiWorkspaceAccessFailureReason.CandidateRejected, decision.FailureReason);
        Assert.Equal(KejiPathSandboxFailureReason.InvalidUserId, decision.PathFailureReason);
        Assert.Null(decision.Candidate);
    }

    [Fact]
    public void NullRequestIsRejectedAsInvalidRequestForAuthenticatedUser()
    {
        var policy = CreatePolicy(new FixedCurrentUserAccessor(User("admin")));

        var decision = policy.Authorize(null);

        Assert.False(decision.IsAllowed);
        Assert.Equal(KejiWorkspaceAccessFailureReason.InvalidRequest, decision.FailureReason);
        Assert.Equal(KejiPathSandboxFailureReason.None, decision.PathFailureReason);
        Assert.Null(decision.Candidate);
    }

    [Fact]
    public void NullPathIsRejectedAsInvalidRequestForAuthenticatedUser()
    {
        var policy = CreatePolicy(new FixedCurrentUserAccessor(User("member")));
        var request = new KejiWorkspaceAccessRequest(null, KejiFileSystemOperation.Read);

        var decision = policy.Authorize(request);

        Assert.False(decision.IsAllowed);
        Assert.Equal(KejiWorkspaceAccessFailureReason.InvalidRequest, decision.FailureReason);
        Assert.Equal(KejiPathSandboxFailureReason.None, decision.PathFailureReason);
        Assert.Null(decision.Candidate);
    }

    [Fact]
    public async Task ConcurrentAuthorizationKeepsUsersAndDecisionsIsolated()
    {
        var accessor = new AsyncLocalCurrentUserAccessor();
        var policy = CreatePolicy(accessor);
        using var barrier = new Barrier(5);

        var admin = AuthorizeConcurrently(
            policy,
            accessor,
            barrier,
            User("admin"),
            new KejiWorkspaceAccessRequest(PathFor("other"), KejiFileSystemOperation.Delete));
        var memberOther = AuthorizeConcurrently(
            policy,
            accessor,
            barrier,
            User("member"),
            new KejiWorkspaceAccessRequest(PathFor("other"), KejiFileSystemOperation.Read));
        var readonlyWrite = AuthorizeConcurrently(
            policy,
            accessor,
            barrier,
            User("readonly"),
            new KejiWorkspaceAccessRequest(PathFor("own"), KejiFileSystemOperation.Write));
        var memberOwn = AuthorizeConcurrently(
            policy,
            accessor,
            barrier,
            User("member"),
            new KejiWorkspaceAccessRequest(PathFor("own"), KejiFileSystemOperation.Enumerate));
        var anonymous = AuthorizeConcurrently(
            policy,
            accessor,
            barrier,
            null,
            new KejiWorkspaceAccessRequest(PathFor("shared"), KejiFileSystemOperation.Read));

        var decisions = await Task.WhenAll(admin, memberOther, readonlyWrite, memberOwn, anonymous);

        AssertAllowed(decisions[0], OtherId);
        AssertDenied(decisions[1], KejiWorkspaceAccessFailureReason.OtherUserWorkspaceDenied);
        AssertDenied(decisions[2], KejiWorkspaceAccessFailureReason.ReadonlyWriteDenied);
        AssertAllowed(decisions[3], OwnId);
        AssertDenied(decisions[4], KejiWorkspaceAccessFailureReason.Unauthenticated);
    }

    private static Task<KejiWorkspaceAccessDecision> AuthorizeConcurrently(
        KejiWorkspaceAccessPolicy policy,
        AsyncLocalCurrentUserAccessor accessor,
        Barrier barrier,
        CurrentUser? user,
        KejiWorkspaceAccessRequest request)
    {
        return Task.Run(() =>
        {
            accessor.CurrentUser = user;
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Concurrent authorization test did not reach the barrier.");
            return policy.Authorize(request);
        });
    }

    private static void AssertAllowed(KejiWorkspaceAccessDecision decision, string targetUserId)
    {
        Assert.True(decision.IsAllowed);
        Assert.Equal(KejiWorkspaceAccessFailureReason.None, decision.FailureReason);
        Assert.Equal(KejiPathSandboxFailureReason.None, decision.PathFailureReason);
        Assert.NotNull(decision.Candidate);
        Assert.EndsWith(targetUserId, decision.Candidate.RootPath!, StringComparison.Ordinal);
    }

    private static void AssertDenied(
        KejiWorkspaceAccessDecision decision,
        KejiWorkspaceAccessFailureReason expectedFailureReason)
    {
        Assert.False(decision.IsAllowed);
        Assert.Equal(expectedFailureReason, decision.FailureReason);
        Assert.Equal(KejiPathSandboxFailureReason.None, decision.PathFailureReason);
        Assert.Null(decision.Candidate);
    }

    private static KejiWorkspaceAccessPolicy CreatePolicy(ICurrentUserAccessor accessor)
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "keji-policy-matrix-tests");
        var options = new KejiWorkspaceOptions(workspaceRoot);
        var resolver = new KejiWorkspacePathCandidateResolver(options);
        return new KejiWorkspaceAccessPolicy(resolver, accessor);
    }

    private static KejiWorkspacePathRequest PathFor(string target)
    {
        return target switch
        {
            "shared" => new KejiWorkspacePathRequest(KejiWorkspaceScope.Shared, null, "file.txt"),
            "own" => new KejiWorkspacePathRequest(KejiWorkspaceScope.User, OwnId, "file.txt"),
            "other" => new KejiWorkspacePathRequest(KejiWorkspaceScope.User, OtherId, "file.txt"),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown test target."),
        };
    }

    private static CurrentUser User(string role) =>
        new(OwnId, $"{role}-user", role, role, KejiAuthenticationKind.Jwt);

    private sealed class FixedCurrentUserAccessor(CurrentUser user) : ICurrentUserAccessor
    {
        public CurrentUser? CurrentUser { get; } = user;
    }

    private sealed class AsyncLocalCurrentUserAccessor : ICurrentUserAccessor
    {
        private readonly AsyncLocal<CurrentUser?> _currentUser = new();

        public CurrentUser? CurrentUser
        {
            get => _currentUser.Value;
            set => _currentUser.Value = value;
        }
    }
}
