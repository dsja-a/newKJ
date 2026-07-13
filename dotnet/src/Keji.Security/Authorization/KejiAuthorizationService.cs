using Keji.Security.Auth;

namespace Keji.Security.Authorization;

public sealed class KejiAuthorizationService : IKejiAuthorizationService
{
    private readonly IKejiRolePermissionMatrix _matrix;

    public KejiAuthorizationService(IKejiRolePermissionMatrix matrix)
    {
        _matrix = matrix;
    }

    public KejiAuthorizationDecision Authorize(CurrentUser? user, KejiPermission permission)
    {
        if (user is null)
            return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.Unauthenticated);

        if (!KejiRoles.IsValid(user.Role))
            return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.UnknownRole);

        if (!KejiPermissionCatalog.IsDefined(permission))
            return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.InvalidPermissionMetadata);

        if (KejiPermissionCatalog.IsWritePermission(permission) &&
            string.Equals(user.Role, KejiRoles.Readonly, StringComparison.Ordinal))
            return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.ReadonlyWriteDenied);

        if (KejiPermissionCatalog.IsAdminOnlyPermission(permission) && !user.IsAdmin)
            return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.AdminRequired);

        var hasPermission = _matrix.HasPermission(user.Role, permission);
        if (!hasPermission)
            return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.PermissionDenied);

        return KejiAuthorizationDecision.Allow();
    }

    public KejiAuthorizationDecision AuthorizeAll(
        CurrentUser? user,
        IReadOnlyList<KejiPermission>? permissions)
    {
        if (user is null)
            return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.Unauthenticated);

        if (!KejiRoles.IsValid(user.Role))
            return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.UnknownRole);

        if (permissions is null || permissions.Count == 0)
            return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.MissingPermissionMetadata);

        foreach (var permission in permissions)
        {
            if (!KejiPermissionCatalog.IsDefined(permission))
                return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.InvalidPermissionMetadata);
        }

        if (string.Equals(user.Role, KejiRoles.Readonly, StringComparison.Ordinal) &&
            permissions.Any(KejiPermissionCatalog.IsWritePermission))
        {
            return KejiAuthorizationDecision.Deny(
                KejiAuthorizationFailureReason.ReadonlyWriteDenied);
        }

        if (!user.IsAdmin && permissions.Any(KejiPermissionCatalog.IsAdminOnlyPermission))
            return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.AdminRequired);

        foreach (var permission in permissions)
        {
            if (!_matrix.HasPermission(user.Role, permission))
                return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.PermissionDenied);
        }

        return KejiAuthorizationDecision.Allow();
    }
}
