using Microsoft.Data.Sqlite;

namespace Keji.Persistence;

public class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly string _connectionString;
    private readonly KejiPersistenceOptions _options;
    private readonly string _resolvedPath;
    private bool _walEnsured;
    private readonly SemaphoreSlim _walLock = new(1, 1);

    public SqliteConnectionFactory(KejiPersistenceOptions options)
    {
        _options = options;
        _resolvedPath = DatabasePathResolver.Resolve(options);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _resolvedPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            DefaultTimeout = options.BusyTimeoutMilliseconds / 1000,
        };
        _connectionString = builder.ConnectionString;
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(_resolvedPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            if (_options.CreateDirectoryIfMissing)
            {
                Directory.CreateDirectory(dir);
            }
            else
            {
                throw new KejiPersistenceException(
                    $"Directory '{dir}' does not exist and CreateDirectoryIfMissing is false.");
            }
        }

        var conn = new SqliteConnection(_connectionString);
        try
        {
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
                await _walLock.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!_walEnsured)
                    {
                        using var walCmd = conn.CreateCommand();
                        walCmd.CommandText = "PRAGMA journal_mode = WAL";
                        var result = await walCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                        if (result?.ToString() == "wal")
                            _walEnsured = true;
                    }
                }
                finally
                {
                    _walLock.Release();
                }
            }

            return conn;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }
}
