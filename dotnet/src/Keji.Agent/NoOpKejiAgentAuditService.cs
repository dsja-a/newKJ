using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;

namespace Keji.Agent;

internal sealed class NoOpKejiAgentAuditService : IKejiAuditService
{
    public Task<KejiAuditResult> WriteAsync(
        KejiAuditCategory category,
        string action,
        KejiAuditOutcome outcome,
        KejiAuditSeverity severity,
        string targetType,
        string? targetId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default) => Task.FromResult(KejiAuditResult.Written);
}
