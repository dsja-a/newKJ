namespace Keji.Security.Auth;

public static class AuditSinkExtensions
{
    public static async Task TryAuditAuthenticationAsync(IKejiAuditBridge? bridge, string outcome, string path, CancellationToken ct)
    {
        if (bridge is null) return;
        await bridge.AuditAuthenticationAsync(outcome, path, ct).ConfigureAwait(false);
    }
}
