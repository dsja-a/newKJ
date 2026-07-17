using System.Collections.Immutable;
using System.Text.Json;

namespace Keji.SmartQuery;

public enum KejiSmartQueryStatus
{
    Invalid = 0, Completed, InvalidRequest, Unauthenticated, Forbidden, DataSourceNotFound,
    ProviderNotFound, SecretUnavailable, MetadataLimitExceeded, InvalidPlan, PlanningFailed,
    ExecutionFailed, QueryTimedOut
}
public enum KejiSmartQueryEventType
{
    Invalid = 0, Started, MetadataLoaded, PlanningStarted, PlanValidated, ExecutionStarted,
    ExecutionCompleted, SummaryStarted, SummaryCompleted, Error, Completed
}
public enum KejiSmartQueryDialect { Invalid = 0, MySql, PostgreSql }
public enum KejiSmartQueryTlsMode { Invalid = 0, Required, VerifyCertificate, VerifyFull }
public enum KejiSmartQueryValueKind
{
    Invalid = 0, Null, String, Int64, Decimal, Double, Boolean, Date, DateTime, Guid,
    BinaryOmitted, Unsupported
}
public enum KejiSmartQueryColumnType
{
    Invalid = 0, String, Integer, Number, Decimal, Boolean, Date, DateTime, Guid
}
public enum KejiSmartQueryFilterOperator
{
    Invalid = 0, Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual,
    Between, Contains, StartsWith, EndsWith, IsNull, IsNotNull, In
}
public enum KejiSmartQueryLogicalOperator { Invalid = 0, And, Or }
public enum KejiSmartQuerySortDirection { Invalid = 0, Ascending, Descending }
public enum KejiSmartQueryJoinType { Invalid = 0, Inner, Left }
public enum KejiSmartQueryAggregate { Invalid = 0, None, Count, CountDistinct, Sum, Average, Minimum, Maximum }
public enum KejiSmartQueryErrorCode
{
    Invalid = 0, InvalidRequest, Unauthenticated, Forbidden, DataSourceNotFound,
    MetadataLimitExceeded, ProviderNotFound, SecretUnavailable, InvalidPlan,
    PlanningFailed, QueryTimedOut, ExecutionFailed
}
public enum KejiSmartQueryStopReason
{
    Invalid = 0, Completed, InvalidRequest, Rejected, Failed, TimedOut, Cancelled
}
public enum KejiSmartQueryDataSourceVisibility { Invalid = 0, Private, Shared }

public sealed record KejiSmartQueryRequest
{
    public string RunId { get; init; } = "";
    public string DataSourceId { get; init; } = "";
    public string Question { get; init; } = "";
    public int RequestedLimit { get; init; } = 100;
}

public sealed record KejiSmartQueryValue(
    KejiSmartQueryValueKind Kind, string? Text = null, long? Int64 = null,
    double? Double = null, decimal? Decimal = null, bool? Boolean = null,
    DateOnly? Date = null, DateTimeOffset? DateTime = null, Guid? Guid = null,
    bool Truncated = false);
public sealed record KejiSmartQueryRow(ImmutableArray<KejiSmartQueryValue> Values);
public sealed record KejiSmartQueryResult(
    string RunId, KejiSmartQueryStatus Status, ImmutableArray<string> Columns,
    ImmutableArray<KejiSmartQueryRow> Rows, int RowsReturned, bool RowsTruncated,
    int CellsTruncated, int ResultBytes, long ExecutionDurationMs, bool SummaryGenerated,
    string Summary, string SafeCode, KejiSmartQueryStopReason StopReason);
public sealed record KejiSmartQueryEvent(
    string RunId, long Sequence, KejiSmartQueryEventType Type, DateTimeOffset Timestamp,
    KejiSmartQueryStatus Status = KejiSmartQueryStatus.Invalid,
    KejiSmartQueryErrorCode ErrorCode = KejiSmartQueryErrorCode.Invalid,
    KejiSmartQueryStopReason StopReason = KejiSmartQueryStopReason.Invalid,
    KejiSmartQueryResult? Result = null);

public sealed record KejiSmartQuerySecretReference
{
    public string Value { get; }
    public string EnvironmentVariableName => Value[4..];
    public KejiSmartQuerySecretReference(string value)
    {
        if (value is null || !value.StartsWith("env:", StringComparison.Ordinal) ||
            value.Length is < 5 or > 132 ||
            !value[4..].All(static c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_') ||
            value[4] is < 'A' or > 'Z')
            throw new ArgumentException("Invalid database secret reference.", nameof(value));
        Value = value;
    }
    public override string ToString() => Value;
}

public interface IKejiSmartQuerySecretResolver
{
    ValueTask<string?> ResolveAsync(
        KejiSmartQuerySecretReference reference, CancellationToken cancellationToken = default);
}
public sealed class EnvironmentKejiSmartQuerySecretResolver : IKejiSmartQuerySecretResolver
{
    public ValueTask<string?> ResolveAsync(
        KejiSmartQuerySecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Environment.GetEnvironmentVariable(reference.EnvironmentVariableName));
    }
}

