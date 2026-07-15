using Keji.Tools.Definitions;
using Keji.Tools.Definitions.Parameters;
using Keji.Tools.Names;
using Keji.Tools.Registry;

namespace Keji.Tools.Validation;

public static class KejiToolInputValidator
{
    public static KejiToolValidationResult Validate(
        IKejiToolRegistry registry,
        string? toolName,
        IReadOnlyDictionary<string, object?>? inputs)
    {
        if (toolName is null || !KejiToolName.TryCreate(toolName, out var name))
            return KejiToolValidationResult.Invalid(
                KejiToolValidationError.InvalidToolName,
                "Invalid tool name format.");

        var resolution = registry.Resolve(name);
        if (resolution.Status != KejiToolResolutionStatus.Found || resolution.Definition is null)
            return KejiToolValidationResult.Invalid(
                KejiToolValidationError.UnknownTool,
                "Tool is not registered.");

        var definition = resolution.Definition;
        if (inputs is null || inputs.Count == 0)
        {
            if (definition.InputSchema.HasRequiredParams)
            {
                return KejiToolValidationResult.Invalid(
                    KejiToolValidationError.MissingRequiredParameter,
                    "Required parameters are missing.");
            }
            return KejiToolValidationResult.Valid();
        }

        var schema = definition.InputSchema;
        var paramMap = new Dictionary<string, KejiToolParameterDefinition>(StringComparer.Ordinal);
        foreach (var p in schema.Parameters)
            paramMap[p.Name] = p;

        foreach (var key in inputs.Keys)
        {
            if (!paramMap.ContainsKey(key))
                return KejiToolValidationResult.Invalid(
                    KejiToolValidationError.UnknownParameter,
                    $"Unknown parameter: '{key}'.");
        }

        foreach (var param in schema.Parameters)
        {
            if (param.Required && (!inputs.TryGetValue(param.Name, out var val) || val is null))
            {
                var msg = param.Sensitive
                    ? "Required parameter is missing."
                    : $"Required parameter '{param.Name}' is missing.";
                return KejiToolValidationResult.Invalid(KejiToolValidationError.MissingRequiredParameter, msg);
            }

            if (!inputs.TryGetValue(param.Name, out var value) || value is null)
                continue;

            if (!ValidateValueType(param, value, out var typeError, out var typeMsg))
                return KejiToolValidationResult.Invalid(typeError, typeMsg);

            if (!ValidateValueConstraints(param, value, out var constraintError, out var constraintMsg))
                return KejiToolValidationResult.Invalid(constraintError, constraintMsg);
        }

        return KejiToolValidationResult.Valid();
    }

    private static bool ValidateValueType(
        KejiToolParameterDefinition param, object value,
        out KejiToolValidationError error, out string message)
    {
        var expectedType = param.Type;

        bool ok = expectedType switch
        {
            KejiToolParameterType.String => value is string,
            KejiToolParameterType.Integer => value is int or long,
            KejiToolParameterType.Number => IsValidNumber(value),
            KejiToolParameterType.Boolean => value is bool,
            KejiToolParameterType.StringArray => value is IReadOnlyList<string> list && list.All(e => e is not null),
            KejiToolParameterType.IntegerArray => value is IReadOnlyList<int> || value is IReadOnlyList<long>,
            _ => false,
        };

        if (ok)
        {
            error = default;
            message = string.Empty;
            return true;
        }

        error = KejiToolValidationError.InvalidParameterType;
        message = param.Sensitive
            ? "Parameter has an invalid type."
            : $"Parameter '{param.Name}' has an invalid type.";
        return false;
    }

    private static bool IsValidNumber(object value)
    {
        if (value is double d)
            return !double.IsNaN(d) && !double.IsInfinity(d);
        if (value is float f)
            return !float.IsNaN(f) && !float.IsInfinity(f);
        return value is int or long;
    }

