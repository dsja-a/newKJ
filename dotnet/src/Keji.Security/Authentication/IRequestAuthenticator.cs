using Keji.Security.Models;

namespace Keji.Security.Authentication;

public interface IRequestAuthenticator
{
    Task<RequestAuthenticationResult> AuthenticateAsync(string? authorizationHeader, string? xApiKeyHeader, string? queryApiKey, string? remoteIp, CancellationToken cancellationToken = default);
}
