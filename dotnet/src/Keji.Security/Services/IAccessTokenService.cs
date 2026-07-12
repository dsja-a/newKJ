using Keji.Security.Models;

namespace Keji.Security.Services;

public interface IAccessTokenService
{
    AccessTokenResult CreateToken(string userId, string username, string role);
    AccessTokenValidationResult ValidateToken(string token);
}
