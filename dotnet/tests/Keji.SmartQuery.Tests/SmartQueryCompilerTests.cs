using System.Collections.Immutable;
using System.Text.Json;
using Keji.SmartQuery;

namespace Keji.SmartQuery.Tests;

public sealed class SmartQueryCompilerTests
{
    private readonly KejiSmartQueryCompiler _compiler = new();
    private readonly KejiSmartQueryOptions _options = new(maxRows: 100);

    [Fact]
    public void CompilesOnlyQuotedIdentifiersAndParameters()
    {
        var plan = Plan(filters: [Filter("name", KejiSmartQueryFilterOperator.Equal, "\" OR 1=1 --")]);
        Assert.True(_compiler.TryCompile(plan, Source(), _options, out var query));
        Assert.Equal("SELECT \"id\", \"name\" FROM \"customers\" WHERE \"name\" = @p0 LIMIT @limit", query.Sql);
        Assert.DoesNotContain("OR 1=1", query.Sql, StringComparison.Ordinal);
        Assert.Equal("\" OR 1=1 --", query.Parameters[0].Value);
        Assert.Equal(11, query.Parameters[1].Value);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("customers;DROP TABLE customers")]
    [InlineData("")]
    public void RejectsUnknownOrInjectedTable(string table)
    {
        Assert.False(_compiler.TryCompile(Plan(table: table), Source(), _options, out _));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("name\" FROM customers --")]
    [InlineData("")]
    public void RejectsUnknownOrInjectedColumn(string column)
    {
        Assert.False(_compiler.TryCompile(Plan(columns: [column]), Source(), _options, out _));
    }

    [Fact]
    public void RejectsDuplicateProjection()
    {
        Assert.False(_compiler.TryCompile(Plan(columns: ["id", "id"]), Source(), _options, out _));
    }

    [Fact]
    public void RejectsNestedFilterValue()
    {
        using var document = JsonDocument.Parse("{\"nested\":true}");
        Assert.False(_compiler.TryCompile(Plan(filters:
            [new("name", KejiSmartQueryFilterOperator.Equal, document.RootElement.Clone())]), Source(), _options, out _));
    }

    [Fact]
    public void CompilesNullPredicateWithoutParameter()
    {
        var plan = Plan(filters: [new("name", KejiSmartQueryFilterOperator.IsNull, null)]);
        Assert.True(_compiler.TryCompile(plan, Source(), _options, out var query));
        Assert.Contains("\"name\" IS NULL", query.Sql, StringComparison.Ordinal);
        Assert.Single(query.Parameters);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void RejectsLimitOutsideBound(int limit)
    {
        Assert.False(_compiler.TryCompile(Plan(limit: limit), Source(), _options, out _));
    }

    [Fact]
    public void ContainsUsesBoundPatternParameter()
    {
        Assert.True(_compiler.TryCompile(Plan(filters:
            [Filter("name", KejiSmartQueryFilterOperator.Contains, "alice")]), Source(), _options, out var query));
        Assert.Equal("%alice%", query.Parameters[0].Value);
    }

    [Fact]
    public void SortIsWhitelistedAndQuoted()
    {
        Assert.True(_compiler.TryCompile(Plan(orderBy:
            [new("name", KejiSmartQuerySortDirection.Descending)]), Source(), _options, out var query));
        Assert.Contains("ORDER BY \"name\" DESC", query.Sql, StringComparison.Ordinal);
    }

    private static KejiSmartQueryFilter Filter(string column, KejiSmartQueryFilterOperator op, object value)
    {
        var json = JsonSerializer.SerializeToElement(value);
        return new(column, op, json);
    }
    private static KejiSmartQueryPlan Plan(string table = "customers", ImmutableArray<string> columns = default,
        ImmutableArray<KejiSmartQueryFilter> filters = default, ImmutableArray<KejiSmartQuerySort> orderBy = default,
        int limit = 10) => new(table, columns.IsDefault ? ["id", "name"] : columns,
            filters.IsDefault ? [] : filters, orderBy.IsDefault ? [] : orderBy, limit);
    internal static KejiSmartQueryDataSource Source() => new("ds_1", "connection_1",
        [new("customers", [new("id", "integer"), new("name", "text"), new("active", "boolean")])]);
}
