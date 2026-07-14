using Keji.Auditing.Models;

namespace Keji.Auditing.Abstractions;

public interface IKejiAuditService
{
    Task<KejiAuditResult> WriteAsync(
        KejiAuditCategory category,
        string action,
        KejiAuditOutcome outcome,
        KejiAuditSeverity severity,
        string targetType,
        string? targetId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);
}
