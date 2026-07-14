using System.Collections.Frozen;

namespace Keji.Tools.Definitions.Parameters;

public sealed class KejiToolParameterDefinition
{
    private static readonly int MaxNameLength = 64;

    public string Name { get; }
    public KejiToolParameterType Type { get; }
    public bool Required { get; }
    public string Description { get; }
    public object? DefaultValue { get; }
    public int? Minimum { get; }
    public int? Maximum { get; }
    public int? MinLength { get; }
    public int? MaxLength { get; }
    public int? MaxItems { get; }
    public bool Sensitive { get; }
    public IReadOnlySet<string>? AllowedValues { get; }

    private static readonly FrozenSet<char> AllowedNameChars = new HashSet<char>(
        "_abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray()).ToFrozenSet();

    public KejiToolParameterDefinition(
        string name,
        KejiToolParameterType type,
        bool required,
        string description,
        object? defaultValue = null,
        int? minimum = null,
        int? maximum = null,
        int? minLength = null,
        int? maxLength = null,
        int? maxItems = null,
        bool sensitive = false,
        IReadOnlySet<string>? allowedValues = null)
    {
        if (name is null)
            throw new KejiToolContractException("Parameter name must not be null.");
        if (name.Length == 0)
            throw new KejiToolContractException("Parameter name must not be empty.");
        if (name.Length > MaxNameLength)
            throw new KejiToolContractException($"Parameter name must not exceed {MaxNameLength} characters.");
        if (!IsValidParameterName(name))
            throw new KejiToolContractException("Parameter name must match: lowercase letters, digits, underscores only.");
        if (description is null)
            throw new KejiToolContractException("Parameter description must not be null.");
        if (description.Length == 0)
            throw new KejiToolContractException("Parameter description must not be empty.");

        if (defaultValue is not null && !IsDefaultValueValid(type, defaultValue))
            throw new KejiToolContractException("Default value type does not match parameter type.");

        if (required && defaultValue is not null && defaultValue is string s && s.Length == 0)
            throw new KejiToolContractException("Required parameter must not have a null or empty default value.");

        if (maxLength.HasValue && maxLength.Value <= 0)
            throw new KejiToolContractException("MaxLength must be greater than 0.");
        if (minLength.HasValue && maxLength.HasValue && minLength > maxLength)
            throw new KejiToolContractException("MinLength must not exceed MaxLength.");

        if (type == KejiToolParameterType.StringArray || type == KejiToolParameterType.IntegerArray)
        {
            if (!maxItems.HasValue)
                throw new KejiToolContractException("Array parameters must specify MaxItems.");
            if (maxItems.Value <= 0)
                throw new KejiToolContractException("MaxItems must be greater than 0.");
        }

        if (minimum.HasValue && maximum.HasValue && minimum > maximum)
            throw new KejiToolContractException("Minimum must not exceed Maximum.");

        if (allowedValues is not null)
        {
            if (allowedValues.Count == 0)
                throw new KejiToolContractException("AllowedValues must contain at least one value.");
            if (allowedValues.Count != new HashSet<string>(allowedValues, StringComparer.Ordinal).Count)
                throw new KejiToolContractException("AllowedValues must not contain duplicates.");
        }

        Name = name;
        Type = type;
        Required = required;
        Description = description;
        DefaultValue = defaultValue;
        Minimum = minimum;
        Maximum = maximum;
        MinLength = minLength;
        MaxLength = maxLength;
        MaxItems = maxItems;
        Sensitive = sensitive;
        AllowedValues = allowedValues is not null
            ? allowedValues.ToFrozenSet(StringComparer.Ordinal)
            : null;
    }

    private static bool IsValidParameterName(string name)
    {
        foreach (var c in name)
        {
            if (!AllowedNameChars.Contains(c))
                return false;
        }
        return true;
    }

    private static bool IsDefaultValueValid(KejiToolParameterType type, object value)
    {
        return type switch
        {
            KejiToolParameterType.String => value is string,
            KejiToolParameterType.Integer => value is int or long,
            KejiToolParameterType.Number => value is double or float or int or long,
            KejiToolParameterType.Boolean => value is bool,
            KejiToolParameterType.StringArray => value is IReadOnlyList<string>,
            KejiToolParameterType.IntegerArray => value is IReadOnlyList<int>,
            _ => false,
        };
    }
}
