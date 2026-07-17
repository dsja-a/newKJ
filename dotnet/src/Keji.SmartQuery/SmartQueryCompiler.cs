using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Keji.SmartQuery;

public abstract class KejiSmartQueryDialectCompiler : IKejiSmartQueryDialectCompiler
{
    public abstract KejiSmartQueryDialect Dialect { get; }
    protected abstract string Quote(string identifier);
    protected abstract string Parameter(int index);
    protected virtual string Limit(string parameter) => " LIMIT " + parameter;

    public bool TryCompile(KejiSmartQueryPlan plan, KejiSmartQueryDataSource source,
        KejiSmartQueryOptions options, out KejiCompiledQuery? query)
    {
        query = null;
        if (source.Dialect != Dialect || plan.Limit is < 1 or > KejiSmartQueryOptions.SystemMaxRows ||
            plan.Select.IsDefaultOrEmpty || plan.Select.Length > options.MaxColumns ||
            plan.OrderBy.IsDefault || plan.OrderBy.Length > options.MaxOrderBy ||
            plan.Joins.IsDefault || plan.GroupBy.IsDefault) return false;

        var tables = source.Tables.Where(static t => t.QueryEnabled)
            .ToDictionary(static t => t.Name, StringComparer.Ordinal);
        if (!tables.TryGetValue(plan.From, out _)) return false;
        var included = new List<string> { plan.From };
        var usedFks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var join in plan.Joins)
        {
            var fk = source.ForeignKeys.SingleOrDefault(f => f.QueryEnabled &&
                string.Equals(f.Name, join.ForeignKey, StringComparison.Ordinal));
            if (fk is null || !usedFks.Add(fk.Name)) return false;
            var principalIncluded = included.Contains(fk.PrincipalTable, StringComparer.Ordinal);
            var dependentIncluded = included.Contains(fk.DependentTable, StringComparer.Ordinal);
            if (principalIncluded == dependentIncluded) return false;
            var next = principalIncluded ? fk.DependentTable : fk.PrincipalTable;
            if (!tables.ContainsKey(next) || included.Contains(next, StringComparer.Ordinal)) return false;
            if (!ColumnAllowed(tables[fk.PrincipalTable], fk.PrincipalColumn) ||
                !ColumnAllowed(tables[fk.DependentTable], fk.DependentColumn)) return false;
            included.Add(next);
        }

        var aliases = included.Select((table, index) => (table, alias: "t" + index.ToString(CultureInfo.InvariantCulture)))
            .ToDictionary(static x => x.table, static x => x.alias, StringComparer.Ordinal);
        var projections = new HashSet<string>(StringComparer.Ordinal);
        var hasAggregate = false;
        foreach (var item in plan.Select)
        {
            if (!ValidAlias(item.Alias) || !projections.Add(item.Alias) ||
                !TryColumn(tables, included, item.Table, item.Column, out var column)) return false;
            if (!AggregateAllowed(item.Aggregate, column!.Type)) return false;
            hasAggregate |= item.Aggregate != KejiSmartQueryAggregate.None;
        }
        foreach (var item in plan.GroupBy)
            if (item.Aggregate != KejiSmartQueryAggregate.None ||
                !TryColumn(tables, included, item.Table, item.Column, out _)) return false;
        if (hasAggregate && plan.Select.Any(p => p.Aggregate == KejiSmartQueryAggregate.None &&
                !plan.GroupBy.Any(g => SameColumn(g, p)))) return false;
        if (!hasAggregate && !plan.GroupBy.IsDefaultOrEmpty) return false;
        foreach (var sort in plan.OrderBy)
            if (!TryColumn(tables, included, sort.Table, sort.Column, out _)) return false;

        var parameters = ImmutableArray.CreateBuilder<KejiCompiledParameter>();
        var nodeCount = 0;
        var leafCount = 0;
        if (!TryFilter(plan.Where, 1, tables, included, aliases, options,
                parameters, ref nodeCount, ref leafCount, out var where)) return false;

