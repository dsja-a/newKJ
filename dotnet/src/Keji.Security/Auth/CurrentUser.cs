using Keji.Security.Authorization;

namespace Keji.Security.Auth;

public class CurrentUser
{
    public string Id { get; }
    public string Username { get; }
    public string Role { get; }
    public string DisplayName { get; }
    public KejiAuthenticationKind AuthenticationKind { get; }

    public bool IsAdmin => KejiRoles.IsAdmin(Role);

    public CurrentUser(string id, string username, string role, string displayName, KejiAuthenticationKind authenticationKind)
    {
        Id = id;
        Username = username;
        Role = role;
        DisplayName = displayName;
        AuthenticationKind = authenticationKind;
    }
}
