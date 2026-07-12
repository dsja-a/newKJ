using Keji.Persistence.Models;
using Microsoft.Data.Sqlite;

namespace Keji.Persistence.Repositories;

public class SqliteConversationRepository : IConversationRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IUnixTimeProvider _timeProvider;

    public SqliteConversationRepository(ISqliteConnectionFactory connectionFactory, IUnixTimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider;
    }

    public async Task<ConversationRecord> CreateAsync(string convId, string title = "新对话", string? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.Now;
        using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = """
            INSERT OR IGNORE INTO conversations (id, title, created_at, updated_at, owner_user_id)
            VALUES (@id, @title, @now, @now, @owner)
            """;
        insertCmd.Parameters.AddWithValue("@id", convId);
        insertCmd.Parameters.AddWithValue("@title", title);
        insertCmd.Parameters.AddWithValue("@now", now);
        insertCmd.Parameters.AddWithValue("@owner", (object?)ownerUserId ?? DBNull.Value);
        await insertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (ownerUserId is not null)
        {
            using var claimCmd = conn.CreateCommand();
            claimCmd.CommandText = """
                UPDATE conversations SET owner_user_id = @owner
                WHERE id = @id AND owner_user_id IS NULL
                """;
            claimCmd.Parameters.AddWithValue("@owner", ownerUserId);
            claimCmd.Parameters.AddWithValue("@id", convId);
            await claimCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetInternalAsync(conn, convId, cancellationToken).ConfigureAwait(false)
            ?? throw new KejiPersistenceException($"Conversation '{convId}' not found after create.");
    }

    public async Task<(ConversationRecord? Record, ConversationOwnershipResult Result)> EnsureOwnedAsync(
        string convId, string ownerUserId, string title = "新对话", CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.Now;
        using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var insertCmd = conn.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = """
                INSERT OR IGNORE INTO conversations (id, title, created_at, updated_at, owner_user_id)
                VALUES (@id, @title, @now, @now, @owner)
                """;
            insertCmd.Parameters.AddWithValue("@id", convId);
            insertCmd.Parameters.AddWithValue("@title", title);
            insertCmd.Parameters.AddWithValue("@now", now);
            insertCmd.Parameters.AddWithValue("@owner", ownerUserId);
            var inserted = await insertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (inserted > 0)
            {
                var record = await GetInternalAsync(conn, convId, cancellationToken).ConfigureAwait(false);
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return (record, ConversationOwnershipResult.Created);
            }

            using var updateCmd = conn.CreateCommand();
            updateCmd.Transaction = tx;
            updateCmd.CommandText = """
                UPDATE conversations SET owner_user_id = @owner
                WHERE id = @id AND owner_user_id IS NULL
                """;
            updateCmd.Parameters.AddWithValue("@owner", ownerUserId);
            updateCmd.Parameters.AddWithValue("@id", convId);
            var updated = await updateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (updated > 0)
            {
                var record = await GetInternalAsync(conn, convId, cancellationToken).ConfigureAwait(false);
                await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
                return (record, ConversationOwnershipResult.ClaimedUnowned);
            }

            var final = await GetInternalAsync(conn, convId, cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);

            if (final?.OwnerUserId == ownerUserId)
                return (final, ConversationOwnershipResult.AlreadyOwned);

            return (final, ConversationOwnershipResult.OwnedByAnotherUser);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ConversationRecord?> GetAsync(string convId, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await GetInternalAsync(conn, convId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<ConversationRecord>> ListAsync(int limit = 50, string? ownerUserId = null, CancellationToken cancellationToken = default)
    {
        if (limit < 1) limit = 1;
        if (limit > 500) limit = 500;

        using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();

        if (ownerUserId is not null)
        {
            cmd.CommandText = """
                SELECT id, title, created_at, updated_at, message_count, owner_user_id
                FROM conversations WHERE owner_user_id = @owner
                ORDER BY updated_at DESC LIMIT @lim
                """;
            cmd.Parameters.AddWithValue("@owner", ownerUserId);
        }
        else
        {
            cmd.CommandText = """
                SELECT id, title, created_at, updated_at, message_count, owner_user_id
                FROM conversations ORDER BY updated_at DESC LIMIT @lim
                """;
        }

        cmd.Parameters.AddWithValue("@lim", limit);
        return await ReadConversationListAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> RenameAsync(string convId, string title, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.Now;
        using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE conversations SET title = @title, updated_at = @t WHERE id = @id";
        cmd.Parameters.AddWithValue("@title", title);
        cmd.Parameters.AddWithValue("@t", now);
        cmd.Parameters.AddWithValue("@id", convId);
        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rows > 0;
    }

    public async Task<bool> DeleteAsync(string convId, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        using var delMsgCmd = conn.CreateCommand();
        delMsgCmd.Transaction = tx;
        delMsgCmd.CommandText = "DELETE FROM messages WHERE conversation_id = @id";
        delMsgCmd.Parameters.AddWithValue("@id", convId);
        await delMsgCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        using var delConvCmd = conn.CreateCommand();
        delConvCmd.Transaction = tx;
        delConvCmd.CommandText = "DELETE FROM conversations WHERE id = @id";
        delConvCmd.Parameters.AddWithValue("@id", convId);
        var rows = await delConvCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (rows == 0)
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async Task<ConversationRecord?> GetInternalAsync(SqliteConnection conn, string convId, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title, created_at, updated_at, message_count, owner_user_id FROM conversations WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", convId);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return new ConversationRecord
        {
            Id = reader.GetString(0),
            Title = reader.IsDBNull(1) ? "新对话" : reader.GetString(1),
            CreatedAt = reader.GetDouble(2),
            UpdatedAt = reader.GetDouble(3),
            MessageCount = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
            OwnerUserId = reader.IsDBNull(5) ? null : reader.GetString(5),
        };
    }

    private static async Task<List<ConversationRecord>> ReadConversationListAsync(SqliteCommand cmd, CancellationToken cancellationToken)
    {
        var list = new List<ConversationRecord>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new ConversationRecord
            {
                Id = reader.GetString(0),
                Title = reader.IsDBNull(1) ? "新对话" : reader.GetString(1),
                CreatedAt = reader.GetDouble(2),
                UpdatedAt = reader.GetDouble(3),
                MessageCount = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                OwnerUserId = reader.IsDBNull(5) ? null : reader.GetString(5),
            });
        }
        return list;
    }
}
