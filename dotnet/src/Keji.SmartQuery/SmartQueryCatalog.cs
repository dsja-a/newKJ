using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Keji.SmartQuery;

public interface IKejiSmartQueryDataSourceAdministration
{
    Task UpsertAsync(KejiSmartQueryDataSource source, CancellationToken cancellationToken = default);
}

public sealed class SqliteKejiSmartQueryDataSourceCatalog :
    IKejiSmartQueryDataSourceCatalog, IKejiSmartQueryDataSourceAdministration
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private volatile bool _initialized;
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 16 };

    public SqliteKejiSmartQueryDataSourceCatalog(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
    }

    public async Task<KejiSmartQueryDataSource?> GetAccessibleAsync(
        string dataSourceId, string userId, CancellationToken cancellationToken = default)
    {
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT dialect, host, port, database_name, username, secret_reference, tls_mode, metadata_json
            FROM smart_query_data_sources
            WHERE id = $id AND owner_user_id = $user AND enabled = 1
            LIMIT 1
            """;
        command.Parameters.AddWithValue("$id", dataSourceId);
        command.Parameters.AddWithValue("$user", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        try
        {
            var metadata = JsonSerializer.Deserialize<Metadata>(reader.GetString(7), Json);
            if (metadata is null) return null;
            return new(dataSourceId, userId, (KejiSmartQueryDialect)reader.GetInt32(0),
                reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4),
                new(reader.GetString(5)), (KejiSmartQueryTlsMode)reader.GetInt32(6),
                metadata.Tables, metadata.ForeignKeys, true);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    public async Task UpsertAsync(KejiSmartQueryDataSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!KejiSmartQueryValidation.IsSafeSource(source, source.Id, source.OwnerUserId, requireEnabled: false))
            throw new ArgumentException("Unsafe data source.", nameof(source));
        await EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        var metadata = JsonSerializer.Serialize(new Metadata(source.Tables, source.ForeignKeys), Json);
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO smart_query_data_sources
              (id, owner_user_id, dialect, host, port, database_name, username, secret_reference, tls_mode, metadata_json, enabled)
            VALUES ($id,$user,$dialect,$host,$port,$database,$username,$secret,$tls,$metadata,$enabled)
            ON CONFLICT(id, owner_user_id) DO UPDATE SET
              dialect=excluded.dialect, host=excluded.host, port=excluded.port,
              database_name=excluded.database_name, username=excluded.username,
              secret_reference=excluded.secret_reference, tls_mode=excluded.tls_mode,
              metadata_json=excluded.metadata_json, enabled=excluded.enabled
            """;
        command.Parameters.AddWithValue("$id", source.Id);
        command.Parameters.AddWithValue("$user", source.OwnerUserId);
        command.Parameters.AddWithValue("$dialect", (int)source.Dialect);
        command.Parameters.AddWithValue("$host", source.Host);
        command.Parameters.AddWithValue("$port", source.Port);
        command.Parameters.AddWithValue("$database", source.Database);
        command.Parameters.AddWithValue("$username", source.Username);
        command.Parameters.AddWithValue("$secret", source.SecretReference.EnvironmentVariableName);
        command.Parameters.AddWithValue("$tls", (int)source.TlsMode);
        command.Parameters.AddWithValue("$metadata", metadata);
        command.Parameters.AddWithValue("$enabled", source.Enabled ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureCreatedAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initializeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS smart_query_data_sources (
                  id TEXT NOT NULL,
                  owner_user_id TEXT NOT NULL,
                  dialect INTEGER NOT NULL,
                  host TEXT NOT NULL,
                  port INTEGER NOT NULL,
                  database_name TEXT NOT NULL,
                  username TEXT NOT NULL,
                  secret_reference TEXT NOT NULL,
                  tls_mode INTEGER NOT NULL,
                  metadata_json TEXT NOT NULL,
                  enabled INTEGER NOT NULL,
                  PRIMARY KEY (id, owner_user_id)
                );
                """;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally { _initializeGate.Release(); }
    }

    private sealed record Metadata(
        ImmutableArray<KejiSmartQueryTable> Tables,
        ImmutableArray<KejiSmartQueryForeignKey> ForeignKeys);
}

internal static class KejiSmartQueryValidation
{
    internal static bool IsIdentifier(string? value, int max) => value is { Length: > 0 } &&
        value.Length <= max && char.IsAsciiLetterOrDigit(value[0]) &&
        value.All(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    internal static bool IsSafeSource(
        KejiSmartQueryDataSource? source, string id, string userId, bool requireEnabled = true)
    {
        if (source is null || requireEnabled && !source.Enabled ||
            source.Id != id || source.OwnerUserId != userId ||
            !IsIdentifier(source.Id, 128) || !IsIdentifier(source.OwnerUserId, 128) ||
            string.IsNullOrWhiteSpace(source.Host) || source.Host.Length > 253 ||
            source.Host.Any(static c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or ':')) ||
            source.Port is < 1 or > 65535 ||
            !IsIdentifier(source.Database, 128) || !IsIdentifier(source.Username, 128) ||
            !Enum.IsDefined(source.Dialect) || !Enum.IsDefined(source.TlsMode) ||
            source.Tables.IsDefaultOrEmpty || source.Tables.Length > 64 ||
            source.ForeignKeys.IsDefault || source.ForeignKeys.Length > 128) return false;
        if (source.Tables.Select(static t => t.Name).Distinct(StringComparer.Ordinal).Count() != source.Tables.Length)
            return false;
        var tables = source.Tables.ToDictionary(static t => t.Name, StringComparer.Ordinal);
        foreach (var table in source.Tables)
        {
            if (!IsIdentifier(table.Name, 128) || table.Columns.IsDefaultOrEmpty || table.Columns.Length > 128 ||
                table.Columns.Select(static c => c.Name).Distinct(StringComparer.Ordinal).Count() != table.Columns.Length ||
                table.Columns.Any(c => !IsIdentifier(c.Name, 128) || !Enum.IsDefined(c.Type))) return false;
        }
        foreach (var fk in source.ForeignKeys)
        {
            if (!IsIdentifier(fk.Name, 128) || !tables.TryGetValue(fk.PrincipalTable, out var principal) ||
                !tables.TryGetValue(fk.DependentTable, out var dependent) ||
                !principal.Columns.Any(c => c.Name == fk.PrincipalColumn) ||
                !dependent.Columns.Any(c => c.Name == fk.DependentColumn)) return false;
        }
        return true;
    }
}
