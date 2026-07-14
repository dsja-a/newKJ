using Keji.Security.Authorization;
using Keji.Security.Auth;

namespace Keji.FileSystem.Workspace;

public sealed class KejiWorkspaceAccessPolicy : IKejiWorkspaceAccessPolicy
{
    private readonly IKejiWorkspacePathCandidateResolver _resolver;
    private readonly ICurrentUserAccessor _currentUserAccessor;

    public KejiWorkspaceAccessPolicy(
        IKejiWorkspacePathCandidateResolver resolver,
        ICurrentUserAccessor currentUserAccessor)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _currentUserAccessor = currentUserAccessor ??
            throw new ArgumentNullException(nameof(currentUserAccessor));
    }

    public KejiWorkspaceAccessDecision Authorize(KejiWorkspaceAccessRequest? request)
    {
        var currentUser = _currentUserAccessor.CurrentUser;
        if (currentUser is null)
            return KejiWorkspaceAccessDecision.Deny(KejiWorkspaceAccessFailureReason.Unauthenticated);
        if (request is null || request.Path is null)
            return KejiWorkspaceAccessDecision.Deny(KejiWorkspaceAccessFailureReason.InvalidRequest);
        if (!KejiRoles.IsValid(currentUser.Role))
            return KejiWorkspaceAccessDecision.Deny(KejiWorkspaceAccessFailureReason.UnknownRole);
        if (!KejiWorkspaceIdentityRules.IsValidUserId(currentUser.Id))
            return KejiWorkspaceAccessDecision.Deny(KejiWorkspaceAccessFailureReason.InvalidActorId);
        if (!Enum.IsDefined(request.Operation) ||
            request.Operation == KejiFileSystemOperation.Unknown)
            return KejiWorkspaceAccessDecision.Deny(KejiWorkspaceAccessFailureReason.InvalidOperation);

        var candidate = _resolver.Resolve(request.Path);
        if (candidate is null || !candidate.IsValid)
            return KejiWorkspaceAccessDecision.Deny(
                KejiWorkspaceAccessFailureReason.CandidateRejected,
                candidate?.FailureReason ?? KejiPathSandboxFailureReason.PathNormalizationFailed);

        if (request.Path.Scope == KejiWorkspaceScope.User &&
            !KejiRoles.IsAdmin(currentUser.Role) &&
            !string.Equals(
                request.Path.TargetUserId,
                currentUser.Id,
                StringComparison.Ordinal))
            return KejiWorkspaceAccessDecision.Deny(
                KejiWorkspaceAccessFailureReason.OtherUserWorkspaceDenied);

        if (KejiRoles.IsReadonly(currentUser.Role) &&
            request.Operation is KejiFileSystemOperation.Write or
                KejiFileSystemOperation.Create or
                KejiFileSystemOperation.Delete)
            return KejiWorkspaceAccessDecision.Deny(
                KejiWorkspaceAccessFailureReason.ReadonlyWriteDenied);

        return KejiWorkspaceAccessDecision.Allow(candidate);
    }
}
