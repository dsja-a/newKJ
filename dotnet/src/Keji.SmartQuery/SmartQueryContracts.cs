using System.Collections.Immutable;
using System.Data.Common;
using System.Text.Json;

namespace Keji.SmartQuery;

public enum KejiSmartQueryStatus { Invalid, Completed, InvalidRequest, Unauthenticated, Forbidden, DataSourceNotFound, ProviderNotFound, PlanRejected, ExecutionFailed, TimedOut }
public enum KejiSmartQueryValueKind { Null, String, Integer, Number, Boolean }
public enum KejiSmartQueryFilterOperator { Equal, NotEqual, LessThan, LessThanOrEqual, GreaterThan, GreaterThanOrEqual, Contains, StartsWith, IsNull, IsNotNull }
public enum KejiSmartQuerySortDirection { Ascending, Descending }

public sealed record KejiSmartQueryRequest
{
    public string DataSourceId { get; init; } = "";
    public string ProviderName { get; init; } = "";
    public string Model { get; init; } = "";
    public string Question { get; init; } = "";
    public bool IncludeSummary { get; init; }
    public int? MaxRows { get; init; }
}

public sealed record KejiSmartQueryValue(KejiSmartQueryValueKind Kind, string? Text = null, long? Integer = null, double? Number = null, bool? Boolean = null);
public sealed record KejiSmartQueryRow(ImmutableArray<KejiSmartQueryValue> Values);
public sealed record KejiSmartQueryResult(KejiSmartQueryStatus Status, ImmutableArray<string> Columns,
    ImmutableArray<KejiSmartQueryRow> Rows, bool Truncated, string? Summary, string SafeCode);
public sealed record KejiSmartQueryColumn(string Name, string DataType);
public sealed record KejiSmartQueryTable(string Name, ImmutableArray<KejiSmartQueryColumn> Columns);
public sealed record KejiSmartQueryDataSource(string Id, string ConnectionReference, ImmutableArray<KejiSmartQueryTable> EnabledTables);

public interface IKejiSmartQueryDataSourceCatalog
{
    Task<KejiSmartQueryDataSource?> GetAccessibleAsync(string dataSourceId, string userId, CancellationToken cancellationToken = default);
}
public interface IKejiSmartQueryConnectionFactory
{
    Task<DbConnection> OpenReadOnlyAsync(KejiSmartQueryDataSource dataSource, CancellationToken cancellationToken = default);
}

public sealed record KejiSmartQueryFilter(string Column, KejiSmartQueryFilterOperator Operator, JsonElement? Value);
public sealed record KejiSmartQuerySort(string Column, KejiSmartQuerySortDirection Direction);
public sealed record KejiSmartQueryPlan(string Table, ImmutableArray<string> Columns,
    ImmutableArray<KejiSmartQueryFilter> Filters, ImmutableArray<KejiSmartQuerySort> OrderBy, int Limit);
public sealed record KejiCompiledQuery(string Sql, ImmutableArray<KejiCompiledParameter> Parameters);
public sealed record KejiCompiledParameter(string Name, object? Value);

public interface IKejiSmartQuery
{
    Task<KejiSmartQueryResult> ExecuteAsync(KejiSmartQueryRequest request, CancellationToken cancellationToken = default);
}
