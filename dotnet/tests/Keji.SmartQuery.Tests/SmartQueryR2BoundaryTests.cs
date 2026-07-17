using System.Text.Json;
using Keji.SmartQuery;

namespace Keji.SmartQuery.Tests;

public sealed class SmartQueryR2BoundaryTests
{
    public static TheoryData<Type> PublicEnums() => new()
    {
        typeof(KejiSmartQueryStatus), typeof(KejiSmartQueryEventType),
        typeof(KejiSmartQueryDialect), typeof(KejiSmartQueryTlsMode),
        typeof(KejiSmartQueryValueKind), typeof(KejiSmartQueryColumnType),
        typeof(KejiSmartQueryFilterOperator), typeof(KejiSmartQueryLogicalOperator),
        typeof(KejiSmartQuerySortDirection), typeof(KejiSmartQueryJoinType),
        typeof(KejiSmartQueryAggregate), typeof(KejiSmartQueryErrorCode),
        typeof(KejiSmartQueryStopReason), typeof(KejiSmartQueryDataSourceVisibility)
    };

    [Theory]
    [MemberData(nameof(PublicEnums))]
    public void PublicEnumStartsWithExplicitInvalidZero(Type type)
    {
        Assert.Equal("Invalid", Enum.GetName(type, 0));
    }

    public static TheoryData<string> InvalidSecretReferences() => new()
    {
        "", "DATABASE_PASSWORD", "env:", "env:lower", "file:PASSWORD",
        "shell:PASSWORD", "https:PASSWORD", "env:PASS-WORD", "env:1PASSWORD"
    };

    [Theory]
    [MemberData(nameof(InvalidSecretReferences))]
    public void SecretReferenceRejectsEveryNonEnvironmentOrUnsafeForm(string value) =>
        Assert.Throws<ArgumentException>(() => new KejiSmartQuerySecretReference(value));

    [Fact]
    public void PublicContractsDoNotExposeSqlOrObjectParameterValues()
    {
        var publicTypes = typeof(IKejiSmartQuery).Assembly.GetExportedTypes();
        Assert.DoesNotContain(publicTypes, static type => type.Name is "KejiCompiledQuery" or "KejiCompiledParameter");
        Assert.DoesNotContain(publicTypes.SelectMany(static type => type.GetProperties()),
            static property => property.Name == "Sql" || property.PropertyType == typeof(object));
    }

    [Theory]
    [InlineData(KejiSmartQueryDialect.MySql)]
    [InlineData(KejiSmartQueryDialect.PostgreSql)]
    public void BetweenCompilesTwoTypedParameters(KejiSmartQueryDialect dialect)
    {
        var filter = new KejiSmartQueryFilterNode(Filter: new(
            "items", "value", KejiSmartQueryFilterOperator.Between,
            LowerValue: JsonSerializer.SerializeToElement(10),
            UpperValue: JsonSerializer.SerializeToElement(20)));
        Assert.True(SmartQueryCompilerTests.Compiler(dialect).TryCompile(
            SmartQueryCompilerTests.Plan(where: filter),
            SmartQueryCompilerTests.Source(dialect, KejiSmartQueryColumnType.Integer),
            new(), out var query));
        Assert.Contains(" BETWEEN @p0 AND @p1", query!.Sql);
        Assert.Equal([10L, 20L], query.Parameters.Take(2).Select(static p => p.Value));
    }

    [Theory]
    [InlineData("50%_off", "%50!%!_off%")]
    [InlineData("bang!sale", "%bang!!sale%")]
    public void LikeMetacharactersAreEscapedAsLiteralData(string input, string expected)
    {
        var filter = new KejiSmartQueryFilterNode(Filter: new(
            "items", "value", KejiSmartQueryFilterOperator.Contains,
            JsonSerializer.SerializeToElement(input)));
        Assert.True(SmartQueryCompilerTests.Compiler(KejiSmartQueryDialect.PostgreSql).TryCompile(
            SmartQueryCompilerTests.Plan(where: filter),
            SmartQueryCompilerTests.Source(KejiSmartQueryDialect.PostgreSql),
            new(), out var query));
        Assert.Equal(expected, query!.Parameters[0].Value);
        Assert.Contains("ESCAPE '!'", query.Sql);
    }

    [Fact]
    public void GuidBecomesGuidParameter()
    {
        const string text = "d2719f65-9d8f-4fc4-bf00-c868c09cbec4";
        var filter = new KejiSmartQueryFilterNode(Filter: new(
            "items", "value", KejiSmartQueryFilterOperator.Equal,
            JsonSerializer.SerializeToElement(text)));
        Assert.True(SmartQueryCompilerTests.Compiler(KejiSmartQueryDialect.MySql).TryCompile(
            SmartQueryCompilerTests.Plan(where: filter),
            SmartQueryCompilerTests.Source(KejiSmartQueryDialect.MySql, KejiSmartQueryColumnType.Guid),
            new(), out var query));
        Assert.IsType<Guid>(query!.Parameters[0].Value);
    }

    [Theory]
    [InlineData("07/17/2026")]
    [InlineData("2026-7-17")]
    public void NonIsoDateIsRejected(string text)
    {
        var filter = new KejiSmartQueryFilterNode(Filter: new(
            "items", "value", KejiSmartQueryFilterOperator.Equal,
            JsonSerializer.SerializeToElement(text)));
        Assert.False(SmartQueryCompilerTests.Compiler(KejiSmartQueryDialect.PostgreSql).TryCompile(
            SmartQueryCompilerTests.Plan(where: filter),
            SmartQueryCompilerTests.Source(KejiSmartQueryDialect.PostgreSql, KejiSmartQueryColumnType.Date),
            new(), out _));
    }
}
