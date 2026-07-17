using System.Collections.Immutable;
using System.Text.Json;
using Keji.SmartQuery;

namespace Keji.SmartQuery.Tests;

public sealed class SmartQueryCompilerTests
{
    private readonly KejiSmartQueryOptions _options = new();

    public static TheoryData<KejiSmartQueryDialect, KejiSmartQueryColumnType,
        KejiSmartQueryFilterOperator, bool> OperatorCompatibility()
    {
        var data = new TheoryData<KejiSmartQueryDialect, KejiSmartQueryColumnType,
            KejiSmartQueryFilterOperator, bool>();
        var types = new[]
        {
            KejiSmartQueryColumnType.String, KejiSmartQueryColumnType.Integer,
            KejiSmartQueryColumnType.Decimal, KejiSmartQueryColumnType.Boolean,
            KejiSmartQueryColumnType.Guid, KejiSmartQueryColumnType.DateTime
        };
        var operators = new[]
        {
            KejiSmartQueryFilterOperator.Equal, KejiSmartQueryFilterOperator.NotEqual,
            KejiSmartQueryFilterOperator.LessThan, KejiSmartQueryFilterOperator.GreaterThanOrEqual,
            KejiSmartQueryFilterOperator.Contains, KejiSmartQueryFilterOperator.StartsWith,
            KejiSmartQueryFilterOperator.EndsWith, KejiSmartQueryFilterOperator.IsNull,
            KejiSmartQueryFilterOperator.IsNotNull, KejiSmartQueryFilterOperator.In
        };
        foreach (var dialect in new[] { KejiSmartQueryDialect.MySql, KejiSmartQueryDialect.PostgreSql })
        foreach (var type in types)
        foreach (var op in operators)
        {
            var expected = op switch
            {
                KejiSmartQueryFilterOperator.Contains or KejiSmartQueryFilterOperator.StartsWith or
                    KejiSmartQueryFilterOperator.EndsWith => type == KejiSmartQueryColumnType.String,
                KejiSmartQueryFilterOperator.LessThan or KejiSmartQueryFilterOperator.GreaterThanOrEqual =>
                    type is not KejiSmartQueryColumnType.Boolean and not KejiSmartQueryColumnType.Guid,
                _ => true
            };
            data.Add(dialect, type, op, expected);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(OperatorCompatibility))]
    public void FilterOperatorCompatibilityIsEnforced(
        KejiSmartQueryDialect dialect, KejiSmartQueryColumnType type,
        KejiSmartQueryFilterOperator op, bool expected)
    {
        var value = Value(type);
        var leaf = op switch
        {
            KejiSmartQueryFilterOperator.IsNull or KejiSmartQueryFilterOperator.IsNotNull =>
                new KejiSmartQueryFilterLeaf("items", "value", op),
            KejiSmartQueryFilterOperator.In =>
                new KejiSmartQueryFilterLeaf("items", "value", op, Values: [value]),
            _ => new KejiSmartQueryFilterLeaf("items", "value", op, value)
        };
        var plan = Plan(where: new(Filter: leaf));
        Assert.Equal(expected, Compiler(dialect).TryCompile(
            plan, Source(dialect, type), _options, out _));
    }

    public static TheoryData<KejiSmartQueryDialect, string, string> UnsafeIdentifiers() => new()
    {
        { KejiSmartQueryDialect.MySql, "items;DROP TABLE x", "value" },
        { KejiSmartQueryDialect.MySql, "items", "value` FROM items --" },
        { KejiSmartQueryDialect.MySql, "missing", "value" },
        { KejiSmartQueryDialect.MySql, "items", "missing" },
        { KejiSmartQueryDialect.MySql, "", "value" },
        { KejiSmartQueryDialect.PostgreSql, "items;DROP TABLE x", "value" },
        { KejiSmartQueryDialect.PostgreSql, "items", "value\" FROM items --" },
        { KejiSmartQueryDialect.PostgreSql, "missing", "value" },
        { KejiSmartQueryDialect.PostgreSql, "items", "missing" },
        { KejiSmartQueryDialect.PostgreSql, "", "value" }
    };

