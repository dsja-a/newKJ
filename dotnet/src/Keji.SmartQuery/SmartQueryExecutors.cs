using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using MySqlConnector;
using Npgsql;

namespace Keji.SmartQuery;

internal abstract class KejiSmartQueryExecutor : IKejiSmartQueryExecutor
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IKejiSmartQuerySecretResolver _secrets;
    protected KejiSmartQueryExecutor(IKejiSmartQuerySecretResolver secrets) => _secrets = secrets;
    public abstract KejiSmartQueryDialect Dialect { get; }
    protected abstract DbConnection CreateConnection(
        KejiSmartQueryDataSource source, string password, KejiSmartQueryOptions options);
    protected virtual Task PrepareReadOnlyAsync(
        DbConnection connection, KejiSmartQueryOptions options, CancellationToken ct) => Task.CompletedTask;
    protected virtual Task ConfigureReadOnlyAsync(
        DbConnection connection, DbTransaction transaction, KejiSmartQueryOptions options,
        CancellationToken ct) => Task.CompletedTask;

    public async Task<KejiSmartQueryResult> ExecuteAsync(
        string runId, KejiSmartQueryDataSource source, KejiCompiledQuery query,
        KejiSmartQueryOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ExecuteCoreAsync(runId,source,query,options,cancellationToken).ConfigureAwait(false);
        }
        catch(OperationCanceledException) { throw; }
        catch(TimeoutException)
        {
            throw new KejiSmartQueryDatabaseTimeoutException();
        }
        catch(DbException exception) when(HasTimeoutCause(exception))
        {
            throw new KejiSmartQueryDatabaseTimeoutException();
        }
    }

    private async Task<KejiSmartQueryResult> ExecuteCoreAsync(
        string runId, KejiSmartQueryDataSource source, KejiCompiledQuery query,
        KejiSmartQueryOptions options, CancellationToken cancellationToken)
    {
        if (source.Dialect != Dialect) throw new InvalidOperationException("Dialect mismatch.");
        var started = Stopwatch.GetTimestamp();
        string? password;
        try { password = await _secrets.ResolveAsync(source.SecretReference, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new KejiSmartQuerySecretUnavailableException();
        }
        if (!ValidSecret(password)) throw new KejiSmartQuerySecretUnavailableException();
        await using var connection = CreateConnection(source, password!, options);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await PrepareReadOnlyAsync(connection, options, cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
        await ConfigureReadOnlyAsync(connection, transaction, options, cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = query.Sql;
        command.CommandTimeout = checked((int)Math.Ceiling(options.QueryTimeout.TotalSeconds));
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
            reader.FieldCount != query.OutputColumns.Length) throw new InvalidOperationException("Invalid result shape.");
        var rows = ImmutableArray.CreateBuilder<KejiSmartQueryRow>();
        var resultBytes = query.OutputColumns.Sum(ByteCount);
        var rowsTruncated = false;
        var cellsTruncated = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (rows.Count == query.Limit) { rowsTruncated = true; break; }
            var values = ImmutableArray.CreateBuilder<KejiSmartQueryValue>(reader.FieldCount);
            var rowBytes = 0;
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var value = ConvertValue(reader.GetValue(i), options, out var bytes, out var truncated);
                rowBytes = checked(rowBytes + bytes);
                if (truncated) cellsTruncated++;
                values.Add(value);
            }
            if (rowBytes > options.MaxRowBytes || resultBytes + rowBytes > options.MaxResultBytes)
            {
                rowsTruncated = true; break;
            }
            resultBytes += rowBytes;
            rows.Add(new(values.ToImmutable()));
        }
        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        return new(runId, KejiSmartQueryStatus.Completed, query.OutputColumns, rows.ToImmutable(),
            rows.Count, rowsTruncated, cellsTruncated, resultBytes,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, false, "",
            "SMART_QUERY_COMPLETED", KejiSmartQueryStopReason.Completed);
    }

    private static bool ValidSecret(string? secret)
    {
        if (string.IsNullOrEmpty(secret) || secret.Any(char.IsControl)) return false;
        try { return StrictUtf8.GetByteCount(secret) <= 4096; }
        catch (EncoderFallbackException) { return false; }
    }
    private static bool HasTimeoutCause(Exception exception)
    {
        for(Exception? current=exception;current is not null;current=current.InnerException)
            if(current is TimeoutException)return true;
        return false;
    }
    private static KejiSmartQueryValue ConvertValue(
        object value, KejiSmartQueryOptions options, out int bytes, out bool truncated)
    {
        truncated = false;
        KejiSmartQueryValue result;
        switch (value)
        {
            case DBNull: result = new(KejiSmartQueryValueKind.Null); break;
            case bool b: result = new(KejiSmartQueryValueKind.Boolean, Boolean: b); break;
            case byte or short or int or long:
                result = new(KejiSmartQueryValueKind.Int64, Int64: Convert.ToInt64(value, CultureInfo.InvariantCulture)); break;
            case decimal d: result = new(KejiSmartQueryValueKind.Decimal, Decimal: d); break;
            case float or double:
                result = new(KejiSmartQueryValueKind.Double, Double: Convert.ToDouble(value, CultureInfo.InvariantCulture)); break;
            case DateOnly date: result = new(KejiSmartQueryValueKind.Date, Date: date); break;
            case DateTime dt:
                result = new(KejiSmartQueryValueKind.DateTime,
                    DateTime: new(DateTime.SpecifyKind(dt, DateTimeKind.Utc))); break;
            case DateTimeOffset dto: result = new(KejiSmartQueryValueKind.DateTime, DateTime: dto); break;
            case Guid guid: result = new(KejiSmartQueryValueKind.Guid, Guid: guid); break;
            case byte[]: result = new(KejiSmartQueryValueKind.BinaryOmitted); break;
            case string text:
                var bounded = TruncateUtf8(text, options.MaxCellBytes, out truncated);
                result = new(KejiSmartQueryValueKind.String, Text: bounded, Truncated: truncated); break;
            default: result = new(KejiSmartQueryValueKind.Unsupported); break;
        }
        bytes = result.Text is null ? 32 : ByteCount(result.Text);
        return result;
    }
    private static string TruncateUtf8(string value, int maxBytes, out bool truncated)
    {
        int count;
        try { count = StrictUtf8.GetByteCount(value); }
        catch (EncoderFallbackException) { truncated = true; return ""; }
        if (count <= maxBytes) { truncated = false; return value; }
        var length = value.Length;
        while (length > 0 && StrictUtf8.GetByteCount(value.AsSpan(0, length)) > maxBytes) length--;
        if (length > 0 && char.IsHighSurrogate(value[length - 1])) length--;
        truncated = true;
        return value[..length];
    }
    private static int ByteCount(string value) => StrictUtf8.GetByteCount(value);
}

