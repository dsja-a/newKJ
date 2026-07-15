using Keji.Security.Authorization;
using Keji.Tools.Definitions;
using Keji.Tools.Definitions.Parameters;
using Keji.Tools.Names;
using Keji.Tools.Registry;
using Keji.Tools.Validation;

namespace Keji.Tools.Tests;

public class KejiToolValidationTests
{
    private static KejiToolRegistryBuilder CreateBuilderWith(params KejiToolDefinition[] defs)
    {
        var builder = new KejiToolRegistryBuilder();
        foreach (var def in defs)
            builder.Register(def);
        return builder;
    }

    private static KejiToolDefinition MakeDef(string name, params KejiToolParameterDefinition[] parameters) => new(
        KejiToolName.Create(name), 1, $"Tool {name}.",
        KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly, KejiToolExecutionTarget.Host,
        KejiPermission.ToolCatalogRead,
        inputSchema: parameters.Length > 0
            ? new KejiToolInputSchema(parameters.ToList())
            : null);

    private static Dictionary<string, object?> DI(params (string key, object? value)[] entries) =>
        entries.ToDictionary(e => e.key, e => e.value);

    [Fact]
    public void Validate_ValidInput_Passes()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("path", KejiToolParameterType.String, true, "Path.", maxLength: 1024)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("path", "/some/file.txt")));
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_NullInputs_Passes()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("path", KejiToolParameterType.String, false, "Path.", maxLength: 1024)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", null);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_InvalidToolName_ReturnsError()
    {
        var registry = CreateBuilderWith().Build();
        var result = KejiToolInputValidator.Validate(registry, "", null);
        Assert.False(result.IsValid);
        Assert.Contains(KejiToolValidationError.InvalidToolName, result.Errors);
    }

    [Fact]
    public void Validate_ToolNotFound_ReturnsError()
    {
        var registry = CreateBuilderWith().Build();
        var result = KejiToolInputValidator.Validate(registry, "nonexistent", null);
        Assert.False(result.IsValid);
        Assert.Contains(KejiToolValidationError.UnknownTool, result.Errors);
    }

    [Fact]
    public void Validate_MissingRequiredParam_ReturnsError()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("path", KejiToolParameterType.String, true, "Path.", maxLength: 1024)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI());
        Assert.False(result.IsValid);
        Assert.Contains(KejiToolValidationError.MissingRequiredParameter, result.Errors);
    }

    [Fact]
    public void Validate_TypeMismatch_ReturnsError()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("count", KejiToolParameterType.Integer, true, "Count.")))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("count", "not_a_number")));
        Assert.False(result.IsValid);
        Assert.Contains(KejiToolValidationError.InvalidParameterType, result.Errors);
    }

    [Fact]
    public void Validate_IntegerValid_Passes()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("count", KejiToolParameterType.Integer, true, "Count.")))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("count", 42)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_IntegerLongValid_Passes()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("count", KejiToolParameterType.Integer, true, "Count.",
                minimum: 1, maximum: 100)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("count", 50L)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_IntegerLongBelowMin_ReturnsError()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("count", KejiToolParameterType.Integer, true, "Count.",
                minimum: 10)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("count", 5L)));
        Assert.False(result.IsValid);
        Assert.Contains(KejiToolValidationError.ValueOutOfRange, result.Errors);
    }

    [Fact]
    public void Validate_IntegerLongAboveMax_ReturnsError()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("count", KejiToolParameterType.Integer, true, "Count.",
                maximum: 100)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("count", 200L)));
        Assert.False(result.IsValid);
        Assert.Contains(KejiToolValidationError.ValueOutOfRange, result.Errors);
    }

    [Fact]
    public void Validate_NumberType_AcceptsInteger()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("ratio", KejiToolParameterType.Number, true, "Ratio.")))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("ratio", 42)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NumberType_AcceptsDouble()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("ratio", KejiToolParameterType.Number, true, "Ratio.")))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("ratio", 3.14)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NumberType_AcceptsFloat()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("ratio", KejiToolParameterType.Number, true, "Ratio.")))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("ratio", 3.14f)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NumberType_RejectsNaN()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("ratio", KejiToolParameterType.Number, true, "Ratio.")))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("ratio", double.NaN)));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NumberType_RejectsInfinity()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("ratio", KejiToolParameterType.Number, true, "Ratio.")))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("ratio", double.PositiveInfinity)));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NumberType_RejectsNegativeInfinity()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("ratio", KejiToolParameterType.Number, true, "Ratio.")))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("ratio", double.NegativeInfinity)));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NumberType_FloatNan_Rejected()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("ratio", KejiToolParameterType.Number, true, "Ratio.")))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("ratio", float.NaN)));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NumberType_FloatInfinity_Rejected()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("ratio", KejiToolParameterType.Number, true, "Ratio.")))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("ratio", float.PositiveInfinity)));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NumberType_LongAboveMax_ReturnsError()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("ratio", KejiToolParameterType.Number, true, "Ratio.",
                maximum: 100)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("ratio", 200L)));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NumberType_LongBelowMin_ReturnsError()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("ratio", KejiToolParameterType.Number, true, "Ratio.",
                minimum: 10)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("ratio", 5L)));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NumberType_DoubleBelowMin_ReturnsError()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("ratio", KejiToolParameterType.Number, true, "Ratio.",
                minimum: 10)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("ratio", 5.0)));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_NumberType_DoubleAboveMax_ReturnsError()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("ratio", KejiToolParameterType.Number, true, "Ratio.",
                maximum: 100)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("ratio", 200.0)));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_BooleanType_AcceptsTrue()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("flag", KejiToolParameterType.Boolean, true, "Flag.")))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("flag", true)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_BooleanType_AcceptsFalse()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("flag", KejiToolParameterType.Boolean, true, "Flag.")))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("flag", false)));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_StringArrayType_AcceptsList()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("items", KejiToolParameterType.StringArray, true, "Items.",
                maxItems: 5)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("items", new List<string> { "a", "b" })));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_StringArrayType_RejectsString()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("items", KejiToolParameterType.StringArray, true, "Items.",
                maxItems: 5)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("items", "not_a_list")));
        Assert.False(result.IsValid);
        Assert.Contains(KejiToolValidationError.InvalidParameterType, result.Errors);
    }

    [Fact]
    public void Validate_IntegerArrayType_AcceptsIntList()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("ids", KejiToolParameterType.IntegerArray, true, "IDs.",
                maxItems: 10)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("ids", new List<int> { 1, 2, 3 })));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_IntegerArrayType_AcceptsLongList()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("ids", KejiToolParameterType.IntegerArray, true, "IDs.",
                maxItems: 10)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("ids", new List<long> { 1L, 2L })));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_IntegerArrayType_ExceedsMaxItems_ReturnsError()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("ids", KejiToolParameterType.IntegerArray, true, "IDs.",
                maxItems: 2)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool",
            DI(("ids", new List<int> { 1, 2, 3 })));
        Assert.False(result.IsValid);
        Assert.Contains(KejiToolValidationError.TooManyItems, result.Errors);
    }

    [Fact]
    public void Validate_IntegerArrayType_LongListExceedsMaxItems_ReturnsError()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("ids", KejiToolParameterType.IntegerArray, true, "IDs.",
                maxItems: 2)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool",
            DI(("ids", new List<long> { 1L, 2L, 3L })));
        Assert.False(result.IsValid);
        Assert.Contains(KejiToolValidationError.TooManyItems, result.Errors);
    }

    [Fact]
    public void Validate_MaxItems_Exceeded_ReturnsError()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("items", KejiToolParameterType.StringArray, true, "Items.",
                maxItems: 2)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool",
            DI(("items", new List<string> { "a", "b", "c" })));
        Assert.False(result.IsValid);
        Assert.Contains(KejiToolValidationError.TooManyItems, result.Errors);
    }

    [Fact]
    public void Validate_StringLength_Exceeded_ReturnsError()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("name", KejiToolParameterType.String, true, "Name.",
                maxLength: 5)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("name", "toolong")));
        Assert.False(result.IsValid);
        Assert.Contains(KejiToolValidationError.ValueTooLong, result.Errors);
    }

    [Fact]
    public void Validate_StringMinLength_TooShort_ReturnsError()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("name", KejiToolParameterType.String, true, "Name.",
                minLength: 3, maxLength: 100)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("name", "ab")));
        Assert.False(result.IsValid);
        Assert.Contains(KejiToolValidationError.ValueOutOfRange, result.Errors);
    }

    [Fact]
    public void Validate_IntegerMinimum_Exceeded_ReturnsError()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("count", KejiToolParameterType.Integer, true, "Count.",
                minimum: 10)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("count", 5)));
        Assert.False(result.IsValid);
        Assert.Contains(KejiToolValidationError.ValueOutOfRange, result.Errors);
    }

    [Fact]
    public void Validate_IntegerMaximum_Exceeded_ReturnsError()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("count", KejiToolParameterType.Integer, true, "Count.",
                maximum: 100)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("count", 200)));
        Assert.False(result.IsValid);
        Assert.Contains(KejiToolValidationError.ValueOutOfRange, result.Errors);
    }

    [Fact]
    public void Validate_AllowedValues_AcceptsValid()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("mode", KejiToolParameterType.String, true, "Mode.",
                allowedValues: new HashSet<string>(StringComparer.Ordinal) { "read", "write" }, maxLength: 100)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("mode", "read")));
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_AllowedValues_RejectsInvalid()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("mode", KejiToolParameterType.String, true, "Mode.",
                allowedValues: new HashSet<string>(StringComparer.Ordinal) { "read", "write" }, maxLength: 100)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("mode", "delete")));
        Assert.False(result.IsValid);
        Assert.Contains(KejiToolValidationError.InvalidEnumValue, result.Errors);
    }

    [Fact]
    public void Validate_UnknownParam_ReturnsError()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("path", KejiToolParameterType.String, false, "Path.", maxLength: 1024)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("extra_param", "value")));
        Assert.False(result.IsValid);
        Assert.Contains(KejiToolValidationError.UnknownParameter, result.Errors);
    }

    [Fact]
    public void Validate_FirstErrorReturned()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("path", KejiToolParameterType.String, true, "Path.",
                maxLength: 10)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(
            ("unknown", "value")));
        Assert.False(result.IsValid);
        Assert.Contains(KejiToolValidationError.UnknownParameter, result.Errors);
    }

    [Fact]
    public void Validate_SensitiveParameter_ErrorMessage()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("password", KejiToolParameterType.String, true, "Password.",
                sensitive: true, maxLength: 10)))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("password", "supersecret123")));
        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
        Assert.DoesNotContain("supersecret123", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_NoInputs_ForToolWithoutParams_Passes()
    {
        var registry = CreateBuilderWith(MakeDef("no_params")).Build();
        var result = KejiToolInputValidator.Validate(registry, "no_params", DI());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_NullInputs_ForToolWithoutParams_Passes()
    {
        var registry = CreateBuilderWith(MakeDef("no_params")).Build();
        var result = KejiToolInputValidator.Validate(registry, "no_params", null);
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidationResult_Valid_IsValid()
    {
        var result = KejiToolValidationResult.Valid();
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void ValidationResult_Invalid_HasErrors()
    {
        var result = KejiToolValidationResult.Invalid(
            KejiToolValidationError.MissingRequiredParameter, "Path is required.");
        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.Contains(KejiToolValidationError.MissingRequiredParameter, result.Errors);
        Assert.Equal("Path is required.", result.ErrorMessage);
    }

    [Fact]
    public void Validate_Integer_NullInputForNonNullable_ReturnsError()
    {
        var registry = CreateBuilderWith(MakeDef("test_tool",
            new KejiToolParameterDefinition("count", KejiToolParameterType.Integer, true, "Count.")))
            .Build();
        var result = KejiToolInputValidator.Validate(registry, "test_tool", DI(("count", null)));
        Assert.False(result.IsValid);
        Assert.Contains(KejiToolValidationError.MissingRequiredParameter, result.Errors);
    }
}