    [Theory]
    [MemberData(nameof(UnsafeIdentifiers))]
    public void UnknownOrInjectedIdentifiersAreRejected(
        KejiSmartQueryDialect dialect, string table, string column)
    {
        var plan = Plan(from: table, select: [new(table, column, "output")]);
        Assert.False(Compiler(dialect).TryCompile(plan, Source(dialect), _options, out _));
    }

    [Theory]
    [InlineData(KejiSmartQueryDialect.MySql, "`items` t0", "LIMIT @p0")]
    [InlineData(KejiSmartQueryDialect.PostgreSql, "\"items\" t0", "LIMIT @p0")]
    public void DialectQuotesIdentifiersAndBindsLimit(
        KejiSmartQueryDialect dialect, string quotedTable, string limit)
    {
        Assert.True(Compiler(dialect).TryCompile(Plan(), Source(dialect), _options, out var query));
        Assert.Contains(quotedTable, query!.Sql, StringComparison.Ordinal);
        Assert.Contains(limit, query.Sql, StringComparison.Ordinal);
        Assert.Equal(11, query.Parameters[^1].Value);
        Assert.DoesNotContain(';', query.Sql);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void SystemRowLimitCannotBeExceeded(int limit)
    {
        Assert.False(Compiler(KejiSmartQueryDialect.PostgreSql).TryCompile(
            Plan(limit: limit), Source(KejiSmartQueryDialect.PostgreSql), _options, out _));
    }

    [Fact]
    public void SensitiveColumnIsRejectedEverywhere()
    {
        var source = Source(KejiSmartQueryDialect.MySql) with
        {
            Tables = [new("items",
                [new("value", KejiSmartQueryColumnType.String),
                 new("secret", KejiSmartQueryColumnType.String, Sensitive: true)])]
        };
        Assert.False(Compiler(source.Dialect).TryCompile(
            Plan(select: [new("items", "secret", "secret")]), source, _options, out _));
        Assert.False(Compiler(source.Dialect).TryCompile(
            Plan(where: new(Filter: new("items", "secret",
                KejiSmartQueryFilterOperator.Equal, JsonSerializer.SerializeToElement("x")))),
            source, _options, out _));
    }

    [Fact]
    public void QueryDisabledColumnIsRejected()
    {
        var source = Source(KejiSmartQueryDialect.PostgreSql) with
        {
            Tables = [new("items", [new("value", KejiSmartQueryColumnType.String, QueryEnabled: false)])]
        };
        Assert.False(Compiler(source.Dialect).TryCompile(Plan(), source, _options, out _));
    }

    [Fact]
    public void EnabledForeignKeyJoinCompilesWithoutCartesianProduct()
    {
        var source = JoinedSource(KejiSmartQueryDialect.PostgreSql);
        var plan = Plan(joins: [new("fk_orders_customer")],
            select: [new("items", "value", "customer"), new("orders", "amount", "amount")]);
        Assert.True(Compiler(source.Dialect).TryCompile(plan, source, _options, out var query));
        Assert.Contains("INNER JOIN \"orders\" t1 ON t1.\"customer_id\" = t0.\"value\"", query!.Sql);
    }

    [Fact]
    public void ArbitraryJoinAndJoinCycleAreRejected()
    {
        var source = JoinedSource(KejiSmartQueryDialect.MySql);
        Assert.False(Compiler(source.Dialect).TryCompile(
            Plan(joins: [new("missing")]), source, _options, out _));
        Assert.False(Compiler(source.Dialect).TryCompile(
            Plan(joins: [new("fk_orders_customer"), new("fk_orders_customer")]), source, _options, out _));
    }

    [Fact]
    public void AggregateRequiresGroupByForPlainProjection()
    {
        var source = JoinedSource(KejiSmartQueryDialect.PostgreSql);
        var select = ImmutableArray.Create(
            new KejiSmartQueryProjection("items", "value", "customer"),
            new KejiSmartQueryProjection("orders", "amount", "total", KejiSmartQueryAggregate.Sum));
        Assert.False(Compiler(source.Dialect).TryCompile(
            Plan(joins: [new("fk_orders_customer")], select: select), source, _options, out _));
        Assert.True(Compiler(source.Dialect).TryCompile(
            Plan(joins: [new("fk_orders_customer")], select: select,
                groupBy: [new("items", "value", "customer")]), source, _options, out var query));
        Assert.Contains("SUM(t1.\"amount\")", query!.Sql);
        Assert.Contains("GROUP BY t0.\"value\"", query.Sql);
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(4, true)]
    public void FilterDepthIsBounded(int depth, bool expected)
    {
        KejiSmartQueryFilterNode node = new(Filter: new("items", "value",
            KejiSmartQueryFilterOperator.Equal, JsonSerializer.SerializeToElement("x")));
        for (var i = 1; i < depth; i++)
            node = new(Group: new(KejiSmartQueryLogicalOperator.And, [node]));
        Assert.Equal(expected, Compiler(KejiSmartQueryDialect.MySql).TryCompile(
            Plan(where: node), Source(KejiSmartQueryDialect.MySql), _options, out _));
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void InListIsBounded(int count, bool expected)
    {
        var values = Enumerable.Range(0, count)
            .Select(static value => JsonSerializer.SerializeToElement(value)).ToImmutableArray();
        var where = new KejiSmartQueryFilterNode(Filter: new("items", "value",
            KejiSmartQueryFilterOperator.In, Values: values));
        Assert.Equal(expected, Compiler(KejiSmartQueryDialect.PostgreSql).TryCompile(
            Plan(where: where), Source(KejiSmartQueryDialect.PostgreSql, KejiSmartQueryColumnType.Integer),
            _options, out _));
    }

    internal static IKejiSmartQueryDialectCompiler Compiler(KejiSmartQueryDialect dialect) =>
        dialect == KejiSmartQueryDialect.MySql ? new MySqlSmartQueryDialect() : new PostgreSqlSmartQueryDialect();
    internal static KejiSmartQueryDataSource Source(
        KejiSmartQueryDialect dialect, KejiSmartQueryColumnType type = KejiSmartQueryColumnType.String) =>
        new("ds_1", "0123456789abcdef", dialect, "db.example.test",
            dialect == KejiSmartQueryDialect.MySql ? 3306 : 5432, "appdb", "reader",
            new("DATABASE_PASSWORD"), KejiSmartQueryTlsMode.VerifyFull,
            [new("items", [new("value", type)])], []);
    internal static KejiSmartQueryDataSource JoinedSource(KejiSmartQueryDialect dialect) =>
        Source(dialect) with
        {
            Tables =
            [
                new("items", [new("value", KejiSmartQueryColumnType.String)]),
                new("orders",
                [
                    new("customer_id", KejiSmartQueryColumnType.String),
                    new("amount", KejiSmartQueryColumnType.Decimal)
                ])
            ],
            ForeignKeys = [new("fk_orders_customer", "items", "value", "orders", "customer_id")]
        };
    internal static KejiSmartQueryPlan Plan(
        string from = "items", ImmutableArray<KejiSmartQueryJoin> joins = default,
        ImmutableArray<KejiSmartQueryProjection> select = default,
        KejiSmartQueryFilterNode? where = null,
        ImmutableArray<KejiSmartQueryProjection> groupBy = default,
        ImmutableArray<KejiSmartQuerySort> orderBy = default, int limit = 10) =>
        new(from, joins.IsDefault ? [] : joins,
            select.IsDefault ? [new(from, "value", "value")] : select, where,
            groupBy.IsDefault ? [] : groupBy, orderBy.IsDefault ? [] : orderBy, limit);

    private static JsonElement Value(KejiSmartQueryColumnType type) => type switch
    {
        KejiSmartQueryColumnType.Integer => JsonSerializer.SerializeToElement(42),
        KejiSmartQueryColumnType.Number => JsonSerializer.SerializeToElement(4.2),
        KejiSmartQueryColumnType.Decimal => JsonSerializer.SerializeToElement(4.2m),
        KejiSmartQueryColumnType.Boolean => JsonSerializer.SerializeToElement(true),
        KejiSmartQueryColumnType.Guid => JsonSerializer.SerializeToElement("d2719f65-9d8f-4fc4-bf00-c868c09cbec4"),
        KejiSmartQueryColumnType.Date or KejiSmartQueryColumnType.DateTime =>
            JsonSerializer.SerializeToElement("2026-07-17T00:00:00Z"),
        _ => JsonSerializer.SerializeToElement("alpha")
    };
}
