namespace Keji.Auditing.Abstractions;

public interface IKejiAuditCorrelationAccessor
{
    string? CorrelationId { get; }
}
