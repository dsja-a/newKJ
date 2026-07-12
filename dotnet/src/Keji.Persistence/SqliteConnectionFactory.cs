using Microsoft.Data.Sqlite;

namespace Keji.Persistence;

public class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly string _connectionString;
    private readonly KejiPersistenceOptions _options;
    private bool _walEnsured;

    public SqliteConnectionFactory(KejiPersistenceOptions options)
    {
        _options = options;
        var resolvedPath = DatabasePathResolver.Resolve(options);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = resolvedPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            DefaultTimeout = options.BusyTimeoutMilliseconds / 1000,
        };
        _connectionString = builder.ConnectionString;
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var foreignCmd = conn.CreateCommand();
        foreignCmd.CommandText = _options.EnableForeignKeys
            ? "PRAGMA foreign_keys = ON"
            : "PRAGMA foreign_keys = OFF";
        await foreignCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        using var busyCmd = conn.CreateCommand();
        busyCmd.CommandText = $"PRAGMA busy_timeout = {_options.BusyTimeoutMilliseconds}";
        await busyCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        if (_options.EnableWal && !_walEnsured)
        {
            using var walCmd = conn.CreateCommand();
            walCmd.CommandText = "PRAGMA journal_mode = WAL";
            var result = await walCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result?.ToString() == "wal")
                _walEnsured = true;
        }

        return conn;
    }
}
