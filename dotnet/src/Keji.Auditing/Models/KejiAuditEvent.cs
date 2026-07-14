using System.Collections.Immutable;

namespace Keji.Auditing.Models;

public sealed class KejiAuditEvent
{
    public Guid EventId { get; }
    public DateTime OccurredAtUtc { get; }
    public KejiAuditCategory Category { get; }
    public string Action { get; }
    public KejiAuditOutcome Outcome { get; }
    public KejiAuditSeverity Severity { get; }
    public string ActorId { get; }
    public string ActorRole { get; }
    public string AuthenticationType { get; }
    public string? CorrelationId { get; }
    public string TargetType { get; }
    public string? TargetId { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    internal KejiAuditEvent(
        Guid eventId,
        DateTime occurredAtUtc,
        KejiAuditCategory category,
        string action,
        KejiAuditOutcome outcome,
        KejiAuditSeverity severity,
        string actorId,
        string actorRole,
        string authenticationType,
        string? correlationId,
        string targetType,
        string? targetId,
        IReadOnlyDictionary<string, string> metadata)
    {
        EventId = eventId;
        OccurredAtUtc = occurredAtUtc;
        Category = category;
        Action = action;
        Outcome = outcome;
        Severity = severity;
        ActorId = actorId;
        ActorRole = actorRole;
        AuthenticationType = authenticationType;
        CorrelationId = correlationId;
        TargetType = targetType;
        TargetId = targetId;
        Metadata = metadata;
    }
}
