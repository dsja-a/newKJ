using System.Collections.Immutable;
using System.Data.Common;
using System.Text.Json;

namespace Keji.SmartQuery;

public enum KejiSmartQueryStatus
{
    Invalid, Completed, InvalidRequest, Unauthenticated, Forbidden, DataSourceNotFound,
    ProviderNotFound, SecretUnavailable, PlanRejected, ExecutionFailed, TimedOut
}
public enum KejiSmartQueryEventType { RunStarted, PlanAccepted, QueryCompleted, SummaryCompleted, Error, RunCompleted }
public enum KejiSmartQueryDialect { MySql, PostgreSql }
public enum KejiSmartQueryTlsMode { Required, VerifyCertificate, VerifyFull }
public enum KejiSmartQueryValueKind { Null, String, Integer, Number, Boolean, Decimal, DateTime }
public enum KejiSmartQueryColumnType { String, Integer, Number, Decimal, Boolean, Date, DateTime, Guid }
public enum KejiSmartQueryFilterOperator
{
    Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual,
    Contains, StartsWith, EndsWith, IsNull, IsNotNull, In
}
public enum KejiSmartQueryLogicalOperator { And, Or }
public enum KejiSmartQuerySortDirection { Ascending, Descending }
public enum KejiSmartQueryJoinType { Inner, Left }
public enum KejiSmartQueryAggregate { None, Count, CountDistinct, Sum, Average, Minimum, Maximum }

public sealed record KejiSmartQueryRequest
{
    public string RunId { get; init; } = "";
    public string DataSourceId { get; init; } = "";
    public string Question { get; init; } = "";
    public int RequestedLimit { get; init; } = 100;
}

public sealed record KejiSmartQueryValue(
    KejiSmartQueryValueKind Kind, string? Text = null, long? Integer = null,
    double? Number = null, decimal? Decimal = null, bool? Boolean = null, DateTimeOffset? DateTime = null);
public sealed record KejiSmartQueryRow(ImmutableArray<KejiSmartQueryValue> Values);
public sealed record KejiSmartQueryResult(
    string RunId, KejiSmartQueryStatus Status, ImmutableArray<string> Columns,
    ImmutableArray<KejiSmartQueryRow> Rows, bool Truncated, string? Summary, string SafeCode);
public sealed record KejiSmartQueryEvent(
    string RunId, long Sequence, KejiSmartQueryEventType Type, DateTimeOffset Timestamp,
    KejiSmartQueryStatus Status = KejiSmartQueryStatus.Invalid, string? SafeCode = null,
    KejiSmartQueryResult? Result = null);

public sealed record KejiSmartQuerySecretReference
{
    public string EnvironmentVariableName { get; }
    public KejiSmartQuerySecretReference(string environmentVariableName)
    {
        if (string.IsNullOrWhiteSpace(environmentVariableName) || environmentVariableName.Length > 128 ||
            !environmentVariableName.All(static c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new ArgumentException("Invalid secret reference.", nameof(environmentVariableName));
        EnvironmentVariableName = environmentVariableName;
    }
    public override string ToString() => "env:" + EnvironmentVariableName;
}

public interface IKejiSmartQuerySecretResolver
{
    string? Resolve(KejiSmartQuerySecretReference reference);
}
public sealed class EnvironmentKejiSmartQuerySecretResolver : IKejiSmartQuerySecretResolver
{
    public string? Resolve(KejiSmartQuerySecretReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return Environment.GetEnvironmentVariable(reference.EnvironmentVariableName);
    }
}

public sealed record KejiSmartQueryColumn(
    string Name, KejiSmartQueryColumnType Type, bool QueryEnabled = true, bool Sensitive = false);
public sealed record KejiSmartQueryForeignKey(
    string Name, string PrincipalTable, string PrincipalColumn, string DependentTable, string DependentColumn,
    bool QueryEnabled = true);
public sealed record KejiSmartQueryTable(
    string Name, ImmutableArray<KejiSmartQueryColumn> Columns, bool QueryEnabled = true);
public sealed record KejiSmartQueryDataSource(
    string Id, string OwnerUserId, KejiSmartQueryDialect Dialect, string Host, int Port,
    string Database, string Username, KejiSmartQuerySecretReference SecretReference,
    KejiSmartQueryTlsMode TlsMode, ImmutableArray<KejiSmartQueryTable> Tables,
    ImmutableArray<KejiSmartQueryForeignKey> ForeignKeys, bool Enabled = true);

public interface IKejiSmartQueryDataSourceCatalog
{
    Task<KejiSmartQueryDataSource?> GetAccessibleAsync(
        string dataSourceId, string userId, CancellationToken cancellationToken = default);
}

public sealed record KejiSmartQueryFilterLeaf(
    string Table, string Column, KejiSmartQueryFilterOperator Operator,
    JsonElement? Value = null, ImmutableArray<JsonElement> Values = default);
public sealed record KejiSmartQueryFilterGroup(
    KejiSmartQueryLogicalOperator Operator, ImmutableArray<KejiSmartQueryFilterNode> Children);
public sealed record KejiSmartQueryFilterNode(
    KejiSmartQueryFilterLeaf? Filter = null, KejiSmartQueryFilterGroup? Group = null);
public sealed record KejiSmartQueryJoin(
    string ForeignKey, KejiSmartQueryJoinType Type = KejiSmartQueryJoinType.Inner);
public sealed record KejiSmartQueryProjection(
    string Table, string Column, string Alias, KejiSmartQueryAggregate Aggregate = KejiSmartQueryAggregate.None);
public sealed record KejiSmartQuerySort(
    string Table, string Column, KejiSmartQuerySortDirection Direction);
public sealed record KejiSmartQueryPlan(
    string From, ImmutableArray<KejiSmartQueryJoin> Joins,
    ImmutableArray<KejiSmartQueryProjection> Select,
    KejiSmartQueryFilterNode? Where, ImmutableArray<KejiSmartQueryProjection> GroupBy,
    ImmutableArray<KejiSmartQuerySort> OrderBy, int Limit);

public sealed record KejiCompiledParameter(string Name, object Value);
public sealed record KejiCompiledQuery(
    string Sql, ImmutableArray<KejiCompiledParameter> Parameters,
    ImmutableArray<string> OutputColumns, int Limit);

public interface IKejiSmartQueryDialectCompiler
{
    KejiSmartQueryDialect Dialect { get; }
    bool TryCompile(KejiSmartQueryPlan plan, KejiSmartQueryDataSource source,
        KejiSmartQueryOptions options, out KejiCompiledQuery? query);
}

public interface IKejiSmartQueryExecutor
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