public sealed record KejiSmartQueryColumn(
    string Name, KejiSmartQueryColumnType Type, bool Nullable = false, string Description = "",
    bool QueryEnabled = true, bool Sensitive = false, int Ordinal = 0);
public sealed record KejiSmartQueryForeignKey(
    string Name, string PrincipalTable, string PrincipalColumn, string DependentTable,
    string DependentColumn, bool QueryEnabled = true, string PrincipalSchema = "public",
    string DependentSchema = "public");
public sealed record KejiSmartQueryTable(
    string Name, ImmutableArray<KejiSmartQueryColumn> Columns, bool QueryEnabled = true,
    string SchemaName = "public", string DisplayName = "", string Description = "",
    string BusinessContext = "", bool QaEnabled = true, long EstimatedRowCount = 0);
public sealed record KejiSmartQueryDataSource(
    string Id, string OwnerUserId, KejiSmartQueryDialect Dialect, string Host, int Port,
    string Database, string Username, KejiSmartQuerySecretReference SecretReference,
    KejiSmartQueryTlsMode TlsMode, ImmutableArray<KejiSmartQueryTable> Tables,
    ImmutableArray<KejiSmartQueryForeignKey> ForeignKeys, bool Enabled = true,
    string DisplayName = "", KejiSmartQueryDataSourceVisibility Visibility = KejiSmartQueryDataSourceVisibility.Private,
    ImmutableArray<string> AllowedSchemas = default, DateTimeOffset CreatedAtUtc = default,
    DateTimeOffset UpdatedAtUtc = default);

public interface IKejiSmartQueryDataSourceCatalog
{
    Task<KejiSmartQueryDataSource?> GetAccessibleAsync(
        string dataSourceId, string userId, bool isAdmin = false,
        CancellationToken cancellationToken = default);
}

public sealed record KejiSmartQueryFilterLeaf(
    string Table, string Column, KejiSmartQueryFilterOperator Operator,
    JsonElement? Value = null, ImmutableArray<JsonElement> Values = default,
    JsonElement? LowerValue = null, JsonElement? UpperValue = null);
public sealed record KejiSmartQueryFilterGroup(
    KejiSmartQueryLogicalOperator Operator, ImmutableArray<KejiSmartQueryFilterNode> Children);
public sealed record KejiSmartQueryFilterNode(
    KejiSmartQueryFilterLeaf? Filter = null, KejiSmartQueryFilterGroup? Group = null);
public sealed record KejiSmartQueryJoin(string ForeignKey, KejiSmartQueryJoinType Type = KejiSmartQueryJoinType.Inner);
public sealed record KejiSmartQueryProjection(
    string Table, string Column, string Alias, KejiSmartQueryAggregate Aggregate = KejiSmartQueryAggregate.None);
public sealed record KejiSmartQuerySort(string Table, string Column, KejiSmartQuerySortDirection Direction);
public sealed record KejiSmartQueryPlan(
    string From, ImmutableArray<KejiSmartQueryJoin> Joins,
    ImmutableArray<KejiSmartQueryProjection> Select, KejiSmartQueryFilterNode? Where,
    ImmutableArray<KejiSmartQueryProjection> GroupBy, ImmutableArray<KejiSmartQuerySort> OrderBy, int Limit);

internal sealed record KejiCompiledParameter(string Name, object Value);
internal sealed record KejiCompiledQuery(
    string Sql, ImmutableArray<KejiCompiledParameter> Parameters,
    ImmutableArray<string> OutputColumns, int Limit, string Fingerprint);
internal interface IKejiSmartQueryDialectCompiler
{
    KejiSmartQueryDialect Dialect { get; }
    bool TryCompile(KejiSmartQueryPlan plan, KejiSmartQueryDataSource source,
        KejiSmartQueryOptions options, out KejiCompiledQuery? query);
}
internal interface IKejiSmartQueryExecutor
{
    KejiSmartQueryDialect Dialect { get; }
    Task<KejiSmartQueryResult> ExecuteAsync(
        string runId, KejiSmartQueryDataSource source, KejiCompiledQuery query,
        KejiSmartQueryOptions options, CancellationToken cancellationToken = default);
}

public interface IKejiSmartQuery
{
    IAsyncEnumerable<KejiSmartQueryEvent> RunStreamAsync(
        KejiSmartQueryRequest request, CancellationToken cancellationToken = default);
    Task<KejiSmartQueryResult> RunAsync(
        KejiSmartQueryRequest request, CancellationToken cancellationToken = default);
}
