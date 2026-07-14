using System.Collections.Immutable;
using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Keji.Security.Auth;
using Keji.Security.Authorization;
using Microsoft.Extensions.Logging;

namespace Keji.Auditing.Services;

public sealed class KejiAuditService : IKejiAuditService
{
    private readonly ICurrentUserAccessor _userAccessor;
    private readonly IEnumerable<IKejiAuditSink> _sinks;
    private readonly TimeProvider _timeProvider;
    private readonly IKejiAuditCorrelationAccessor _correlationAccessor;
    private readonly ILogger<KejiAuditService> _logger;
    private const string AnonymousActorId = "system";
    private const string AnonymousRole = "unknown";
    private const string UnknownAuthType = "none";

    private static readonly HashSet<string> KnownRoles = new(StringComparer.Ordinal)
    {
        KejiRoles.Admin, KejiRoles.Member, KejiRoles.Readonly,
    };

    private static readonly HashSet<string> KnownAuthTypes = new(StringComparer.Ordinal)
    {
        "Jwt", "ApiKey", "None",
    };

    public KejiAuditService(
        ICurrentUserAccessor userAccessor,
        IEnumerable<IKejiAuditSink> sinks,
        TimeProvider timeProvider,
        IKejiAuditCorrelationAccessor correlationAccessor,
        ILogger<KejiAuditService> logger)
    {
        _userAccessor = userAccessor;
        _sinks = sinks;
        _timeProvider = timeProvider;
        _correlationAccessor = correlationAccessor;
        _logger = logger;
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

        var validationError = ValidateInputs(category, outcome, severity, action, targetType, targetId);
        if (validationError.HasValue)
            return validationError.Value;

        var user = _userAccessor.CurrentUser;

        var actorId = user?.Id ?? AnonymousActorId;
        var actorRole = user?.Role ?? AnonymousRole;
        var authType = user?.AuthenticationKind.ToString() ?? UnknownAuthType;

        if (!KnownRoles.Contains(actorRole))
            actorRole = AnonymousRole;
        if (!KnownAuthTypes.Contains(authType))
            authType = UnknownAuthType;

        var rawCorrelationId = _correlationAccessor.CorrelationId;
        var correlationId = SanitizeCorrelationId(rawCorrelationId);

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
            correlationId: correlationId,
            targetType: targetType,
            targetId: targetId,
            metadata: sanitizedMetadata);

        var successCount = 0;
        var failureCount = 0;

        foreach (var sink in _sinks)
        {
            try
            {
                var r = await sink.WriteAsync(auditEvent, cancellationToken).ConfigureAwait(false);
                if (r == KejiAuditSinkResult.Written)
                    successCount++;
                else
                    failureCount++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                failureCount++;
                _logger.LogError("AuditSinkError: sink={SinkType} code=SINK_WRITE_FAILED", sink.GetType().Name);
            }
        }

        if (successCount == 0 && failureCount == 0)
            return KejiAuditResult.SinkError;

        if (failureCount == 0)
            return KejiAuditResult.Written;

        if (successCount > 0)
            return KejiAuditResult.PartialFailure;

        return KejiAuditResult.SinkError;
    }

    private static KejiAuditResult? ValidateInputs(
        KejiAuditCategory category,
        KejiAuditOutcome outcome,
        KejiAuditSeverity severity,
        string action,
        string targetType,
        string? targetId)
    {
        if (!Enum.IsDefined(category))
            return KejiAuditResult.ValidationError;
        if (!Enum.IsDefined(outcome))
            return KejiAuditResult.ValidationError;
        if (!Enum.IsDefined(severity))
            return KejiAuditResult.ValidationError;

        if (string.IsNullOrEmpty(action) || action.Length > 256 || ContainsControlChars(action))
            return KejiAuditResult.ValidationError;

        if (string.IsNullOrEmpty(targetType) || targetType.Length > 128 || ContainsControlChars(targetType))
            return KejiAuditResult.ValidationError;

        if (targetId != null && (targetId.Length > 128 || ContainsControlChars(targetId)))
            return KejiAuditResult.ValidationError;

        return null;
    }

    private static bool ContainsControlChars(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c))
                return true;
        }
        return false;
    }

    private static string? SanitizeCorrelationId(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var span = raw.AsSpan();
        var cleaned = new char[span.Length];
        var written = 0;
        for (var i = 0; i < span.Length && written < 128; i++)
        {
            var c = span[i];
            if (char.IsControl(c)) continue;
            cleaned[written++] = c;
        }
        if (written == 0) return null;
        return new string(cleaned, 0, written);
    }
}
