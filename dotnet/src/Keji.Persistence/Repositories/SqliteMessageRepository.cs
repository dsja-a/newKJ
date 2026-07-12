using Keji.Persistence.Models;
using Microsoft.Data.Sqlite;

namespace Keji.Persistence.Repositories;

public class SqliteMessageRepository : IMessageRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteMessageRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<long> AddAsync(string conversationId, string role, string content, double timestamp, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(role))
            throw new KejiPersistenceException("Message role must not be empty.");

        using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        using var insertCmd = conn.CreateCommand();
        insertCmd.Transaction = tx;
        insertCmd.CommandText = """
            INSERT INTO messages (conversation_id, role, content, created_at)
            VALUES (@cid, @role, @content, @t)
            """;
        insertCmd.Parameters.AddWithValue("@cid", conversationId);
        insertCmd.Parameters.AddWithValue("@role", role);
        insertCmd.Parameters.AddWithValue("@content", content);
        insertCmd.Parameters.AddWithValue("@t", timestamp);
        await insertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        using var updateCmd = conn.CreateCommand();
        updateCmd.Transaction = tx;
        updateCmd.CommandText = """
            UPDATE conversations SET updated_at = @t, message_count = message_count + 1
            WHERE id = @cid
            """;
        updateCmd.Parameters.AddWithValue("@t", timestamp);
        updateCmd.Parameters.AddWithValue("@cid", conversationId);
        var rows = await updateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (rows == 0)
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new KejiPersistenceException($"Conversation '{conversationId}' not found.");
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

        using var getIdCmd = conn.CreateCommand();
        getIdCmd.CommandText = "SELECT last_insert_rowid()";
        var idResult = await getIdCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(idResult);
    }

    public async Task<List<MessageRecord>> ListByConversationAsync(string conversationId, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (limit < 1) limit = 1;
        if (limit > 1000) limit = 1000;

        using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, conversation_id, role, content, created_at
            FROM messages WHERE conversation_id = @cid
            ORDER BY created_at ASC, id ASC LIMIT @lim
            """;
        cmd.Parameters.AddWithValue("@cid", conversationId);
        cmd.Parameters.AddWithValue("@lim", limit);

        var list = new List<MessageRecord>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new MessageRecord
            {
                Id = reader.GetInt64(0),
                ConversationId = reader.GetString(1),
                Role = reader.GetString(2),
                Content = reader.GetString(3),
                CreatedAt = reader.GetDouble(4),
            });
        }
        return list;
    }
}
