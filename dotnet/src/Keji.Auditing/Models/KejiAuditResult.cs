namespace Keji.Auditing.Models;

public enum KejiAuditResult
{
    Written,
    PartialFailure,
    SinkError,
    ValidationError
}
