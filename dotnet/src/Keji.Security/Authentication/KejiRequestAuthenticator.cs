using System.Net;
using Keji.Persistence;
using Keji.Persistence.Repositories;
using Keji.Security.Auth;
using Keji.Security.Exceptions;
using Keji.Security.Models;
using Keji.Security.Options;
using Keji.Security.Services;

namespace Keji.Security.Authentication;

public class KejiRequestAuthenticator : IRequestAuthenticator
{
    private readonly KejiSecurityOptions _options;
    private readonly IAccessTokenService? _tokenService;
    private readonly IUserRepository _userRepository;
    private readonly TimeProvider _timeProvider;

    public KejiRequestAuthenticator(
        KejiSecurityOptions options,
        IAccessTokenService? tokenService,
        IUserRepository userRepository,
        TimeProvider timeProvider)
    {
        _options = options;
        _tokenService = tokenService;
        _userRepository = userRepository;
        _timeProvider = timeProvider;
    }

    public async Task<RequestAuthenticationResult> AuthenticateAsync(
        string? authorizationHeader,
        string? xApiKeyHeader,
        string? queryApiKey,
        string? remoteIp,
        CancellationToken cancellationToken = default)
    {
        // Check localhost bypass
        if (_options.AllowLocalhostWithoutAuth && !string.IsNullOrEmpty(remoteIp))
        {
            if (IPAddress.TryParse(remoteIp, out var ip) && IPAddress.IsLoopback(ip))
            {
                return RequestAuthenticationResult.Authenticated(new CurrentUser(
                    "localhost", "localhost", "admin", "Localhost", KejiAuthenticationKind.Localhost));
            }
        }

        var bearerToken = ExtractBearerToken(authorizationHeader);

        if (bearerToken != null)
        {
            return await AuthenticateBearerAsync(bearerToken, cancellationToken);
        }

        // Try X-API-Key
        var apiKey = xApiKeyHeader;
        if (string.IsNullOrEmpty(apiKey))
        {
            apiKey = _options.AllowApiKeyInQuery ? queryApiKey : null;
        }

        if (!string.IsNullOrEmpty(apiKey))
        {
            return AuthenticateWithApiKey(apiKey);
        }

        return RequestAuthenticationResult.NotAuthenticated();
    }

    private string? ExtractBearerToken(string? authorizationHeader)
    {
        if (string.IsNullOrEmpty(authorizationHeader))
            return null;

        if (!authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        var token = authorizationHeader["Bearer ".Length..].Trim();
        return token.Length > 0 ? token : null;
    }

    private async Task<RequestAuthenticationResult> AuthenticateBearerAsync(string bearerToken, CancellationToken cancellationToken)
    {
        if (_options.AuthMode != KejiAuthMode.ApiKeyOnly && _tokenService != null)
        {
            // Try as JWT first
            var validationResult = _tokenService.ValidateToken(bearerToken);
            if (validationResult.IsValid && validationResult.Claims != null)
            {
                return await AuthenticateWithJwtClaimsAsync(validationResult.Claims, cancellationToken);
            }

            if (_options.AuthMode == KejiAuthMode.UserOnly)
                return RequestAuthenticationResult.NotAuthenticated();
        }

        // Try as API Key
        if (_options.AuthMode != KejiAuthMode.UserOnly)
        {
            return AuthenticateWithApiKey(bearerToken);
        }

        return RequestAuthenticationResult.NotAuthenticated();
    }

    private async Task<RequestAuthenticationResult> AuthenticateWithJwtClaimsAsync(AccessTokenClaims claims, CancellationToken cancellationToken)
    {
        try
        {
            var user = await _userRepository.GetByIdAsync(claims.Sub, cancellationToken);
            if (user == null)
                return RequestAuthenticationResult.NotAuthenticated("账号已禁用或不存在");

            if (!user.IsActive)
                return RequestAuthenticationResult.NotAuthenticated("账号已禁用或不存在");

            return RequestAuthenticationResult.Authenticated(new CurrentUser(
                user.Id, user.Username, user.Role, user.DisplayName, KejiAuthenticationKind.Jwt));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (KejiPersistenceException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new KejiSecurityException("Internal authentication error.");
        }
    }

    private RequestAuthenticationResult AuthenticateWithApiKey(string apiKey)
    {
        if (InternalApiKeyComparer.IsValid(apiKey, _options.ApiKey))
        {
            return RequestAuthenticationResult.Authenticated(new CurrentUser(
                "service", "api_key", "admin", "API Key", KejiAuthenticationKind.ApiKey));
        }

        return RequestAuthenticationResult.NotAuthenticated();
    }
}
