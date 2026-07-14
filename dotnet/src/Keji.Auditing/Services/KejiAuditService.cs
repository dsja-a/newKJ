using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Keji.Security.Auth;

namespace Keji.Auditing.Services;

public sealed class KejiAuditService : IKejiAuditService
{
    private readonly ICurrentUserAccessor _userAccessor;
    private readonly IKejiAuditSink _sink;
    private readonly TimeProvider _timeProvider;
    private const string AnonymousActorId = "system";
    private const string AnonymousRole = "unknown";
    private const string UnknownAuthType = "none";

    public KejiAuditService(
        ICurrentUserAccessor userAccessor,
        IKejiAuditSink sink,
        TimeProvider timeProvider)
    {
        _userAccessor = userAccessor;
        _sink = sink;
        _timeProvider = timeProvider;
    }

    public async Task<KejiAuditResult> WriteAsync(
        KejiAuditCategory category,
        string action,
        KejiAuditOutcome outcome,
        KejiAuditSeverity severity,
        string targetType,
        string? targetId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var user = _userAccessor.CurrentUser;

            var actorId = user?.Id ?? AnonymousActorId;
            var actorRole = user?.Role ?? AnonymousRole;
            var authType = user?.AuthenticationKind.ToString() ?? UnknownAuthType;
            var sanitizedMetadata = KejiAuditMetadataSanitizer.Sanitize(metadata);

            var auditEvent = new KejiAuditEvent(
                eventId: Guid.NewGuid(),
                occurredAtUtc: _timeProvider.GetUtcNow().UtcDateTime,
                category: category,
                action: action,
                outcome: outcome,
                severity: severity,
                actorId: actorId,
                actorRole: actorRole,
                authenticationType: authType,
                correlationId: null,
                targetType: targetType,
                targetId: targetId,
                metadata: sanitizedMetadata);

            var sinkResult = await _sink.WriteAsync(auditEvent, cancellationToken).ConfigureAwait(false);

            return sinkResult switch
            {
                KejiAuditSinkResult.Written => KejiAuditResult.Written,
                _ => KejiAuditResult.SinkError,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return KejiAuditResult.SinkError;
        }
    }
}
