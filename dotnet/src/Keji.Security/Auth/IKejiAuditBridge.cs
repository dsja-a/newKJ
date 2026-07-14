namespace Keji.Security.Auth;

public interface IKejiAuditBridge
{
    Task AuditAuthenticationAsync(string outcome, string path, CancellationToken cancellationToken = default);
    Task AuditAuthorizationAsync(string outcome, string reason, CancellationToken cancellationToken = default);
}
