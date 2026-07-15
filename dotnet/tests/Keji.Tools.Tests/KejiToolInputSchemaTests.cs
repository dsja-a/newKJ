using Keji.Tools.Definitions.Parameters;
using Keji.Tools.Names;

namespace Keji.Tools.Tests;

public class KejiToolInputSchemaTests
{
    [Fact]
    public void Schema_Empty_Succeeds()
    {
        var schema = new KejiToolInputSchema(new List<KejiToolParameterDefinition>());
        Assert.Empty(schema.Parameters);
        Assert.False(schema.HasRequiredParams);
    }

    [Fact]
    public void Schema_SingleParam_Succeeds()
    {
        var schema = new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            new("path", KejiToolParameterType.String, true, "Path.", maxLength: 1024),
        });
        Assert.Single(schema.Parameters);
        Assert.True(schema.HasRequiredParams);
    }

    [Fact]
    public void Schema_DuplicateNames_Throws()
    {
        Assert.Throws<KejiToolContractException>(() => new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            new("path", KejiToolParameterType.String, true, "Path.", maxLength: 1024),
            new("path", KejiToolParameterType.Integer, false, "Count."),
        }));
    }

    [Fact]
    public void Schema_OptionalOnly_NoRequiredParams()
    {
        var schema = new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            new("flag", KejiToolParameterType.Boolean, false, "Flag."),
            new("name", KejiToolParameterType.String, false, "Name.", maxLength: 100),
        });
        Assert.Equal(2, schema.Parameters.Count);
        Assert.False(schema.HasRequiredParams);
    }

    [Fact]
    public void Schema_MultipleParams_AllPresent()
    {
        var schema = new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            new("path", KejiToolParameterType.String, true, "Path.", maxLength: 1024),
            new("recursive", KejiToolParameterType.Boolean, false, "Recursive."),
            new("pattern", KejiToolParameterType.String, true, "Pattern.", maxLength: 256),
        });
        Assert.Equal(3, schema.Parameters.Count);
        Assert.True(schema.HasRequiredParams);
    }

    [Fact]
    public void Schema_NullList_CreatesEmpty()
    {
        var schema = new KejiToolInputSchema(null);
        Assert.NotNull(schema);
        Assert.Empty(schema.Parameters);
        Assert.False(schema.HasRequiredParams);
    }

    [Fact]
    public void Schema_ParameterList_DeepImmutable()
    {
        var list = new List<KejiToolParameterDefinition>
        {
            new("path", KejiToolParameterType.String, true, "Path.", maxLength: 1024),
        };
        var schema = new KejiToolInputSchema(list);
        list.Add(new("extra", KejiToolParameterType.Integer, false, "Extra."));
        Assert.Single(schema.Parameters);
    }

    [Fact]
    public void Schema_EmptySchema_Immutable()
    {
        var schema = new KejiToolInputSchema(null);
        Assert.IsAssignableFrom<KejiToolParameterDefinition[]>(schema.Parameters);
        Assert.Empty(schema.Parameters);
    }

    [Fact]
    public void Schema_PreservesInsertionOrder()
    {
        var schema = new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            new("z_param", KejiToolParameterType.Boolean, false, "Z."),
            new("a_param", KejiToolParameterType.String, false, "A.", maxLength: 100),
            new("m_param", KejiToolParameterType.Integer, false, "M."),
        });
        Assert.Equal("z_param", schema.Parameters[0].Name);
        Assert.Equal("a_param", schema.Parameters[1].Name);
        Assert.Equal("m_param", schema.Parameters[2].Name);
    }
}
