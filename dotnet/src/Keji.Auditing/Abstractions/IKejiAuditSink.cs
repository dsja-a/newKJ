using Keji.Auditing.Models;

namespace Keji.Auditing.Abstractions;

public interface IKejiAuditSink
{
    Task<KejiAuditSinkResult> WriteAsync(KejiAuditEvent auditEvent, CancellationToken cancellationToken = default);
}