    private static bool ValidateValueConstraints(
        KejiToolParameterDefinition param, object value,
        out KejiToolValidationError error, out string message)
    {
        switch (param.Type)
        {
            case KejiToolParameterType.String when value is string str:
            {
                if (param.MaxLength.HasValue && str.Length > param.MaxLength.Value)
                {
                    error = KejiToolValidationError.ValueTooLong;
                    message = param.Sensitive
                        ? "Parameter value exceeds maximum length."
                        : $"Parameter '{param.Name}' exceeds maximum length.";
                    return false;
                }
                if (param.MinLength.HasValue && str.Length < param.MinLength.Value)
                {
                    error = KejiToolValidationError.ValueOutOfRange;
                    message = param.Sensitive
                        ? "Parameter value is too short."
                        : $"Parameter '{param.Name}' is too short.";
                    return false;
                }
                if (param.AllowedValues is not null && !param.AllowedValues.Contains(str))
                {
                    error = KejiToolValidationError.InvalidEnumValue;
                    message = param.Sensitive
                        ? "Parameter value is not allowed."
                        : $"Parameter '{param.Name}' has an invalid value.";
                    return false;
                }
                break;
            }
            case KejiToolParameterType.Integer when value is int i:
            {
                if (param.Minimum.HasValue && i < param.Minimum.Value)
                {
                    error = KejiToolValidationError.ValueOutOfRange;
                    message = param.Sensitive
                        ? "Parameter value is out of range."
                        : $"Parameter '{param.Name}' is below minimum.";
                    return false;
                }
                if (param.Maximum.HasValue && i > param.Maximum.Value)
                {
                    error = KejiToolValidationError.ValueOutOfRange;
                    message = param.Sensitive
                        ? "Parameter value is out of range."
                        : $"Parameter '{param.Name}' exceeds maximum.";
                    return false;
                }
                break;
            }
            case KejiToolParameterType.Integer when value is long l:
            {
                if (param.Minimum.HasValue && l < param.Minimum.Value)
                {
                    error = KejiToolValidationError.ValueOutOfRange;
                    message = param.Sensitive
                        ? "Parameter value is out of range."
                        : $"Parameter '{param.Name}' is below minimum.";
                    return false;
                }
                if (param.Maximum.HasValue && l > param.Maximum.Value)
                {
                    error = KejiToolValidationError.ValueOutOfRange;
                    message = param.Sensitive
                        ? "Parameter value is out of range."
                        : $"Parameter '{param.Name}' exceeds maximum.";
                    return false;
                }
                break;
            }
            case KejiToolParameterType.Number when value is int i:
            {
                if (param.Minimum.HasValue && i < param.Minimum.Value)
                {
                    error = KejiToolValidationError.ValueOutOfRange;
                    message = param.Sensitive
                        ? "Parameter value is out of range."
                        : $"Parameter '{param.Name}' is below minimum.";
                    return false;
                }
                if (param.Maximum.HasValue && i > param.Maximum.Value)
                {
                    error = KejiToolValidationError.ValueOutOfRange;
                    message = param.Sensitive
                        ? "Parameter value is out of range."
                        : $"Parameter '{param.Name}' exceeds maximum.";
                    return false;
                }
                break;
            }
            case KejiToolParameterType.Number when value is long l:
            {
                if (param.Minimum.HasValue && l < param.Minimum.Value)
                {
                    error = KejiToolValidationError.ValueOutOfRange;
                    message = param.Sensitive
                        ? "Parameter value is out of range."
                        : $"Parameter '{param.Name}' is below minimum.";
                    return false;
                }
                if (param.Maximum.HasValue && l > param.Maximum.Value)
                {
                    error = KejiToolValidationError.ValueOutOfRange;
                    message = param.Sensitive
                        ? "Parameter value is out of range."
                        : $"Parameter '{param.Name}' exceeds maximum.";
                    return false;
                }
                break;
            }
            case KejiToolParameterType.Number when value is double d:
            {
                if (param.Minimum.HasValue && d < (double)param.Minimum.Value)
                {
                    error = KejiToolValidationError.ValueOutOfRange;
                    message = param.Sensitive
                        ? "Parameter value is out of range."
                        : $"Parameter '{param.Name}' is below minimum.";
                    return false;
                }
                if (param.Maximum.HasValue && d > (double)param.Maximum.Value)
                {
                    error = KejiToolValidationError.ValueOutOfRange;
                    message = param.Sensitive
                        ? "Parameter value is out of range."
                        : $"Parameter '{param.Name}' exceeds maximum.";
                    return false;
                }
                break;
            }
            case KejiToolParameterType.Number when value is float f:
            {
                if (param.Minimum.HasValue && f < (float)param.Minimum.Value)
                {
                    error = KejiToolValidationError.ValueOutOfRange;
                    message = param.Sensitive
                        ? "Parameter value is out of range."
                        : $"Parameter '{param.Name}' is below minimum.";
                    return false;
                }
                if (param.Maximum.HasValue && f > (float)param.Maximum.Value)
                {
                    error = KejiToolValidationError.ValueOutOfRange;
                    message = param.Sensitive
                        ? "Parameter value is out of range."
                        : $"Parameter '{param.Name}' exceeds maximum.";
                    return false;
                }
                break;
            }
            case KejiToolParameterType.StringArray when value is IReadOnlyList<string> list:
            {
                if (param.MaxItems.HasValue && list.Count > param.MaxItems.Value)
                {
                    error = KejiToolValidationError.TooManyItems;
                    message = param.Sensitive
                        ? "Too many items."
                        : $"Parameter '{param.Name}' has too many items.";
                    return false;
                }
                break;
            }
            case KejiToolParameterType.IntegerArray when value is IReadOnlyList<int> list:
            {
                if (param.MaxItems.HasValue && list.Count > param.MaxItems.Value)
                {
                    error = KejiToolValidationError.TooManyItems;
                    message = param.Sensitive
                        ? "Too many items."
                        : $"Parameter '{param.Name}' has too many items.";
                    return false;
                }
                break;
            }
            case KejiToolParameterType.IntegerArray when value is IReadOnlyList<long> list:
            {
                if (param.MaxItems.HasValue && list.Count > param.MaxItems.Value)
                {
                    error = KejiToolValidationError.TooManyItems;
                    message = param.Sensitive
                        ? "Too many items."
                        : $"Parameter '{param.Name}' has too many items.";
                    return false;
                }
                break;
            }
        }

        error = default;
        message = string.Empty;
        return true;
    }
}
