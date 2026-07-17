using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace Keji.SmartQuery;

public sealed class KejiSmartQueryCompiler
{
    public bool TryCompile(KejiSmartQueryPlan plan, KejiSmartQueryDataSource source,
        KejiSmartQueryOptions options, out KejiCompiledQuery query)
    {
        query = new("", []);
        var table = source.EnabledTables.FirstOrDefault(t => string.Equals(t.Name, plan.Table, StringComparison.Ordinal));
        if (table is null || plan.Columns.IsDefaultOrEmpty || plan.Columns.Length > options.MaxColumns ||
            plan.Filters.IsDefault || plan.Filters.Length > options.MaxFilters || plan.OrderBy.IsDefault ||
            plan.OrderBy.Length > options.MaxOrderBy || plan.Limit is < 1 || plan.Limit > options.MaxRows) return false;
        var allowed = table.Columns.Select(static c => c.Name).ToHashSet(StringComparer.Ordinal);
        if (plan.Columns.Distinct(StringComparer.Ordinal).Count() != plan.Columns.Length ||
            plan.Columns.Any(c => !allowed.Contains(c)) || plan.Filters.Any(f => !allowed.Contains(f.Column)) ||
            plan.OrderBy.Any(o => !allowed.Contains(o.Column)) ||
            plan.Filters.Any(f => !Enum.IsDefined(f.Operator)) ||
            plan.OrderBy.Any(o => !Enum.IsDefined(o.Direction))) return false;
        var sql = new System.Text.StringBuilder("SELECT ");
        sql.AppendJoin(", ", plan.Columns.Select(Quote)).Append(" FROM ").Append(Quote(table.Name));
        var parameters = ImmutableArray.CreateBuilder<KejiCompiledParameter>();
        if (plan.Filters.Length > 0)
        {
            sql.Append(" WHERE ");
            for (var i = 0; i < plan.Filters.Length; i++)
            {
                if (i > 0) sql.Append(" AND ");
                var filter = plan.Filters[i];
                sql.Append(Quote(filter.Column));
                if (filter.Operator is KejiSmartQueryFilterOperator.IsNull or KejiSmartQueryFilterOperator.IsNotNull)
                {
                    if (filter.Value is not null) return false;
                    sql.Append(filter.Operator == KejiSmartQueryFilterOperator.IsNull ? " IS NULL" : " IS NOT NULL");
                    continue;
                }
                if (!TryScalar(filter.Value, out var value)) return false;
                var name = "@p" + parameters.Count.ToString(CultureInfo.InvariantCulture);
                sql.Append(' ').Append(Operator(filter.Operator)).Append(' ').Append(name);
                parameters.Add(new(name, PatternValue(filter.Operator, value)));
            }
        }
        if (plan.OrderBy.Length > 0)
        {
            sql.Append(" ORDER BY ");
            sql.AppendJoin(", ", plan.OrderBy.Select(o => Quote(o.Column) +
                (o.Direction == KejiSmartQuerySortDirection.Descending ? " DESC" : " ASC")));
        }
        sql.Append(" LIMIT @limit");
        parameters.Add(new("@limit", checked(plan.Limit + 1)));
        query = new(sql.ToString(), parameters.ToImmutable());
        return true;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private static string Operator(KejiSmartQueryFilterOperator op) => op switch
    {
        KejiSmartQueryFilterOperator.Equal => "=", KejiSmartQueryFilterOperator.NotEqual => "<>",
        KejiSmartQueryFilterOperator.LessThan => "<", KejiSmartQueryFilterOperator.LessThanOrEqual => "<=",
        KejiSmartQueryFilterOperator.GreaterThan => ">", KejiSmartQueryFilterOperator.GreaterThanOrEqual => ">=",
        KejiSmartQueryFilterOperator.Contains or KejiSmartQueryFilterOperator.StartsWith => "LIKE",
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };
    private static object? PatternValue(KejiSmartQueryFilterOperator op, object? value) => op switch
    {
        KejiSmartQueryFilterOperator.Contains => "%" + value + "%",
        KejiSmartQueryFilterOperator.StartsWith => value + "%", _ => value,
    };
    private static bool TryScalar(JsonElement? element, out object? value)
    {
        value = null;
        if (element is null) return false;
        var item = element.Value;
        switch (item.ValueKind)
        {
            case JsonValueKind.String: value = item.GetString(); return true;
            case JsonValueKind.Number when item.TryGetInt64(out var n): value = n; return true;
            case JsonValueKind.Number when item.TryGetDouble(out var d) && double.IsFinite(d): value = d; return true;
            case JsonValueKind.True:
            case JsonValueKind.False: value = item.GetBoolean(); return true;
            case JsonValueKind.Null: value = DBNull.Value; return true;
            default: return false;
        }
    }
}
