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
            new("path", KejiToolParameterType.String, true, "Path."),
        });
        Assert.Single(schema.Parameters);
        Assert.True(schema.HasRequiredParams);
    }

    [Fact]
    public void Schema_DuplicateNames_Throws()
    {
        Assert.Throws<KejiToolContractException>(() => new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            new("path", KejiToolParameterType.String, true, "Path."),
            new("path", KejiToolParameterType.Integer, false, "Count."),
        }));
    }

    [Fact]
    public void Schema_OptionalOnly_NoRequiredParams()
    {
        var schema = new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            new("flag", KejiToolParameterType.Boolean, false, "Flag."),
            new("name", KejiToolParameterType.String, false, "Name."),
        });
        Assert.Equal(2, schema.Parameters.Count);
        Assert.False(schema.HasRequiredParams);
    }

    [Fact]
    public void Schema_MultipleParams_AllPresent()
    {
        var schema = new KejiToolInputSchema(new List<KejiToolParameterDefinition>
        {
            new("path", KejiToolParameterType.String, true, "Path."),
            new("recursive", KejiToolParameterType.Boolean, false, "Recursive."),
            new("pattern", KejiToolParameterType.String, true, "Pattern."),
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
}
