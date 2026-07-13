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

        if (KejiPermissionCatalog.IsAdminOnlyPermission(permission) && !user.IsAdmin)
            return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.AdminRequired);

        if (KejiPermissionCatalog.IsWritePermission(permission) && KejiRoles.IsReadonly(user.Role))
            return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.ReadonlyWriteDenied);

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

        if (!user.IsAdmin && permissions.Any(KejiPermissionCatalog.IsAdminOnlyPermission))
            return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.AdminRequired);

        if (KejiRoles.IsReadonly(user.Role) && permissions.Any(KejiPermissionCatalog.IsWritePermission))
        {
            return KejiAuthorizationDecision.Deny(
                KejiAuthorizationFailureReason.ReadonlyWriteDenied);
        }

        foreach (var permission in permissions)
        {
            if (!_matrix.HasPermission(user.Role, permission))
                return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.PermissionDenied);
        }

        return KejiAuthorizationDecision.Allow();
    }
}
