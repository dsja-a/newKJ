using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using Keji.Security.Exceptions;
using Keji.Security.Models;
using Keji.Security.Options;

namespace Keji.Security.Services;

public class JwtAccessTokenService : IAccessTokenService
{
    private static readonly HashSet<string> ValidRoles = new(StringComparer.OrdinalIgnoreCase) { "admin", "member", "readonly" };

    private readonly KejiSecurityOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SymmetricSecurityKey _signingKey;
    private readonly TokenValidationParameters _validationParameters;

    private const int MaxTokenLength = 8192;

    public JwtAccessTokenService(KejiSecurityOptions options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;

        if (string.IsNullOrEmpty(options.JwtSecret))
            throw new KejiSecurityConfigurationException("JWT Secret is not configured.");
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(options.JwtSecret);
        _signingKey = new SymmetricSecurityKey(keyBytes);

        _validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(options.JwtClockSkewSeconds),
            ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
            LifetimeValidator = (DateTime? notBefore, DateTime? expires, SecurityToken token, TokenValidationParameters parameters) =>
            {
                var now = _timeProvider.GetUtcNow().UtcDateTime;
                if (expires.HasValue && expires.Value < now.Add(-parameters.ClockSkew))
                    return false;
                if (notBefore.HasValue && notBefore.Value > now.Add(parameters.ClockSkew))
                    return false;
                return true;
            },
        };
    }

    public AccessTokenResult CreateToken(string userId, string username, string role)
    {
        var now = _timeProvider.GetUtcNow();
        var expires = now.AddHours(_options.JwtExpireHours);
        var expiresIn = _options.JwtExpireHours * 3600;
        var jti = Guid.NewGuid().ToString("N");

        var unixNow = now.ToUnixTimeSeconds();
        var claims = new[]
        {
            new Claim("sub", userId),
            new Claim("username", username),
            new Claim("role", role),
            new Claim("jti", jti),
            new Claim("iat", unixNow.ToString(), ClaimValueTypes.Integer64),
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            IssuedAt = now.UtcDateTime,
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256),
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var tokenString = tokenHandler.WriteToken(token);

        return new AccessTokenResult(tokenString, expiresIn);
    }

    public AccessTokenValidationResult ValidateToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength)
            return AccessTokenValidationResult.Fail("Invalid token length.");

        try
        {
            var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = tokenHandler.ValidateToken(token, _validationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwtToken ||
                !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.Ordinal))
            {
                return AccessTokenValidationResult.Fail("Invalid algorithm. Only HS256 is supported.");
            }

            var sub = principal.FindFirst("sub")?.Value;
            var username = principal.FindFirst("username")?.Value;
            var role = principal.FindFirst("role")?.Value;
            var jti = principal.FindFirst("jti")?.Value;
            var iatVal = principal.FindFirst("iat")?.Value;
            var expVal = principal.FindFirst("exp")?.Value;

            if (string.IsNullOrEmpty(sub))
                return AccessTokenValidationResult.Fail("Token missing 'sub' claim.");

            if (string.IsNullOrEmpty(username))
                return AccessTokenValidationResult.Fail("Token missing 'username' claim.");

            if (string.IsNullOrEmpty(role) || !ValidRoles.Contains(role))
                return AccessTokenValidationResult.Fail("Token has invalid or missing 'role' claim.");

            long iat = 0;
            long exp = 0;
            long.TryParse(iatVal, out iat);
            long.TryParse(expVal, out exp);

            var claims = new AccessTokenClaims(sub, username, role, iat, exp, jti ?? "");
            return AccessTokenValidationResult.Success(claims);
        }
        catch (SecurityTokenExpiredException)
        {
            return AccessTokenValidationResult.Fail("Token has expired.");
        }
        catch (SecurityTokenInvalidAlgorithmException)
        {
            return AccessTokenValidationResult.Fail("Invalid algorithm.");
        }
        catch (Exception ex) when (ex is SecurityTokenInvalidSignatureException or
                                    SecurityTokenInvalidLifetimeException or
                                    ArgumentException or
                                    SecurityTokenMalformedException or
                                    SecurityTokenInvalidIssuerException or
                                    SecurityTokenInvalidAudienceException)
        {
            return AccessTokenValidationResult.Fail("Token validation failed.");
        }
    }
}
