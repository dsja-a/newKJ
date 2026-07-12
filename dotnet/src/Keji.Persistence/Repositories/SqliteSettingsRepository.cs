using Microsoft.Data.Sqlite;

namespace Keji.Persistence.Repositories;

public class SqliteSettingsRepository : ISettingsRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteSettingsRepository(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<string> GetAsync(string key, string defaultValue = "", CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = @k";
        cmd.Parameters.AddWithValue("@k", key);
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is not null && result is not DBNull ? result.ToString()! : defaultValue;
    }

    public async Task SetAsync(string key, string value, double timestamp, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(key))
            throw new KejiPersistenceException("Settings key must not be empty.");

        using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO settings (key, value, updated_at) VALUES (@k, @v, @t)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value, updated_at = excluded.updated_at
            """;
        cmd.Parameters.AddWithValue("@k", key);
        cmd.Parameters.AddWithValue("@v", value);
        cmd.Parameters.AddWithValue("@t", timestamp);
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM settings";
        var dict = new Dictionary<string, string>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            dict[reader.GetString(0)] = reader.GetString(1);
        }
        return dict;
    }
}
