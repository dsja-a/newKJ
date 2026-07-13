using Keji.Security.Auth;

namespace Keji.Security.Authorization;

public interface IKejiRolePermissionHintProvider
{
    string GetHint(CurrentUser? user);
}
