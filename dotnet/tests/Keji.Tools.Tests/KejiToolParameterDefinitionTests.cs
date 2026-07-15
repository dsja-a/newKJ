using Keji.Tools.Definitions.Parameters;
using Keji.Tools.Names;
using System.Collections.Immutable;

namespace Keji.Tools.Tests;

public class KejiToolParameterDefinitionTests
{
    [Fact]
    public void Parameter_AllProperties_Set()
    {
        var p = new KejiToolParameterDefinition("path", KejiToolParameterType.String, true, "File path.", maxLength: 1024);
        Assert.Equal("path", p.Name);
        Assert.Equal(KejiToolParameterType.String, p.Type);
        Assert.True(p.Required);
        Assert.Equal("File path.", p.Description);
        Assert.Null(p.DefaultValue);
        Assert.Null(p.Minimum);
        Assert.Null(p.Maximum);
        Assert.Equal(1024, p.MaxLength);
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
            allowedValues: allowed, maxLength: 10);
        Assert.False(p.Required);
        Assert.NotNull(p.AllowedValues);
        Assert.Equal(3, p.AllowedValues.Count);
    }

    [Fact]
    public void Parameter_NullName_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition(null!, KejiToolParameterType.String, true, "desc", maxLength: 100));
    }

    [Fact]
    public void Parameter_EmptyName_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("", KejiToolParameterType.String, true, "desc", maxLength: 100));
    }

    [Fact]
    public void Parameter_NameInvalidChars_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("Invalid_Name", KejiToolParameterType.String, true, "desc", maxLength: 100));
    }

    [Fact]
    public void Parameter_NameTooLong_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition(new string('x', 65), KejiToolParameterType.String, true, "desc", maxLength: 100));
    }

    [Fact]
    public void Parameter_NullDescription_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("param", KejiToolParameterType.String, true, null!, maxLength: 100));
    }

    [Fact]
    public void Parameter_EmptyDescription_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("param", KejiToolParameterType.String, true, "", maxLength: 100));
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
            defaultValue: "default_name", maxLength: 100);
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
            maxItems: 10, maxItemLength: 100);
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
            sensitive: true, maxLength: 512);
        Assert.True(p.Sensitive);
    }

    [Fact]
    public void Parameter_Sensitive_DefaultFalse()
    {
        var p = new KejiToolParameterDefinition("name", KejiToolParameterType.String, true, "Name.", maxLength: 100);
        Assert.False(p.Sensitive);
    }

    [Fact]
    public void Parameter_AllowedValues_Deduplicated()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "z", "a", "a" };
        var p = new KejiToolParameterDefinition("mode", KejiToolParameterType.String, false, "Mode.",
            allowedValues: allowed, maxLength: 10);
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
                allowedValues: new HashSet<string>(), maxLength: 100));
    }

    [Fact]
    public void Parameter_InvalidEnumType_Zero_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", (KejiToolParameterType)0, false, "desc", maxLength: 100));
    }

    [Fact]
    public void Parameter_InvalidEnumType_Negative_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", (KejiToolParameterType)(-1), false, "desc", maxLength: 100));
    }

    [Fact]
    public void Parameter_InvalidEnumType_OutOfRange_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", (KejiToolParameterType)999, false, "desc", maxLength: 100));
    }

    [Fact]
    public void Parameter_AllDefinedTypes_Pass()
    {
        foreach (KejiToolParameterType t in Enum.GetValues<KejiToolParameterType>())
        {
            if (t == KejiToolParameterType.String)
            {
                var p = new KejiToolParameterDefinition("x", t, false, "desc", maxLength: 100);
                Assert.Equal(t, p.Type);
            }
            else if (t == KejiToolParameterType.StringArray)
            {
                var p = new KejiToolParameterDefinition("x", t, false, "desc", maxItems: 10, maxItemLength: 100);
                Assert.Equal(t, p.Type);
            }
            else if (t == KejiToolParameterType.IntegerArray)
            {
                var p = new KejiToolParameterDefinition("x", t, false, "desc", maxItems: 10);
                Assert.Equal(t, p.Type);
            }
            else
            {
                var p = new KejiToolParameterDefinition("x", t, false, "desc");
                Assert.Equal(t, p.Type);
            }
        }
    }

    [Fact]
    public void Parameter_StringWithoutMaxLength_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.String, false, "desc"));
    }

    [Fact]
    public void Parameter_StringWithMinMaxConstraints_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.String, false, "desc",
                maxLength: 100, minimum: 1));
    }

    [Fact]
    public void Parameter_IntegerWithStringConstraints_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.Integer, false, "desc",
                maxLength: 100));
    }

    [Fact]
    public void Parameter_IntegerWithMaxItems_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.Integer, false, "desc",
                maxItems: 10));
    }

    [Fact]
    public void Parameter_IntegerWithAllowedValues_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.Integer, false, "desc",
                allowedValues: new HashSet<string> { "a" }));
    }

    [Fact]
    public void Parameter_NumberWithStringConstraints_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.Number, false, "desc",
                maxLength: 100));
    }

    [Fact]
    public void Parameter_NumberWithAllowedValues_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.Number, false, "desc",
                allowedValues: new HashSet<string> { "a" }));
    }

    [Fact]
    public void Parameter_BooleanWithAnyConstraints_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.Boolean, false, "desc",
                minimum: 1));
    }

    [Fact]
    public void Parameter_BooleanWithAllowedValues_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.Boolean, false, "desc",
                allowedValues: new HashSet<string> { "a" }));
    }

    [Fact]
    public void Parameter_StringArrayWithScalarConstraints_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.StringArray, false, "desc",
                maxItems: 10, minimum: 1));
    }

    [Fact]
    public void Parameter_StringArrayWithAllowedValues_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.StringArray, false, "desc",
                maxItems: 10, allowedValues: new HashSet<string> { "a" }));
    }

    [Fact]
    public void Parameter_StringArrayWithoutMaxItems_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.StringArray, false, "desc"));
    }

    [Fact]
    public void Parameter_IntegerArrayWithScalarConstraints_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.IntegerArray, false, "desc",
                maxItems: 10, minLength: 1));
    }

    [Fact]
    public void Parameter_IntegerArrayWithAllowedValues_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.IntegerArray, false, "desc",
                maxItems: 10, allowedValues: new HashSet<string> { "a" }));
    }

    [Fact]
    public void Parameter_IntegerArrayWithoutMaxItems_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.IntegerArray, false, "desc"));
    }

    [Fact]
    public void Parameter_DefaultValueStringArray_DeepImmutable()
    {
        var original = new List<string> { "a", "b" };
        var p = new KejiToolParameterDefinition("items", KejiToolParameterType.StringArray, false, "Items.",
            defaultValue: original, maxItems: 10, maxItemLength: 100);
        original.Add("c");
        var dv = (ImmutableArray<string>)p.DefaultValue!;
        Assert.Equal(2, dv.Length);
        Assert.Equal("a", dv[0]);
        Assert.Equal("b", dv[1]);
    }

    [Fact]
    public void Parameter_DefaultValueIntegerArray_DeepImmutable()
    {
        var original = new List<int> { 1, 2 };
        var p = new KejiToolParameterDefinition("ids", KejiToolParameterType.IntegerArray, false, "IDs.",
            defaultValue: original, maxItems: 10);
        original.Add(3);
        var dv = (ImmutableArray<long>)p.DefaultValue!;
        Assert.Equal(2, dv.Length);
    }

    [Fact]
    public void Parameter_DescriptionMaxLength_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.String, false,
                new string('x', 2001), maxLength: 100));
    }

    [Fact]
    public void Parameter_NegativeMinLength_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.String, false, "desc",
                maxLength: 100, minLength: -1));
    }

    [Fact]
    public void Parameter_StringArrayWithoutMaxItemLength_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.StringArray, false, "desc",
                maxItems: 10));
    }

    [Fact]
    public void Parameter_DefaultValueStringExceedsMaxLength_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.String, false, "desc",
                defaultValue: "toolong", maxLength: 3));
    }

    [Fact]
    public void Parameter_DefaultValueIntegerOutOfRange_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.Integer, false, "desc",
                defaultValue: 200, maximum: 100));
    }

    [Fact]
    public void Parameter_DefaultValueNumberNaN_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.Number, false, "desc",
                defaultValue: double.NaN));
    }

    [Fact]
    public void Parameter_DefaultValueNumberInfinity_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.Number, false, "desc",
                defaultValue: double.PositiveInfinity));
    }

    [Fact]
    public void Parameter_DefaultValueArrayExceedsMaxItems_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.StringArray, false, "desc",
                defaultValue: new List<string> { "a", "b", "c" }, maxItems: 2, maxItemLength: 100));
    }

    [Fact]
    public void Parameter_DefaultValueStringArrayItemExceedsMaxItemLength_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition("x", KejiToolParameterType.StringArray, false, "desc",
                defaultValue: new List<string> { "a", "toolong" }, maxItems: 10, maxItemLength: 3));
    }

    [Fact]
    public void Parameter_DefaultValueStringArray_ImmutableElement_NotModifiable()
    {
        var p = new KejiToolParameterDefinition("x", KejiToolParameterType.StringArray, false, "desc",
            defaultValue: new List<string> { "a", "b" }, maxItems: 10, maxItemLength: 100);
        var dv = (ImmutableArray<string>)p.DefaultValue!;
        Assert.Equal("a", dv[0]);
        Assert.Equal("b", dv[1]);
        Assert.Equal(2, dv.Length);
    }

    [Theory]
    [InlineData("_path")]
    [InlineData("1path")]
    [InlineData("Path")]
    [InlineData(" path")]
    [InlineData("path ")]
    public void Parameter_InvalidName_Throws(string name)
    {
        Assert.Throws<KejiToolContractException>(() =>
            new KejiToolParameterDefinition(name, KejiToolParameterType.String, true, "desc", maxLength: 100));
    }
}