        var sql = new StringBuilder("SELECT ");
        sql.AppendJoin(", ", plan.Select.Select(p => Projection(p, aliases)));
        sql.Append(" FROM ").Append(Quote(plan.From)).Append(' ').Append(aliases[plan.From]);
        var joinedTables = new HashSet<string>(StringComparer.Ordinal) { plan.From };
        foreach (var join in plan.Joins)
        {
            var fk = source.ForeignKeys.Single(f => f.Name == join.ForeignKey);
            var next = joinedTables.Contains(fk.PrincipalTable) ? fk.DependentTable : fk.PrincipalTable;
            sql.Append(join.Type == KejiSmartQueryJoinType.Left ? " LEFT JOIN " : " INNER JOIN ")
                .Append(Quote(next)).Append(' ').Append(aliases[next]).Append(" ON ")
                .Append(aliases[fk.DependentTable]).Append('.').Append(Quote(fk.DependentColumn))
                .Append(" = ").Append(aliases[fk.PrincipalTable]).Append('.').Append(Quote(fk.PrincipalColumn));
            joinedTables.Add(next);
        }
        if (where.Length > 0) sql.Append(" WHERE ").Append(where);
        if (!plan.GroupBy.IsDefaultOrEmpty)
            sql.Append(" GROUP BY ").AppendJoin(", ", plan.GroupBy.Select(p => Column(p.Table, p.Column, aliases)));
        if (!plan.OrderBy.IsDefaultOrEmpty)
            sql.Append(" ORDER BY ").AppendJoin(", ", plan.OrderBy.Select(s =>
                Column(s.Table, s.Column, aliases) + (s.Direction == KejiSmartQuerySortDirection.Descending ? " DESC" : " ASC")));
        var limitName = Parameter(parameters.Count);
        parameters.Add(new(limitName, checked(plan.Limit + 1)));
        if (parameters.Count > options.MaxParameters) return false;
        sql.Append(Limit(limitName));
        query = new(sql.ToString(), parameters.ToImmutable(),
            plan.Select.Select(static p => p.Alias).ToImmutableArray(), plan.Limit);
        return true;
    }

    private bool TryFilter(KejiSmartQueryFilterNode? node, int depth,
        IReadOnlyDictionary<string, KejiSmartQueryTable> tables, IReadOnlyCollection<string> included,
        IReadOnlyDictionary<string, string> aliases, KejiSmartQueryOptions options,
        ImmutableArray<KejiCompiledParameter>.Builder parameters, ref int nodes, ref int leaves,
        out string sql)
    {
        sql = "";
        if (node is null) return true;
        if (++nodes > options.MaxFilterNodes || depth > options.MaxFilterDepth ||
            (node.Filter is null) == (node.Group is null)) return false;
        if (node.Filter is { } leaf)
        {
            if (++leaves > options.MaxFilterLeaves ||
                !TryColumn(tables, included, leaf.Table, leaf.Column, out var column) ||
                !OperatorAllowed(leaf.Operator, column!.Type)) return false;
            var left = Column(leaf.Table, leaf.Column, aliases);
            if (leaf.Operator is KejiSmartQueryFilterOperator.IsNull or KejiSmartQueryFilterOperator.IsNotNull)
            {
                if (leaf.Value is not null || !leaf.Values.IsDefaultOrEmpty) return false;
                sql = left + (leaf.Operator == KejiSmartQueryFilterOperator.IsNull ? " IS NULL" : " IS NOT NULL");
                return true;
            }
            if (leaf.Operator == KejiSmartQueryFilterOperator.In)
            {
                if (leaf.Value is not null || leaf.Values.IsDefaultOrEmpty || leaf.Values.Length > options.MaxInItems) return false;
                var names = new List<string>(leaf.Values.Length);
                foreach (var value in leaf.Values)
                {
                    if (!TryValue(value, column.Type, out var scalar)) return false;
                    var name = Parameter(parameters.Count); parameters.Add(new(name, scalar!)); names.Add(name);
                }
                sql = left + " IN (" + string.Join(", ", names) + ")";
                return parameters.Count <= options.MaxParameters;
            }
            if (leaf.Value is null || !leaf.Values.IsDefaultOrEmpty ||
                !TryValue(leaf.Value.Value, column.Type, out var item)) return false;
            if (leaf.Operator is KejiSmartQueryFilterOperator.Contains or
                KejiSmartQueryFilterOperator.StartsWith or KejiSmartQueryFilterOperator.EndsWith)
                item = leaf.Operator switch
                {
                    KejiSmartQueryFilterOperator.Contains => "%" + item + "%",
                    KejiSmartQueryFilterOperator.StartsWith => item + "%",
                    _ => "%" + item
                };
            var parameter = Parameter(parameters.Count);
            parameters.Add(new(parameter, item!));
            sql = left + " " + Operator(leaf.Operator) + " " + parameter;
            return parameters.Count <= options.MaxParameters;
        }
        var group = node.Group!;
        if (group.Children.IsDefaultOrEmpty || group.Children.Length > options.MaxFilterNodes) return false;
        var children = new List<string>(group.Children.Length);
        foreach (var child in group.Children)
        {
            if (!TryFilter(child, depth + 1, tables, included, aliases, options,
                    parameters, ref nodes, ref leaves, out var childSql)) return false;
            children.Add("(" + childSql + ")");
        }
        sql = string.Join(group.Operator == KejiSmartQueryLogicalOperator.And ? " AND " : " OR ", children);
        return true;
    }

    private string Projection(KejiSmartQueryProjection p, IReadOnlyDictionary<string, string> aliases)
    {
        var value = Column(p.Table, p.Column, aliases);
        value = p.Aggregate switch
        {
            KejiSmartQueryAggregate.Count => "COUNT(" + value + ")",
            KejiSmartQueryAggregate.CountDistinct => "COUNT(DISTINCT " + value + ")",
            KejiSmartQueryAggregate.Sum => "SUM(" + value + ")",
            KejiSmartQueryAggregate.Average => "AVG(" + value + ")",
            KejiSmartQueryAggregate.Minimum => "MIN(" + value + ")",
            KejiSmartQueryAggregate.Maximum => "MAX(" + value + ")",
            _ => value
        };
        return value + " AS " + Quote(p.Alias);
    }
    private string Column(string table, string column, IReadOnlyDictionary<string, string> aliases) =>
        aliases[table] + "." + Quote(column);
    private static bool SameColumn(KejiSmartQueryProjection a, KejiSmartQueryProjection b) =>
        a.Table == b.Table && a.Column == b.Column;
    private static bool ValidAlias(string value) => value.Length is > 0 and <= 64 &&
        (char.IsAsciiLetter(value[0]) || value[0] == '_') &&
        value.All(static c => char.IsAsciiLetterOrDigit(c) || c == '_');
    private static bool ColumnAllowed(KejiSmartQueryTable table, string name) =>
        table.QueryEnabled && table.Columns.Any(c => c.Name == name && c.QueryEnabled && !c.Sensitive);
    private static bool TryColumn(IReadOnlyDictionary<string, KejiSmartQueryTable> tables,
        IReadOnlyCollection<string> included, string table, string name, out KejiSmartQueryColumn? column)
    {
        column = null;
        if (!included.Contains(table) || !tables.TryGetValue(table, out var definition)) return false;
        column = definition.Columns.SingleOrDefault(c => c.Name == name && c.QueryEnabled && !c.Sensitive);
        return column is not null;
    }
    private static bool AggregateAllowed(KejiSmartQueryAggregate aggregate, KejiSmartQueryColumnType type) =>
        aggregate is KejiSmartQueryAggregate.None or KejiSmartQueryAggregate.Count or KejiSmartQueryAggregate.CountDistinct or
            KejiSmartQueryAggregate.Minimum or KejiSmartQueryAggregate.Maximum ||
        aggregate is KejiSmartQueryAggregate.Sum or KejiSmartQueryAggregate.Average &&
            type is KejiSmartQueryColumnType.Integer or KejiSmartQueryColumnType.Number or KejiSmartQueryColumnType.Decimal;
    private static bool OperatorAllowed(KejiSmartQueryFilterOperator op, KejiSmartQueryColumnType type) => op switch
    {
        KejiSmartQueryFilterOperator.Contains or KejiSmartQueryFilterOperator.StartsWith or
            KejiSmartQueryFilterOperator.EndsWith => type == KejiSmartQueryColumnType.String,
        KejiSmartQueryFilterOperator.LessThan or KejiSmartQueryFilterOperator.LessThanOrEqual or
            KejiSmartQueryFilterOperator.GreaterThan or KejiSmartQueryFilterOperator.GreaterThanOrEqual =>
            type is not KejiSmartQueryColumnType.Boolean and not KejiSmartQueryColumnType.Guid,
        _ => true
    };
    private static string Operator(KejiSmartQueryFilterOperator op) => op switch
    {
        KejiSmartQueryFilterOperator.Equal => "=",
        KejiSmartQueryFilterOperator.NotEqual => "<>",
        KejiSmartQueryFilterOperator.LessThan => "<",
        KejiSmartQueryFilterOperator.LessThanOrEqual => "<=",
        KejiSmartQueryFilterOperator.GreaterThan => ">",
        KejiSmartQueryFilterOperator.GreaterThanOrEqual => ">=",
        KejiSmartQueryFilterOperator.Contains or KejiSmartQueryFilterOperator.StartsWith or
            KejiSmartQueryFilterOperator.EndsWith => "LIKE",
        _ => throw new ArgumentOutOfRangeException(nameof(op))
    };
    private static bool TryValue(JsonElement value, KejiSmartQueryColumnType type, out object? result)
    {
        result = null;
        if (value.ValueKind == JsonValueKind.Null)
        {
            result = DBNull.Value; return true;
        }
        switch (type)
        {
            case KejiSmartQueryColumnType.String:
            case KejiSmartQueryColumnType.Guid:
                if (value.ValueKind != JsonValueKind.String) return false;
                result = value.GetString()!; return type != KejiSmartQueryColumnType.Guid || Guid.TryParse((string)result, out _);
            case KejiSmartQueryColumnType.Integer:
                if (!value.TryGetInt64(out var integer)) return false; result = integer; return true;
            case KejiSmartQueryColumnType.Number:
                if (!value.TryGetDouble(out var number) || !double.IsFinite(number)) return false; result = number; return true;
            case KejiSmartQueryColumnType.Decimal:
                if (!value.TryGetDecimal(out var dec)) return false; result = dec; return true;
            case KejiSmartQueryColumnType.Boolean:
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                result = value.GetBoolean(); return true;
            case KejiSmartQueryColumnType.Date:
            case KejiSmartQueryColumnType.DateTime:
                if (value.ValueKind != JsonValueKind.String ||
                    !DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var date)) return false;
                result = date; return true;
            default: return false;
        }
    }
}

public sealed class MySqlSmartQueryDialect : KejiSmartQueryDialectCompiler
{
    public override KejiSmartQueryDialect Dialect => KejiSmartQueryDialect.MySql;
    protected override string Quote(string identifier) => "`" + identifier.Replace("`", "``", StringComparison.Ordinal) + "`";
    protected override string Parameter(int index) => "@p" + index.ToString(CultureInfo.InvariantCulture);
}

public sealed class PostgreSqlSmartQueryDialect : KejiSmartQueryDialectCompiler
{
    public override KejiSmartQueryDialect Dialect => KejiSmartQueryDialect.PostgreSql;
    protected override string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    protected override string Parameter(int index) => "@p" + index.ToString(CultureInfo.InvariantCulture);
}
