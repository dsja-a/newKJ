using Keji.Security.Authorization;
using Keji.Tools.Definitions;
using Keji.Tools.Definitions.Parameters;
using Keji.Tools.Names;

namespace Keji.Tools.Tests;

public class KejiToolDefinitionTests
{
    [Fact]
    public void Definition_AllProperties_Set()
    {
        var def = new KejiToolDefinition(
            KejiToolName.Create("test_tool"), 1, "A test tool.",
            KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
            KejiPermission.ToolCatalogRead);

        Assert.Equal("test_tool", def.Name.Value);
        Assert.Equal(1, def.ContractVersion);
        Assert.Equal("A test tool.", def.Description);
        Assert.Equal(KejiToolCategory.Utility, def.Category);
        Assert.Equal(KejiToolRiskLevel.ReadOnly, def.RiskLevel);
        Assert.Equal(KejiToolExecutionTarget.Host, def.ExecutionTarget);
        Assert.Equal(KejiPermission.ToolCatalogRead, def.RequiredPermission);
        Assert.Equal(KejiToolAvailability.ContractOnly, def.Availability);
        Assert.NotNull(def.Tags);
        Assert.Empty(def.Tags);
        Assert.NotNull(def.InputSchema);
        Assert.False(def.IsDeterministic);
        Assert.False(def.SupportsCancellation);
    }

    [Fact]
    public void Definition_WithAvailability_SetsCorrectly()
    {
        var def = new KejiToolDefinition(
            KejiToolName.Create("test_tool"), 1, "desc",
            KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
            KejiPermission.ToolCatalogRead,
            availability: KejiToolAvailability.ExecutionPending);

        Assert.Equal(KejiToolAvailability.ExecutionPending, def.Availability);
    }

    [Fact]
    public void Definition_WithTags_SetsCorrectly()
    {
        var tags = new HashSet<string> { "tag1", "tag2" };
        var def = new KejiToolDefinition(
            KejiToolName.Create("tagged_tool"), 1, "desc",
            KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
            KejiPermission.ToolCatalogRead,
            tags: tags);

        Assert.Equal(2, def.Tags.Count);
        Assert.Contains("tag1", def.Tags);
        Assert.Contains("tag2", def.Tags);
    }

