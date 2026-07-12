using Keji.Persistence.Models;
using Microsoft.Data.Sqlite;

namespace Keji.Persistence.Repositories;

public class SqliteUserRepository : IUserRepository
{
    private static readonly HashSet<string> ValidRoles = new(StringComparer.OrdinalIgnoreCase) { "admin", "member", "readonly" };

    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IUnixTimeProvider _timeProvider;

    public SqliteUserRepository(ISqliteConnectionFactory connectionFactory, IUnixTimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider;
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM users";
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result);
    }

    public async Task<UserAccountRecord?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, password_hash, display_name, role, is_active, created_at, last_login_at FROM users WHERE username = @u";
        cmd.Parameters.AddWithValue("@u", username.Trim());
        return await ReadUserAccountAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserAccountRecord?> GetByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, password_hash, display_name, role, is_active, created_at, last_login_at FROM users WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", userId);
        return await ReadUserAccountAsync(cmd, cancellationToken).ConfigureAwait(false);
    }

    public async Task<List<UserSummaryRecord>> ListAsync(CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, username, display_name, role, is_active, created_at, last_login_at FROM users ORDER BY created_at ASC";
        var list = new List<UserSummaryRecord>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(new UserSummaryRecord
            {
                Id = reader.GetString(0),
                Username = reader.GetString(1),
                DisplayName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                Role = reader.GetString(3),
                IsActive = reader.GetInt32(4) != 0,
                CreatedAt = reader.GetDouble(5),
                LastLoginAt = reader.IsDBNull(6) ? null : reader.GetDouble(6),
            });
        }
        return list;
    }

    public async Task<string> CreateAsync(string username, string passwordHash, string role = "member", string displayName = "", CancellationToken cancellationToken = default)
    {
        var trimmed = username.Trim();
        if (string.IsNullOrEmpty(trimmed))
            throw new KejiPersistenceException("Username must not be empty.");

        if (string.IsNullOrEmpty(passwordHash))
            throw new KejiPersistenceException("Password hash must not be empty.");

        if (!ValidRoles.Contains(role))
            throw new KejiPersistenceException($"Invalid role: '{role}'. Must be one of: admin, member, readonly.");

        var uid = Guid.NewGuid().ToString("N")[..16];
        var now = _timeProvider.Now;
        var display = string.IsNullOrEmpty(displayName) ? trimmed : displayName;

        using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO users (id, username, password_hash, display_name, role, is_active, created_at)
                VALUES (@id, @u, @pwh, @disp, @role, 1, @now)
                """;
            cmd.Parameters.AddWithValue("@id", uid);
            cmd.Parameters.AddWithValue("@u", trimmed);
            cmd.Parameters.AddWithValue("@pwh", passwordHash);
            cmd.Parameters.AddWithValue("@disp", display);
            cmd.Parameters.AddWithValue("@role", role);
            cmd.Parameters.AddWithValue("@now", now);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new DuplicateUsernameException(trimmed);
        }

        return uid;
    }

    public async Task<bool> UpdateAsync(string userId, UpdateUserCommand command, CancellationToken cancellationToken = default)
    {
        var sets = new List<string>();
        var parameters = new List<(string Name, object? Value)>();

        if (command.DisplayName is not null)
        {
            sets.Add("display_name = @display_name");
            parameters.Add(("@display_name", command.DisplayName));
        }

        if (command.Role is not null)
        {
            if (!ValidRoles.Contains(command.Role))
                throw new KejiPersistenceException($"Invalid role: '{command.Role}'.");
            sets.Add("role = @role");
            parameters.Add(("@role", command.Role));
        }

        if (command.IsActive is not null)
        {
            sets.Add("is_active = @is_active");
            parameters.Add(("@is_active", command.IsActive.Value ? 1 : 0));
        }

        if (command.PasswordHash is not null)
        {
            sets.Add("password_hash = @password_hash");
            parameters.Add(("@password_hash", command.PasswordHash));
        }

        using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        if (sets.Count == 0)
        {
            using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT 1 FROM users WHERE id = @id";
            checkCmd.Parameters.AddWithValue("@id", userId);
            var exists = await checkCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return exists is not null;
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE users SET {string.Join(", ", sets)} WHERE id = @id";
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        cmd.Parameters.AddWithValue("@id", userId);

        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rows > 0;
    }

    public async Task TouchLoginAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE users SET last_login_at = @t WHERE id = @id";
        cmd.Parameters.AddWithValue("@t", _timeProvider.Now);
        cmd.Parameters.AddWithValue("@id", userId);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string userId, CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            using var listCmd = conn.CreateCommand();
            listCmd.Transaction = tx;
            listCmd.CommandText = "SELECT id FROM conversations WHERE owner_user_id = @id";
            listCmd.Parameters.AddWithValue("@id", userId);

            var convIds = new List<string>();
            using var reader = await listCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                convIds.Add(reader.GetString(0));

            foreach (var cid in convIds)
            {
                using var delMsgCmd = conn.CreateCommand();
                delMsgCmd.Transaction = tx;
                delMsgCmd.CommandText = "DELETE FROM messages WHERE conversation_id = @cid";
                delMsgCmd.Parameters.AddWithValue("@cid", cid);
                await delMsgCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                using var delConvCmd = conn.CreateCommand();
                delConvCmd.Transaction = tx;
                delConvCmd.CommandText = "DELETE FROM conversations WHERE id = @cid";
                delConvCmd.Parameters.AddWithValue("@cid", cid);
                await delConvCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            using var delUserCmd = conn.CreateCommand();
            delUserCmd.Transaction = tx;
            delUserCmd.CommandText = "DELETE FROM users WHERE id = @id";
            delUserCmd.Parameters.AddWithValue("@id", userId);
            var deleted = await delUserCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (deleted == 0)
            {
                await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (SqliteException ex)
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw new KejiPersistenceException("Failed to delete user. The transaction has been rolled back.", ex);
        }
    }

    private static async Task<UserAccountRecord?> ReadUserAccountAsync(SqliteCommand cmd, CancellationToken cancellationToken)
    {
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return null;

        return new UserAccountRecord
        {
            Id = reader.GetString(0),
            Username = reader.GetString(1),
            PasswordHash = reader.GetString(2),
            DisplayName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
            Role = reader.GetString(4),
            IsActive = reader.GetInt32(5) != 0,
            CreatedAt = reader.GetDouble(6),
            LastLoginAt = reader.IsDBNull(7) ? null : reader.GetDouble(7),
        };
    }
}
