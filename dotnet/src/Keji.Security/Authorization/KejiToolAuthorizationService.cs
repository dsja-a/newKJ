using Keji.Security.Auth;

namespace Keji.Security.Authorization;

public sealed class KejiToolAuthorizationService : IKejiToolAuthorizationService
{
    public KejiAuthorizationDecision Authorize(
        CurrentUser? user,
        KejiToolPermissionDescriptor? descriptor)
    {
        if (user is null)
            return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.Unauthenticated);

        if (descriptor is null)
            return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.InvalidToolDescriptor);

        if (!KejiRoles.IsValid(user.Role))
            return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.UnknownRole);

        switch (descriptor.AccessLevel)
        {
            case KejiToolAccessLevel.Read:
                return KejiAuthorizationDecision.Allow();

            case KejiToolAccessLevel.Write:
                if (string.Equals(user.Role, KejiRoles.Readonly, StringComparison.Ordinal))
                    return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.ReadonlyWriteDenied);
                return KejiAuthorizationDecision.Allow();

            case KejiToolAccessLevel.Admin:
                if (!user.IsAdmin)
                    return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.AdminRequired);
                return KejiAuthorizationDecision.Allow();

            default:
                return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.InvalidToolDescriptor);
        }
    }

    public KejiAuthorizationDecision AuthorizeLegacy(CurrentUser? user, string? toolName)
    {
        if (user is null)
            return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.Unauthenticated);

        if (toolName is null ||
            toolName.Length is 0 or > 256 ||
            string.IsNullOrWhiteSpace(toolName) ||
            toolName.AsSpan().Trim().Length != toolName.Length)
        {
            return KejiAuthorizationDecision.Deny(
                KejiAuthorizationFailureReason.InvalidToolDescriptor);
        }

        if (!KejiLegacyToolPermissionCatalog.TryResolve(toolName, out var descriptor))
            return KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.UnknownTool);

        // This is a compatibility authorization decision only. TASK-010's registry
        // remains responsible for proving that a tool is registered and executable.
        return Authorize(user, descriptor);
    }
}
