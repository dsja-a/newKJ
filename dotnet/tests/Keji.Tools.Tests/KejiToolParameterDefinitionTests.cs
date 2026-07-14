using Keji.Tools.Definitions.Parameters;
using Keji.Tools.Names;

namespace Keji.Tools.Tests;

public class KejiToolParameterDefinitionTests
{
    [Fact]
    public void Parameter_AllProperties_Set()
    {
        var p = new KejiToolParameterDefinition("path", KejiToolParameterType.String, true, "File path.");
        Assert.Equal("path", p.Name);
        Assert.Equal(KejiToolParameterType.String, p.Type);
        Assert.True(p.Required);
        Assert.Equal("File path.", p.Description);
        Assert.Null(p.DefaultValue);
        Assert.Null(p.Minimum);
        Assert.Null(p.Maximum);
        Assert.Null(p.MaxLength);
        Assert.Null(p.MinLength);
        Assert.Null(p.MaxItems);
        Assert.Null(p.AllowedValues);
        Assert.False(p.Sensitive);
    }

    [Fact]
    public void Parameter_Optional_AllowedValues()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "csv", "json", "xml" };
        var p = new KejiToolParameterDefinition("format", KejiToolParameterType.String, false, "Format.",
            allowedValues: allowed);
        Assert.False(p.Required);
        Assert.NotNull(p.AllowedValues);
        Assert.Equal(3, p.AllowedValues.Count);
    }

    [Fact]
    public void Parameter_NullName_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition(null!, KejiToolParameterType.String, true, "desc"));
    }

    [Fact]
    public void Parameter_EmptyName_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("", KejiToolParameterType.String, true, "desc"));
    }

    [Fact]
    public void Parameter_NameInvalidChars_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("Invalid_Name", KejiToolParameterType.String, true, "desc"));
    }

    [Fact]
    public void Parameter_NameTooLong_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition(new string('x', 65), KejiToolParameterType.String, true, "desc"));
    }

    [Fact]
    public void Parameter_NullDescription_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("param", KejiToolParameterType.String, true, null!));
    }

    [Fact]
    public void Parameter_EmptyDescription_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("param", KejiToolParameterType.String, true, ""));
    }

    [Fact]
    public void Parameter_DefaultValueTypeMismatch_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("count", KejiToolParameterType.Integer, false, "Count.",
                defaultValue: "not_an_integer"));
    }

    [Fact]
    public void Parameter_DefaultValueString_Succeeds()
    {
        var p = new KejiToolParameterDefinition("name", KejiToolParameterType.String, false, "Name.",
            defaultValue: "default_name");
        Assert.Equal("default_name", p.DefaultValue);
    }

    [Fact]
    public void Parameter_DefaultValueInteger_Succeeds()
    {
        var p = new KejiToolParameterDefinition("count", KejiToolParameterType.Integer, false, "Count.",
            defaultValue: 42);
        Assert.Equal(42, p.DefaultValue);
    }

    [Fact]
    public void Parameter_DefaultValueNumber_Succeeds()
    {
        var p = new KejiToolParameterDefinition("ratio", KejiToolParameterType.Number, false, "Ratio.",
            defaultValue: 3.14);
        Assert.Equal(3.14, p.DefaultValue);
    }

    [Fact]
    public void Parameter_DefaultValueBoolean_Succeeds()
    {
        var p = new KejiToolParameterDefinition("flag", KejiToolParameterType.Boolean, false, "Flag.",
            defaultValue: true);
        Assert.Equal(true, p.DefaultValue);
    }

    [Fact]
    public void Parameter_ArrayTypeRequiresMaxItems_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("items", KejiToolParameterType.StringArray, false, "Items."));
    }

    [Fact]
    public void Parameter_ArrayTypeWithMaxItems_Succeeds()
    {
        var p = new KejiToolParameterDefinition("items", KejiToolParameterType.StringArray, false, "Items.",
            maxItems: 10);
        Assert.Equal(10, p.MaxItems);
    }

    [Fact]
    public void Parameter_IntegerArrayWithMaxItems_Succeeds()
    {
        var p = new KejiToolParameterDefinition("ids", KejiToolParameterType.IntegerArray, false, "IDs.",
            maxItems: 100);
        Assert.Equal(100, p.MaxItems);
    }

    [Fact]
    public void Parameter_IntegerConstraints_Succeeds()
    {
        var p = new KejiToolParameterDefinition("count", KejiToolParameterType.Integer, false, "Count.",
            minimum: 1, maximum: 100);
        Assert.Equal(1, p.Minimum);
        Assert.Equal(100, p.Maximum);
    }

    [Fact]
    public void Parameter_StringConstraints_Succeeds()
    {
        var p = new KejiToolParameterDefinition("name", KejiToolParameterType.String, false, "Name.",
            minLength: 1, maxLength: 100);
        Assert.Equal(1, p.MinLength);
        Assert.Equal(100, p.MaxLength);
    }

    [Fact]
    public void Parameter_Sensitive_True()
    {
        var p = new KejiToolParameterDefinition("password", KejiToolParameterType.String, true, "Password.",
            sensitive: true);
        Assert.True(p.Sensitive);
    }

    [Fact]
    public void Parameter_Sensitive_DefaultFalse()
    {
        var p = new KejiToolParameterDefinition("name", KejiToolParameterType.String, true, "Name.");
        Assert.False(p.Sensitive);
    }

    [Fact]
    public void Parameter_AllowedValues_Deduplicated()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "z", "a", "a" };
        var p = new KejiToolParameterDefinition("mode", KejiToolParameterType.String, false, "Mode.",
            allowedValues: allowed);
        Assert.Equal(2, p.AllowedValues!.Count);
        Assert.Contains("a", p.AllowedValues);
        Assert.Contains("z", p.AllowedValues);
    }

    [Fact]
    public void Parameter_IntegerMinMax_ValidConstraints()
    {
        var p = new KejiToolParameterDefinition("count", KejiToolParameterType.Integer, false, "Count.",
            minimum: 0, maximum: 100);
        Assert.Equal(0, p.Minimum);
        Assert.Equal(100, p.Maximum);
    }

    [Fact]
    public void Parameter_MinGreaterThanMax_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("count", KejiToolParameterType.Integer, false, "Count.",
                minimum: 10, maximum: 5));
    }

    [Fact]
    public void Parameter_MaxLengthZero_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("name", KejiToolParameterType.String, false, "Name.",
                maxLength: 0));
    }

    [Fact]
    public void Parameter_EmptyAllowedValues_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("mode", KejiToolParameterType.String, false, "Mode.",
                allowedValues: new HashSet<string>()));
    }
}
