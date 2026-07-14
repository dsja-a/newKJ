using Keji.Security.Authorization;
using Keji.Tools.Definitions;
using Keji.Tools.Definitions.Parameters;
using Keji.Tools.Names;
using Keji.Tools.Registry;

namespace Keji.Tools.Tests;

public class KejiToolRegistryTests
{
    private static KejiToolDefinition MakeDef(string name) => new(
        KejiToolName.Create(name), 1, $"Tool {name}.",
        KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
        KejiPermission.ToolCatalogRead);

    [Fact]
    public void Builder_RegisterAndBuild_Succeeds()
    {
        var builder = new KejiToolRegistryBuilder();
        builder.Register(MakeDef("tool_a"));
        builder.Register(MakeDef("tool_b"));
        var registry = builder.Build();

        Assert.NotNull(registry);
        Assert.True(registry.Contains(KejiToolName.Create("tool_a")));
        Assert.True(registry.Contains(KejiToolName.Create("tool_b")));
    }

    [Fact]
    public void Builder_DoubleBuild_Throws()
    {
        var builder = new KejiToolRegistryBuilder();
        builder.Register(MakeDef("tool_a"));
        builder.Build();
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Builder_DuplicateName_Throws()
    {
        var builder = new KejiToolRegistryBuilder();
        builder.Register(MakeDef("tool_a"));
        Assert.Throws<KejiToolContractException>(() => builder.Register(MakeDef("tool_a")));
    }

    [Fact]
    public void Builder_CaseSensitiveDuplicate_Succeeds()
    {
        var builder = new KejiToolRegistryBuilder();
        builder.Register(MakeDef("tool_a"));
        builder.Register(MakeDef("tool_a_b"));
        var registry = builder.Build();
        Assert.True(registry.Contains(KejiToolName.Create("tool_a")));
        Assert.True(registry.Contains(KejiToolName.Create("tool_a_b")));
    }

    [Fact]
    public void Builder_RegisterAfterFreeze_Throws()
    {
        var builder = new KejiToolRegistryBuilder();
        builder.Register(MakeDef("tool_a"));
        var registry = builder.Build();
        Assert.Throws<InvalidOperationException>(() => builder.Register(MakeDef("tool_b")));
    }

    [Fact]
    public void Registry_Contains_CaseSensitive()
    {
        var builder = new KejiToolRegistryBuilder();
        builder.Register(MakeDef("test_tool"));
        var registry = builder.Build();
        Assert.True(registry.Contains(KejiToolName.Create("test_tool")));
        Assert.False(registry.Contains(KejiToolName.Create("test_tool_b")));
    }

    [Fact]
    public void Registry_Contains_InvalidName_ReturnsFalse()
    {
        var builder = new KejiToolRegistryBuilder();
        var registry = builder.Build();
        Assert.False(registry.Contains(KejiToolName.Create("nonexistent")));
    }

    [Fact]
    public void Registry_Resolve_Found()
    {
        var builder = new KejiToolRegistryBuilder();
        var def = MakeDef("my_tool");
        builder.Register(def);
        var registry = builder.Build();
        var result = registry.Resolve(KejiToolName.Create("my_tool"));
        Assert.Equal(KejiToolResolutionStatus.Found, result.Status);
        Assert.NotNull(result.Definition);
        Assert.Same(def, result.Definition);
    }

    [Fact]
    public void Registry_Resolve_NotRegistered()
    {
        var builder = new KejiToolRegistryBuilder();
        var registry = builder.Build();
        var result = registry.Resolve(KejiToolName.Create("nonexistent_tool"));
        Assert.Equal(KejiToolResolutionStatus.NotRegistered, result.Status);
        Assert.Null(result.Definition);
    }

    [Fact]
    public void Registry_GetAll_Ordered()
    {
        var builder = new KejiToolRegistryBuilder();
        builder.Register(MakeDef("z_tool"));
        builder.Register(MakeDef("a_tool"));
        builder.Register(MakeDef("m_tool"));
        var registry = builder.Build();
        var all = registry.GetAll();
        Assert.Equal(3, all.Count);
        Assert.Equal("a_tool", all[0].Name.Value);
        Assert.Equal("m_tool", all[1].Name.Value);
        Assert.Equal("z_tool", all[2].Name.Value);
    }

    [Fact]
    public void Registry_Empty_HasNoTools()
    {
        var builder = new KejiToolRegistryBuilder();
        var registry = builder.Build();
        Assert.Empty(registry.GetAll());
        Assert.False(registry.Contains(KejiToolName.Create("anything")));
        var result = registry.Resolve(KejiToolName.Create("anything"));
        Assert.Equal(KejiToolResolutionStatus.NotRegistered, result.Status);
    }

    [Fact]
    public void Resolution_Found_CarriesDefinition()
    {
        var def = MakeDef("found_tool");
        var res = KejiToolResolution.Found(def);
        Assert.Equal(KejiToolResolutionStatus.Found, res.Status);
        Assert.Same(def, res.Definition);
        Assert.Equal(def.Name, res.Name);
    }

    [Fact]
    public void Resolution_InvalidName_CarriesNullDefinition()
    {
        var name = KejiToolName.Create("bad_name");
        var res = KejiToolResolution.InvalidName(name);
        Assert.Equal(KejiToolResolutionStatus.InvalidName, res.Status);
        Assert.Null(res.Definition);
        Assert.Equal(name, res.Name);
    }

    [Fact]
    public void Resolution_NotRegistered_CarriesNullDefinition()
    {
        var name = KejiToolName.Create("missing_tool");
        var res = KejiToolResolution.NotRegistered(name);
        Assert.Equal(KejiToolResolutionStatus.NotRegistered, res.Status);
        Assert.Null(res.Definition);
        Assert.Equal(name, res.Name);
    }

    [Fact]
    public async Task FrozenRegistry_ConcurrentRead_Succeeds()
    {
        var builder = new KejiToolRegistryBuilder();
        var def = MakeDef("concurrent_tool");
        builder.Register(def);
        var registry = builder.Build();

        var tasks = new Task<bool>[10];
        for (int i = 0; i < 10; i++)
        {
            tasks[i] = Task.Run(() => registry.Contains(KejiToolName.Create("concurrent_tool")));
        }
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.True(r));
    }
}
