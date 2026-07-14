namespace Keji.Security.Auth;

public static class AuditSinkExtensions
{
    public static async Task TryAuditAsync(IKejiAuditBridge? bridge, string outcome, string? actorId, string? actorRole, string path, CancellationToken ct)
    {
        if (bridge is null) return;
        try
        {
            await bridge.AuditAuthenticationAsync(outcome, actorId, actorRole, path, ct);
        }
        catch
        {
        }
    }
}
