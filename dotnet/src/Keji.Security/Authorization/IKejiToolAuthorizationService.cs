using Keji.Security.Auth;

namespace Keji.Security.Authorization;

public interface IKejiToolAuthorizationService
{
    KejiAuthorizationDecision Authorize(
        CurrentUser? user,
        KejiToolPermissionDescriptor? descriptor);

    KejiAuthorizationDecision AuthorizeLegacy(CurrentUser? user, string? toolName);
}