internal class MySqlSmartQueryExecutor : KejiSmartQueryExecutor
{
    public MySqlSmartQueryExecutor(IKejiSmartQuerySecretResolver secrets) : base(secrets) { }
    public override KejiSmartQueryDialect Dialect => KejiSmartQueryDialect.MySql;
    protected override DbConnection CreateConnection(
        KejiSmartQueryDataSource source, string password, KejiSmartQueryOptions options)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server=source.Host,Port=checked((uint)source.Port),Database=source.Database,
            UserID=source.Username,Password=password,Pooling=false,AllowLoadLocalInfile=false,
            AllowUserVariables=false,ConnectionTimeout=checked((uint)Math.Ceiling(options.ConnectTimeout.TotalSeconds)),
            DefaultCommandTimeout=checked((uint)Math.Ceiling(options.QueryTimeout.TotalSeconds)),
            SslMode=source.TlsMode switch
            {
                KejiSmartQueryTlsMode.Required=>MySqlSslMode.Required,
                KejiSmartQueryTlsMode.VerifyCertificate=>MySqlSslMode.VerifyCA,
                _=>MySqlSslMode.VerifyFull
            }
        };
        return new MySqlConnection(builder.ConnectionString);
    }
    protected override async Task PrepareReadOnlyAsync(
        DbConnection connection, KejiSmartQueryOptions options, CancellationToken ct)
    {
        await ExecuteControl(connection,null,"SET TRANSACTION READ ONLY",ct).ConfigureAwait(false);
        await ExecuteControl(connection,null,
            "SET SESSION MAX_EXECUTION_TIME = "+((long)options.QueryTimeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),ct)
            .ConfigureAwait(false);
    }
    internal static async Task ExecuteControl(
        DbConnection connection,DbTransaction? transaction,string text,CancellationToken ct)
    {
        await using var command=connection.CreateCommand(); command.Transaction=transaction; command.CommandText=text;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

internal class PostgreSqlSmartQueryExecutor : KejiSmartQueryExecutor
{
    public PostgreSqlSmartQueryExecutor(IKejiSmartQuerySecretResolver secrets) : base(secrets) { }
    public override KejiSmartQueryDialect Dialect=>KejiSmartQueryDialect.PostgreSql;
    protected override DbConnection CreateConnection(
        KejiSmartQueryDataSource source,string password,KejiSmartQueryOptions options)
    {
        var builder=new NpgsqlConnectionStringBuilder
        {
            Host=source.Host,Port=source.Port,Database=source.Database,Username=source.Username,Password=password,
            Pooling=false,Multiplexing=false,Timeout=checked((int)Math.Ceiling(options.ConnectTimeout.TotalSeconds)),
            CommandTimeout=checked((int)Math.Ceiling(options.QueryTimeout.TotalSeconds)),
            SslMode=source.TlsMode switch
            {
                KejiSmartQueryTlsMode.Required=>SslMode.Require,
                KejiSmartQueryTlsMode.VerifyCertificate=>SslMode.VerifyCA,
                _=>SslMode.VerifyFull
            }
        };
        return new NpgsqlConnection(builder.ConnectionString);
    }
    protected override async Task ConfigureReadOnlyAsync(
        DbConnection connection,DbTransaction transaction,KejiSmartQueryOptions options,CancellationToken ct)
    {
        await MySqlSmartQueryExecutor.ExecuteControl(connection,transaction,"SET TRANSACTION READ ONLY",ct).ConfigureAwait(false);
        await MySqlSmartQueryExecutor.ExecuteControl(connection,transaction,
            "SET LOCAL statement_timeout = "+((long)options.QueryTimeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),ct)
            .ConfigureAwait(false);
        await MySqlSmartQueryExecutor.ExecuteControl(connection,transaction,
            "SET LOCAL lock_timeout = "+Math.Min(5000,(long)options.QueryTimeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture),ct)
            .ConfigureAwait(false);
    }
}

internal sealed class KejiSmartQuerySecretUnavailableException : Exception;
internal sealed class KejiSmartQueryDatabaseTimeoutException : Exception;
