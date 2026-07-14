using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Keji.Security.Auth;
using Microsoft.Extensions.Logging;

namespace Keji.Api.Middleware;

public sealed class KejiAuditBridgeImpl : IKejiAuditBridge
{
    private readonly IKejiAuditService _auditService;
    private readonly ILogger<KejiAuditBridgeImpl> _logger;

    public KejiAuditBridgeImpl(IKejiAuditService auditService, ILogger<KejiAuditBridgeImpl> logger)
    {
        _auditService = auditService;
        _logger = logger;
    }

    public async Task AuditAuthenticationAsync(string outcome, string path, CancellationToken cancellationToken = default)
    {
        var (auditOutcome, severity) = outcome switch
        {
            "success" => (KejiAuditOutcome.Success, KejiAuditSeverity.Information),
            "error" => (KejiAuditOutcome.Error, KejiAuditSeverity.Error),
            _ => (KejiAuditOutcome.Failure, KejiAuditSeverity.Warning),
        };

        var action = outcome == "success" ? "authenticate" : "authenticate_failed";

        try
        {
            var result = await _auditService.WriteAsync(
                KejiAuditCategory.Authentication,
                action: action,
                outcome: auditOutcome,
                severity: severity,
                targetType: "http_request",
                cancellationToken: cancellationToken).ConfigureAwait(false);

            LogResult("AUTH", result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    public async Task AuditAuthorizationAsync(string outcome, string reason, CancellationToken cancellationToken = default)
    {
        var (auditOutcome, severity) = outcome == "allowed"
            ? (KejiAuditOutcome.Success, KejiAuditSeverity.Information)
            : (KejiAuditOutcome.Denied, KejiAuditSeverity.Warning);

        var metadata = new Dictionary<string, string>
        {
            ["reason"] = (reason ?? "unknown").Length > 64 ? reason![..64] : reason ?? "unknown",
        };

        try
        {
            var result = await _auditService.WriteAsync(
                KejiAuditCategory.Authorization,
                action: "authorize",
                outcome: auditOutcome,
                severity: severity,
                targetType: "endpoint",
                metadata: metadata,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            LogResult("AUTHZ", result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    private void LogResult(string prefix, KejiAuditResult result)
    {
        switch (result)
        {
            case KejiAuditResult.PartialFailure:
                _logger.LogWarning("AuditSinkPartialFailure: code={Code}_PARTIAL_SINK_FAILURE", prefix);
                break;
            case KejiAuditResult.SinkError:
                _logger.LogWarning("AuditSinkError: code={Code}_SINK_FAILED", prefix);
                break;
            case KejiAuditResult.ValidationError:
                _logger.LogWarning("AuditValidationError: code={Code}_VALIDATION_FAILED", prefix);
                break;
        }
    }
}
