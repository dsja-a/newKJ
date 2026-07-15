using System.Collections.Frozen;

namespace Keji.Tools.Definitions.Parameters;

public sealed class KejiToolParameterDefinition
{
    private static readonly int MaxNameLength = 64;
    private static readonly int MaxDescriptionLength = 2000;
    private static readonly int GlobalMaxLength = 100000;

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
        if (description.Length > MaxDescriptionLength)
            throw new KejiToolContractException($"Parameter description must not exceed {MaxDescriptionLength} characters.");

        if (!Enum.IsDefined(type))
            throw new KejiToolContractException("Parameter type must be a defined enum value.");

        switch (type)
        {
            case KejiToolParameterType.String:
                if (minimum.HasValue || maximum.HasValue || maxItems.HasValue)
                    throw new KejiToolContractException("String parameters must not use Minimum, Maximum, or MaxItems constraints.");
                if (!maxLength.HasValue)
                    throw new KejiToolContractException("String parameters must specify MaxLength.");
                if (maxLength.Value < 1 || maxLength.Value > GlobalMaxLength)
                    throw new KejiToolContractException($"MaxLength must be between 1 and {GlobalMaxLength}.");
                if (minLength.HasValue && minLength.Value < 0)
                    throw new KejiToolContractException("MinLength must not be negative.");
                if (allowedValues is not null)
                {
                    if (allowedValues.Count == 0)
                        throw new KejiToolContractException("AllowedValues must contain at least one value.");
                    if (allowedValues.Count != new HashSet<string>(allowedValues, StringComparer.Ordinal).Count)
                        throw new KejiToolContractException("AllowedValues must not contain duplicates.");
                }
                break;

            case KejiToolParameterType.Integer:
                if (minLength.HasValue || maxLength.HasValue || maxItems.HasValue)
                    throw new KejiToolContractException("Integer parameters must not use string or array constraints.");
                if (allowedValues is not null)
                    throw new KejiToolContractException("Integer parameters must not use AllowedValues.");
                break;

            case KejiToolParameterType.Number:
                if (minLength.HasValue || maxLength.HasValue || maxItems.HasValue)
                    throw new KejiToolContractException("Number parameters must not use string or array constraints.");
                if (allowedValues is not null)
                    throw new KejiToolContractException("Number parameters must not use AllowedValues.");
                break;

            case KejiToolParameterType.Boolean:
                if (minimum.HasValue || maximum.HasValue || minLength.HasValue || maxLength.HasValue || maxItems.HasValue)
                    throw new KejiToolContractException("Boolean parameters must not use any constraints.");
                if (allowedValues is not null)
                    throw new KejiToolContractException("Boolean parameters must not use AllowedValues.");
                break;

            case KejiToolParameterType.StringArray:
                if (!maxItems.HasValue)
                    throw new KejiToolContractException("Array parameters must specify MaxItems.");
                if (maxItems.Value <= 0)
                    throw new KejiToolContractException("MaxItems must be greater than 0.");
                if (minimum.HasValue || maximum.HasValue || minLength.HasValue || maxLength.HasValue)
                    throw new KejiToolContractException("StringArray parameters must not use scalar constraints.");
                if (allowedValues is not null)
                    throw new KejiToolContractException("StringArray parameters must not use AllowedValues.");
                break;

            case KejiToolParameterType.IntegerArray:
                if (!maxItems.HasValue)
                    throw new KejiToolContractException("Array parameters must specify MaxItems.");
                if (maxItems.Value <= 0)
                    throw new KejiToolContractException("MaxItems must be greater than 0.");
                if (minimum.HasValue || maximum.HasValue || minLength.HasValue || maxLength.HasValue)
                    throw new KejiToolContractException("IntegerArray parameters must not use scalar constraints.");
                if (allowedValues is not null)
                    throw new KejiToolContractException("IntegerArray parameters must not use AllowedValues.");
                break;
        }

        if (defaultValue is not null && !IsDefaultValueValid(type, defaultValue))
            throw new KejiToolContractException("Default value type does not match parameter type.");

        if (minimum.HasValue && maximum.HasValue && minimum > maximum)
            throw new KejiToolContractException("Minimum must not exceed Maximum.");

        if (minLength.HasValue && maxLength.HasValue && minLength > maxLength)
            throw new KejiToolContractException("MinLength must not exceed MaxLength.");

        Name = name;
        Type = type;
        Required = required;
        Description = description;
        DefaultValue = TakeSnapshot(type, defaultValue);
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

    private static object? TakeSnapshot(KejiToolParameterType type, object? value)
    {
        if (value is null)
            return null;

        return type switch
        {
            KejiToolParameterType.StringArray when value is IReadOnlyList<string> list
                => list.ToArray(),
            KejiToolParameterType.IntegerArray when value is IReadOnlyList<int> list
                => list.ToArray(),
            KejiToolParameterType.IntegerArray when value is IReadOnlyList<long> list
                => list.ToArray(),
            _ => value,
        };
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
            KejiToolParameterType.IntegerArray => value is IReadOnlyList<int> or IReadOnlyList<long>,
            _ => false,
        };
    }
}
