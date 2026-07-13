using Keji.Security.Auth;

namespace Keji.Security.Authorization;

public interface IKejiAuthorizationService
{
    KejiAuthorizationDecision Authorize(CurrentUser? user, KejiPermission permission);
    KejiAuthorizationDecision AuthorizeAll(CurrentUser? user, IReadOnlyList<KejiPermission>? permissions);
}
