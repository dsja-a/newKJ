using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using MySqlConnector;
using Npgsql;

namespace Keji.SmartQuery;

public abstract class KejiSmartQueryExecutor : IKejiSmartQueryExecutor
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IKejiSmartQuerySecretResolver _secrets;
    protected KejiSmartQueryExecutor(IKejiSmartQuerySecretResolver secrets) => _secrets = secrets;
    public abstract KejiSmartQueryDialect Dialect { get; }
    protected abstract DbConnection CreateConnection(KejiSmartQueryDataSource source, string password);
    protected virtual Task PrepareReadOnlyAsync(DbConnection connection, CancellationToken ct) => Task.CompletedTask;
    protected virtual Task ConfigureReadOnlyAsync(
        DbConnection connection, DbTransaction transaction, CancellationToken ct) => Task.CompletedTask;

    public async Task<KejiSmartQueryResult> ExecuteAsync(string runId, KejiSmartQueryDataSource source,
        KejiCompiledQuery query, KejiSmartQueryOptions options, CancellationToken cancellationToken = default)
    {
        if (source.Dialect != Dialect) throw new InvalidOperationException("Dialect mismatch.");
        string? password;
        try { password = _secrets.Resolve(source.SecretReference); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new KejiSmartQuerySecretUnavailableException();
        }
        if (string.IsNullOrEmpty(password)) throw new KejiSmartQuerySecretUnavailableException();
        await using var connection = CreateConnection(source, password);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await PrepareReadOnlyAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
        await ConfigureReadOnlyAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = query.Sql;
        command.CommandTimeout = options.CommandTimeoutSeconds;
        foreach (var item in query.Parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = item.Name;
            parameter.Value = item.Value;
            command.Parameters.Add(parameter);
        }
        await using var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess | CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);
        if (reader.FieldCount is < 1 || reader.FieldCount > options.MaxColumns ||
            reader.FieldCount != query.OutputColumns.Length)
            throw new InvalidOperationException("Invalid result shape.");
        var rows = ImmutableArray.CreateBuilder<KejiSmartQueryRow>();
        var totalBytes = query.OutputColumns.Sum(ByteCount);
        var truncated = false;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (rows.Count == query.Limit) { truncated = true; break; }
            var values = ImmutableArray.CreateBuilder<KejiSmartQueryValue>(reader.FieldCount);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var value = ConvertValue(reader.GetValue(i), options);
                totalBytes = checked(totalBytes + ValueBytes(value));
                if (totalBytes > options.MaxResultBytes) throw new InvalidOperationException("Result too large.");
                values.Add(value);
            }
            rows.Add(new(values.ToImmutable()));
        }
        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        return new(runId, KejiSmartQueryStatus.Completed, query.OutputColumns,
            rows.ToImmutable(), truncated, null, "SMART_QUERY_COMPLETED");
    }

    private static KejiSmartQueryValue ConvertValue(object value, KejiSmartQueryOptions options) => value switch
    {
        DBNull => new(KejiSmartQueryValueKind.Null),
        bool b => new(KejiSmartQueryValueKind.Boolean, Boolean: b),
        byte or short or int or long => new(KejiSmartQueryValueKind.Integer,
            Integer: Convert.ToInt64(value, CultureInfo.InvariantCulture)),
        decimal d => new(KejiSmartQueryValueKind.Decimal, Decimal: d),
        float or double => new(KejiSmartQueryValueKind.Number,
            Number: Convert.ToDouble(value, CultureInfo.InvariantCulture)),
        DateTime dt => new(KejiSmartQueryValueKind.DateTime,
            DateTime: new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc))),
        DateTimeOffset dto => new(KejiSmartQueryValueKind.DateTime, DateTime: dto),
        _ => Text(Convert.ToString(value, CultureInfo.InvariantCulture) ?? "", options)
    };
    private static KejiSmartQueryValue Text(string value, KejiSmartQueryOptions options)
    {
        if (ByteCount(value) > options.MaxCellBytes) throw new InvalidOperationException("Cell too large.");
        return new(KejiSmartQueryValueKind.String, Text: value);
    }
    private static int ByteCount(string value)
    {
        try { return StrictUtf8.GetByteCount(value); }
        catch (EncoderFallbackException) { throw new InvalidOperationException("Invalid UTF-8 value."); }
    }
    private static int ValueBytes(KejiSmartQueryValue value) => value.Text is null ? 32 : ByteCount(value.Text);
}

public class MySqlSmartQueryExecutor : KejiSmartQueryExecutor
{
    public MySqlSmartQueryExecutor(IKejiSmartQuerySecretResolver secrets) : base(secrets) { }
    public override KejiSmartQueryDialect Dialect => KejiSmartQueryDialect.MySql;
    protected override DbConnection CreateConnection(KejiSmartQueryDataSource source, string password)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = source.Host, Port = checked((uint)source.Port), Database = source.Database,
            UserID = source.Username, Password = password,
            SslMode = source.TlsMode switch
            {
                KejiSmartQueryTlsMode.Required => MySqlSslMode.Required,
                KejiSmartQueryTlsMode.VerifyCertificate => MySqlSslMode.VerifyCA,
                _ => MySqlSslMode.VerifyFull
            },
            ConnectionTimeout = 15, DefaultCommandTimeout = 30,
            Pooling = false
        };
        return new MySqlConnection(builder.ConnectionString);
    }
    protected override async Task PrepareReadOnlyAsync(DbConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SET TRANSACTION READ ONLY";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

public class PostgreSqlSmartQueryExecutor : KejiSmartQueryExecutor
{
    public PostgreSqlSmartQueryExecutor(IKejiSmartQuerySecretResolver secrets) : base(secrets) { }
    public override KejiSmartQueryDialect Dialect => KejiSmartQueryDialect.PostgreSql;
    protected override DbConnection CreateConnection(KejiSmartQueryDataSource source, string password)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = source.Host, Port = source.Port, Database = source.Database,
            Username = source.Username, Password = password,
            SslMode = source.TlsMode switch
            {
                KejiSmartQueryTlsMode.Required => SslMode.Require,
                KejiSmartQueryTlsMode.VerifyCertificate => SslMode.VerifyCA,
                _ => SslMode.VerifyFull
            },
            Pooling = false, Multiplexing = false
        };
        return new NpgsqlConnection(builder.ConnectionString);
    }
    protected override async Task ConfigureReadOnlyAsync(
        DbConnection connection, DbTransaction transaction, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SET TRANSACTION READ ONLY";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

public sealed class KejiSmartQuerySecretUnavailableException : Exception
{
    public KejiSmartQuerySecretUnavailableException() : base("SmartQuery database secret is unavailable.") { }
}
