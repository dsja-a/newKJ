using Keji.Security.Exceptions;
using Keji.Security.Models;

namespace Keji.Security.Services;

public class UnavailableAccessTokenService : IAccessTokenService
{
    public AccessTokenResult CreateToken(string userId, string username, string role)
    {
        throw new KejiSecurityException("当前认证模式不支持用户登录");
    }

    public AccessTokenValidationResult ValidateToken(string token)
    {
        return AccessTokenValidationResult.Fail("Token validation is not available in the current auth mode.");
    }
}
