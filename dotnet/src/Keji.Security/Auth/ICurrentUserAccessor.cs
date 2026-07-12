namespace Keji.Security.Auth;

public interface ICurrentUserAccessor
{
    CurrentUser? CurrentUser { get; }
}
