namespace Keji.Security.Auth;

public interface IKejiAuditBridge
{
    Task AuditAuthenticationAsync(string outcome, string? actorId, string? actorRole, string path, CancellationToken cancellationToken = default);
    Task AuditAuthorizationAsync(string outcome, string? actorId, string? actorRole, string reason, CancellationToken cancellationToken = default);
}
