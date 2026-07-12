using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Keji.Security.Auth;

public class HttpCurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public CurrentUser? CurrentUser
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext?.User?.Identity?.IsAuthenticated != true)
                return null;

            var principal = httpContext.User;

            var id = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            var username = principal.FindFirstValue(ClaimTypes.Name);
            var role = principal.FindFirstValue(ClaimTypes.Role);
            var displayName = principal.FindFirstValue(KejiClaimTypes.DisplayName);
            var authKindStr = principal.FindFirstValue(KejiClaimTypes.AuthenticationKind);

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(role))
                return null;

            var authKind = authKindStr switch
            {
                "ApiKey" => KejiAuthenticationKind.ApiKey,
                "Localhost" => KejiAuthenticationKind.Localhost,
                _ => KejiAuthenticationKind.Jwt
            };

            return new CurrentUser(id, username, role, displayName ?? username, authKind);
        }
    }
}
