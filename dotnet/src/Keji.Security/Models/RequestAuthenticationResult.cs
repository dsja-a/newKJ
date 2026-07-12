using Keji.Security.Auth;

namespace Keji.Security.Models;

public class RequestAuthenticationResult
{
    public bool IsAuthenticated { get; }
    public CurrentUser? User { get; }
    public string? FailureMessage { get; }

    private RequestAuthenticationResult(bool isAuthenticated, CurrentUser? user, string? failureMessage)
    {
        IsAuthenticated = isAuthenticated;
        User = user;
        FailureMessage = failureMessage;
    }

    public static RequestAuthenticationResult Authenticated(CurrentUser user)
        => new(true, user, null);

    public static RequestAuthenticationResult NotAuthenticated(string? message = null)
        => new(false, null, message);
}
