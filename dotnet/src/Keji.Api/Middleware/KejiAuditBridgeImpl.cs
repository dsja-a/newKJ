using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Keji.Security.Auth;

namespace Keji.Api.Middleware;

public sealed class KejiAuditBridgeImpl : IKejiAuditBridge
{
    private readonly IKejiAuditService _auditService;

    public KejiAuditBridgeImpl(IKejiAuditService auditService)
    {
        _auditService = auditService;
    }

    public async Task AuditAuthenticationAsync(string outcome, string? actorId, string? actorRole, string path, CancellationToken cancellationToken = default)
    {
        var (auditOutcome, severity) = outcome switch
        {
            "success" => (KejiAuditOutcome.Success, KejiAuditSeverity.Information),
            "error" => (KejiAuditOutcome.Error, KejiAuditSeverity.Error),
            _ => (KejiAuditOutcome.Failure, KejiAuditSeverity.Warning),
        };

        var metadata = new Dictionary<string, string>
        {
            ["path"] = path.Length > 256 ? path[..256] : path,
        };

        await _auditService.WriteAsync(
            KejiAuditCategory.Authentication,
            action: outcome == "success" ? "authenticate" : "authenticate_failed",
            outcome: auditOutcome,
            severity: severity,
            targetType: "http_request",
            metadata: metadata,
            cancellationToken: cancellationToken);
    }

    public async Task AuditAuthorizationAsync(string outcome, string? actorId, string? actorRole, string reason, CancellationToken cancellationToken = default)
    {
        var (auditOutcome, severity) = outcome == "allowed"
            ? (KejiAuditOutcome.Success, KejiAuditSeverity.Information)
            : (KejiAuditOutcome.Denied, KejiAuditSeverity.Warning);

        var metadata = new Dictionary<string, string>
        {
            ["reason"] = (reason ?? "unknown").Length > 64 ? reason![..64] : reason ?? "unknown",
        };

        await _auditService.WriteAsync(
            KejiAuditCategory.Authorization,
            action: "authorize",
            outcome: auditOutcome,
            severity: severity,
            targetType: "endpoint",
            metadata: metadata,
            cancellationToken: cancellationToken);
    }
}
