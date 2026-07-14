using Keji.Tools.Catalog;
using Keji.Tools.Names;

namespace Keji.Tools.Tests;

public class BuiltInToolCatalogTests
{
    [Fact]
    public void Catalog_AllTools_HaveValidNames()
    {
        foreach (var def in BuiltInToolCatalog.All)
        {
            Assert.NotNull(def.Name);
            Assert.Matches("^[a-z][a-z0-9_]{0,63}$", def.Name.Value);
        }
    }

    [Fact]
    public void Catalog_AllTools_HaveNonEmptyDescription()
    {
        foreach (var def in BuiltInToolCatalog.All)
        {
            Assert.False(string.IsNullOrEmpty(def.Description));
        }
    }

    [Fact]
    public void Catalog_NoDuplicateNames()
    {
        var names = BuiltInToolCatalog.All.Select(d => d.Name.Value).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Catalog_AllTools_HaveValidPermission()
    {
        foreach (var def in BuiltInToolCatalog.All)
        {
            var perm = def.RequiredPermission;
            Assert.True(Enum.IsDefined(perm), $"Tool {def.Name} has undefined permission {(int)perm}");
        }
    }

    [Fact]
    public void Catalog_AllTools_HaveValidCategory()
    {
        foreach (var def in BuiltInToolCatalog.All)
        {
            Assert.True(Enum.IsDefined(def.Category), $"Tool {def.Name} has undefined category {(int)def.Category}");
        }
    }

    [Fact]
    public void Catalog_AllTools_HaveValidRiskLevel()
    {
        foreach (var def in BuiltInToolCatalog.All)
        {
            Assert.True(Enum.IsDefined(def.RiskLevel), $"Tool {def.Name} has undefined risk level {(int)def.RiskLevel}");
        }
    }

    [Fact]
    public void Catalog_AllTools_HaveValidExecutionTarget()
    {
        foreach (var def in BuiltInToolCatalog.All)
        {
            Assert.True(Enum.IsDefined(def.ExecutionTarget), $"Tool {def.Name} has undefined execution target {(int)def.ExecutionTarget}");
        }
    }

    [Fact]
    public void Catalog_AllTools_DescriptionUnder2000()
    {
        foreach (var def in BuiltInToolCatalog.All)
        {
            Assert.True(def.Description.Length <= 2000, $"Tool {def.Name} description exceeds 2000 chars");
        }
    }

    [Fact]
    public void Catalog_AllTools_TagsUnderLimit()
    {
        foreach (var def in BuiltInToolCatalog.All)
        {
            Assert.True(def.Tags.Count <= 16, $"Tool {def.Name} has {def.Tags.Count} tags (max 16)");
            foreach (var tag in def.Tags)
                Assert.True(tag.Length <= 32, $"Tool {def.Name} has tag '{tag}' with length {tag.Length} (max 32)");
        }
    }

    [Fact]
    public void Catalog_AllTools_ValidContractVersion()
    {
        foreach (var def in BuiltInToolCatalog.All)
        {
            Assert.True(def.ContractVersion >= 1, $"Tool {def.Name} has version {def.ContractVersion} (min 1)");
        }
    }

    [Fact]
    public void Catalog_AllTools_HaveValidAvailability()
    {
        foreach (var def in BuiltInToolCatalog.All)
        {
            Assert.True(Enum.IsDefined(def.Availability), $"Tool {def.Name} has undefined availability {(int)def.Availability}");
        }
    }

    [Fact]
    public void Catalog_ReadFile_HasRequiredParams()
    {
        var def = BuiltInToolCatalog.All.Single(d => d.Name.Value == "read_file");
        Assert.NotNull(def.InputSchema);
        Assert.True(def.InputSchema!.HasRequiredParams);
        Assert.Contains(def.InputSchema.Parameters, p => p.Name == "path");
    }

    [Fact]
    public void Catalog_WriteFile_HasContentParam()
    {
        var def = BuiltInToolCatalog.All.Single(d => d.Name.Value == "write_file");
        Assert.NotNull(def.InputSchema);
        Assert.Contains(def.InputSchema!.Parameters, p => p.Name == "content");
    }

    [Fact]
    public void Catalog_DatabaseTools_RequireDatabaseManage()
    {
        var def = BuiltInToolCatalog.All.Single(d => d.Name.Value == "db_connect");
        Assert.Equal(Keji.Security.Authorization.KejiPermission.DatabaseManage, def.RequiredPermission);
    }

    [Fact]
    public void Catalog_NetworkTools_HaveExternalSideEffectRisk()
    {
        var webSearch = BuiltInToolCatalog.All.Single(d => d.Name.Value == "web_search");
        Assert.Equal(Definitions.KejiToolRiskLevel.ExternalSideEffect, webSearch.RiskLevel);

        var webFetch = BuiltInToolCatalog.All.Single(d => d.Name.Value == "web_fetch");
        Assert.Equal(Definitions.KejiToolRiskLevel.ExternalSideEffect, webFetch.RiskLevel);
    }

    [Fact]
    public void Catalog_CreateBuilder_ProducesValidRegistry()
    {
        var builder = BuiltInToolCatalog.CreateBuilder();
        var registry = builder.Build();
        Assert.NotNull(registry);
        Assert.Equal(BuiltInToolCatalog.All.Count, registry.GetAll().Count);
    }

    [Fact]
    public void Catalog_AllTools_CaseSensitiveNames()
    {
        foreach (var def in BuiltInToolCatalog.All)
        {
            Assert.Equal(def.Name.Value, def.Name.Value.ToLowerInvariant());
            Assert.False(def.Name.Value.Any(char.IsUpper), $"Tool {def.Name} contains uppercase characters");
        }
    }

    [Theory]
    [MemberData(nameof(ExpectedTools))]
    public void Catalog_ContainsExpectedTool(string name)
    {
        Assert.Contains(BuiltInToolCatalog.All, d => d.Name.Value == name);
    }

    public static IEnumerable<object[]> ExpectedTools()
    {
        yield return new[] { "read_file" };
        yield return new[] { "write_file" };
        yield return new[] { "edit_file" };
        yield return new[] { "glob" };
        yield return new[] { "grep" };
        yield return new[] { "web_search" };
        yield return new[] { "web_fetch" };
        yield return new[] { "db_connect" };
        yield return new[] { "get_time" };
        yield return new[] { "calculator" };
        yield return new[] { "query_knowledge" };
    }
}