    [Fact]
    public void Definition_WithInputSchema_SetsCorrectly()
    {
        var schema = new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            new("path", KejiToolParameterType.String, true, "File path."),
        });
        var def = new KejiToolDefinition(
            KejiToolName.Create("tool"), 1, "desc",
            KejiToolCategory.FileRead, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
            KejiPermission.FileRead,
            inputSchema: schema);

        Assert.NotNull(def.InputSchema);
        Assert.Single(def.InputSchema.Parameters);
    }

    [Fact]
    public void Definition_ContractVersionZero_Throws()
    {
        Assert.Throws<KejiToolContractException>(() => new KejiToolDefinition(
            KejiToolName.Create("tool"), 0, "desc",
            KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
            KejiPermission.FileRead));
    }

    [Fact]
    public void Definition_NullDescription_Throws()
    {
        Assert.Throws<KejiToolContractException>(() => new KejiToolDefinition(
            KejiToolName.Create("tool"), 1, null!,
            KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
            KejiPermission.FileRead));
    }

    [Fact]
    public void Definition_EmptyDescription_Throws()
    {
        Assert.Throws<KejiToolContractException>(() => new KejiToolDefinition(
            KejiToolName.Create("tool"), 1, "",
            KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
            KejiPermission.FileRead));
    }

    [Fact]
    public void Definition_DescriptionTooLong_Throws()
    {
        Assert.Throws<KejiToolContractException>(() => new KejiToolDefinition(
            KejiToolName.Create("tool"), 1, new string('x', 2001),
            KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
            KejiPermission.FileRead));
    }

    [Fact]
    public void Definition_DescriptionAtBoundary_Succeeds()
    {
        var def = new KejiToolDefinition(
            KejiToolName.Create("tool"), 1, new string('x', 2000),
            KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
            KejiPermission.FileRead);
        Assert.Equal(2000, def.Description.Length);
    }

    [Fact]
    public void Definition_InvalidPermission_Throws()
    {
        Assert.Throws<KejiToolContractException>(() => new KejiToolDefinition(
            KejiToolName.Create("tool"), 1, "desc",
            KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
            (KejiPermission)999));
    }

    [Fact]
    public void Definition_InvalidCategory_Throws()
    {
        Assert.Throws<KejiToolContractException>(() => new KejiToolDefinition(
            KejiToolName.Create("tool"), 1, "desc",
            (KejiToolCategory)0, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
            KejiPermission.FileRead));
    }

    [Fact]
    public void Definition_InvalidRiskLevel_Throws()
    {
        Assert.Throws<KejiToolContractException>(() => new KejiToolDefinition(
            KejiToolName.Create("tool"), 1, "desc",
            KejiToolCategory.Utility, (KejiToolRiskLevel)0, KejiToolExecutionTarget.Host,
            KejiPermission.FileRead));
    }

    [Fact]
    public void Definition_InvalidExecutionTarget_Throws()
    {
        Assert.Throws<KejiToolContractException>(() => new KejiToolDefinition(
            KejiToolName.Create("tool"), 1, "desc",
            KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, (KejiToolExecutionTarget)0,
            KejiPermission.FileRead));
    }

    [Fact]
    public void Definition_TooManyTags_Throws()
    {
        var tags = new HashSet<string>(Enumerable.Range(0, 17).Select(i => $"tag{i}"));
        Assert.Throws<KejiToolContractException>(() => new KejiToolDefinition(
            KejiToolName.Create("tool"), 1, "desc",
            KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
            KejiPermission.FileRead,
            tags: tags));
    }

    [Fact]
    public void Definition_TagTooLong_Throws()
    {
        var tags = new HashSet<string> { new string('x', 33) };
        Assert.Throws<KejiToolContractException>(() => new KejiToolDefinition(
            KejiToolName.Create("tool"), 1, "desc",
            KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
            KejiPermission.FileRead,
            tags: tags));
    }

    [Fact]
    public void Definition_EmptyTag_Throws()
    {
        var tags = new HashSet<string> { "" };
        Assert.Throws<KejiToolContractException>(() => new KejiToolDefinition(
            KejiToolName.Create("tool"), 1, "desc",
            KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
            KejiPermission.FileRead,
            tags: tags));
    }

    [Fact]
    public void Definition_ValidTagsAtBoundaries_Succeeds()
    {
        var tags = new HashSet<string> { new string('x', 32), "a" };
        var def = new KejiToolDefinition(
            KejiToolName.Create("tool"), 1, "desc",
            KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
            KejiPermission.FileRead,
            tags: tags);
        Assert.Equal(2, def.Tags.Count);
    }

    [Fact]
    public void Definition_IsDeterministic_DefaultFalse()
    {
        var def = new KejiToolDefinition(
            KejiToolName.Create("tool"), 1, "desc",
            KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
            KejiPermission.FileRead);
        Assert.False(def.IsDeterministic);
    }

    [Fact]
    public void Definition_SupportsCancellation_DefaultFalse()
    {
        var def = new KejiToolDefinition(
            KejiToolName.Create("tool"), 1, "desc",
            KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
            KejiPermission.FileRead);
        Assert.False(def.SupportsCancellation);
    }

    [Fact]
    public void Definition_InputSchema_NullDefaultsToEmpty()
    {
        var def = new KejiToolDefinition(
            KejiToolName.Create("tool"), 1, "desc",
            KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
            KejiPermission.FileRead);
        Assert.NotNull(def.InputSchema);
        Assert.Empty(def.InputSchema.Parameters);
    }

    [Fact]
    public void Definition_AllCategories_AreDefined()
    {
        var idx = 0;
        foreach (KejiToolCategory cat in Enum.GetValues<KejiToolCategory>())
        {
            idx++;
            var def = new KejiToolDefinition(
                KejiToolName.Create($"cat_{idx}"), 1, $"Category test for {cat}",
                cat, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
                KejiPermission.FileRead);
            Assert.Equal(cat, def.Category);
        }
    }

    [Fact]
    public void Definition_AllRiskLevels_AreDefined()
    {
        var idx = 0;
        foreach (KejiToolRiskLevel rl in Enum.GetValues<KejiToolRiskLevel>())
        {
            idx++;
            var def = new KejiToolDefinition(
                KejiToolName.Create($"rl_{idx}"), 1, $"Risk test for {rl}",
                KejiToolCategory.Utility, rl, KejiToolExecutionTarget.Host,
                KejiPermission.FileRead);
            Assert.Equal(rl, def.RiskLevel);
        }
    }

    [Fact]
    public void Definition_AllExecutionTargets_AreDefined()
    {
        var idx = 0;
        foreach (KejiToolExecutionTarget et in Enum.GetValues<KejiToolExecutionTarget>())
        {
            idx++;
            var def = new KejiToolDefinition(
                KejiToolName.Create($"et_{idx}"), 1, $"Exec test for {et}",
                KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, et,
                KejiPermission.FileRead);
            Assert.Equal(et, def.ExecutionTarget);
        }
    }

    [Fact]
    public void Definition_AllAvailabilityValues_AreDefined()
    {
        var idx = 0;
        foreach (KejiToolAvailability a in Enum.GetValues<KejiToolAvailability>())
        {
            idx++;
            var def = new KejiToolDefinition(
                KejiToolName.Create($"av_{idx}"), 1, $"Avail test for {a}",
                KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
                KejiPermission.FileRead,
                availability: a);
            Assert.Equal(a, def.Availability);
        }
    }
}
