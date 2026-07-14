using System.Text.Json;
using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Keji.Persistence;
using Microsoft.Data.Sqlite;

namespace Keji.Auditing.Sinks;

public sealed class KejiDatabaseAuditSink : IKejiAuditSink
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IUnixTimeProvider _timeProvider;

    public KejiDatabaseAuditSink(
        ISqliteConnectionFactory connectionFactory,
        IUnixTimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider;
    }

    public async Task<KejiAuditSinkResult> WriteAsync(KejiAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SqliteConnection? conn = null;

        try
        {
            conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO audit_events (event_type, actor, session_id, tool_name, path, action, status, detail, client_ip, created_at)
                VALUES (@event_type, @actor, @session_id, @tool_name, @path, @action, @status, @detail, @client_ip, @created_at)
                """;

            cmd.Parameters.AddWithValue("@event_type", auditEvent.Category.ToString());
            cmd.Parameters.AddWithValue("@actor", auditEvent.ActorId);
            cmd.Parameters.AddWithValue("@session_id", auditEvent.CorrelationId ?? string.Empty);
            cmd.Parameters.AddWithValue("@tool_name", auditEvent.TargetType);
            cmd.Parameters.AddWithValue("@path", auditEvent.TargetId ?? string.Empty);
            cmd.Parameters.AddWithValue("@action", auditEvent.Action);
            cmd.Parameters.AddWithValue("@status", auditEvent.Outcome.ToString());
            cmd.Parameters.AddWithValue("@detail", SerializeDetail(auditEvent));
            cmd.Parameters.AddWithValue("@client_ip", string.Empty);
            cmd.Parameters.AddWithValue("@created_at", _timeProvider.Now);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return KejiAuditSinkResult.Written;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return KejiAuditSinkResult.Error;
        }
        finally
        {
            conn?.Dispose();
        }
    }

    private static string SerializeDetail(KejiAuditEvent auditEvent)
    {
        var detail = new Dictionary<string, object?>
        {
            ["event_id"] = auditEvent.EventId.ToString(),
            ["severity"] = auditEvent.Severity.ToString(),
            ["authentication_type"] = auditEvent.AuthenticationType,
            ["actor_role"] = auditEvent.ActorRole,
        };

        if (auditEvent.Metadata.Count > 0)
        {
            detail["metadata"] = auditEvent.Metadata;
        }

        return JsonSerializer.Serialize(detail);
    }
}
