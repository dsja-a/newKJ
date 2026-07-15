using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Keji.Tools.Definitions.Parameters;

public sealed class KejiToolParameterDefinition
{
    private static readonly int MaxNameLength = 64;
    private static readonly int MaxDescriptionLength = 2000;
    private static readonly int GlobalMaxLength = 100000;
    private static readonly int GlobalMaxItemLength = 100000;

    private static readonly Regex NamePattern = new("^[a-z][a-z0-9_]{0,63}$", RegexOptions.Singleline);

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
    public int? MaxItemLength { get; }
    public bool Sensitive { get; }
    public IReadOnlySet<string>? AllowedValues { get; }

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
        int? maxItemLength = null,
        bool sensitive = false,
        IReadOnlySet<string>? allowedValues = null)
    {
        if (name is null)
            throw new KejiToolContractException("Parameter name must not be null.");
        if (name.Length == 0 || name.Length > MaxNameLength || !NamePattern.IsMatch(name))
            throw new KejiToolContractException("Parameter name must match: ^[a-z][a-z0-9_]{0,63}$.");

        if (description is null)
            throw new KejiToolContractException("Parameter description must not be null.");
        if (description.Length == 0)
            throw new KejiToolContractException("Parameter description must not be empty.");
        if (description.Length > MaxDescriptionLength)
            throw new KejiToolContractException($"Parameter description must not exceed {MaxDescriptionLength} characters.");

        if (!Enum.IsDefined(type))
            throw new KejiToolContractException("Parameter type must be a defined enum value.");

        if (required && defaultValue is not null)
            throw new KejiToolContractException("Required parameter must not have a default value.");

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
                if (maxItemLength.HasValue)
                    throw new KejiToolContractException("String parameters must not use MaxItemLength.");
                CheckAllowedValues(allowedValues);
                break;

            case KejiToolParameterType.Integer:
                if (minLength.HasValue || maxLength.HasValue || maxItems.HasValue || maxItemLength.HasValue)
                    throw new KejiToolContractException("Integer parameters must not use string or array constraints.");
                if (allowedValues is not null)
                    throw new KejiToolContractException("Integer parameters must not use AllowedValues.");
                break;

            case KejiToolParameterType.Number:
                if (minLength.HasValue || maxLength.HasValue || maxItems.HasValue || maxItemLength.HasValue)
                    throw new KejiToolContractException("Number parameters must not use string or array constraints.");
                if (allowedValues is not null)
                    throw new KejiToolContractException("Number parameters must not use AllowedValues.");
                break;

            case KejiToolParameterType.Boolean:
                if (minimum.HasValue || maximum.HasValue || minLength.HasValue || maxLength.HasValue || maxItems.HasValue || maxItemLength.HasValue)
                    throw new KejiToolContractException("Boolean parameters must not use any constraints.");
                if (allowedValues is not null)
                    throw new KejiToolContractException("Boolean parameters must not use AllowedValues.");
                break;

            case KejiToolParameterType.StringArray:
                if (!maxItems.HasValue)
                    throw new KejiToolContractException("Array parameters must specify MaxItems.");
                if (maxItems.Value <= 0)
                    throw new KejiToolContractException("MaxItems must be greater than 0.");
                if (!maxItemLength.HasValue)
                    throw new KejiToolContractException("StringArray parameters must specify MaxItemLength.");
                if (maxItemLength.Value < 1 || maxItemLength.Value > GlobalMaxItemLength)
                    throw new KejiToolContractException($"MaxItemLength must be between 1 and {GlobalMaxItemLength}.");
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
                if (maxItemLength.HasValue)
                    throw new KejiToolContractException("IntegerArray parameters must not use MaxItemLength.");
                if (minimum.HasValue || maximum.HasValue || minLength.HasValue || maxLength.HasValue)
                    throw new KejiToolContractException("IntegerArray parameters must not use scalar constraints.");
                if (allowedValues is not null)
                    throw new KejiToolContractException("IntegerArray parameters must not use AllowedValues.");
                break;
        }

        if (defaultValue is not null && !IsValidDefaultValueType(type, defaultValue))
            throw new KejiToolContractException("Default value type does not match parameter type.");

        if (minimum.HasValue && maximum.HasValue && minimum > maximum)
            throw new KejiToolContractException("Minimum must not exceed Maximum.");
        if (minLength.HasValue && maxLength.HasValue && minLength > maxLength)
            throw new KejiToolContractException("MinLength must not exceed MaxLength.");

        var snapshot = TakeSnapshot(type, defaultValue);
        ValidateDefaultValue(type, snapshot, minimum, maximum, minLength, maxLength, maxItems, maxItemLength, allowedValues, sensitive);

        Name = name;
        Type = type;
        Required = required;
        Description = description;
        DefaultValue = snapshot;
        Minimum = minimum;
        Maximum = maximum;
        MinLength = minLength;
        MaxLength = maxLength;
        MaxItems = maxItems;
        MaxItemLength = maxItemLength;
        Sensitive = sensitive;
        AllowedValues = allowedValues is not null
            ? allowedValues.ToFrozenSet(StringComparer.Ordinal)
            : null;
    }

    private static void CheckAllowedValues(IReadOnlySet<string>? allowedValues)
    {
        if (allowedValues is null)
            return;
        if (allowedValues.Count == 0)
            throw new KejiToolContractException("AllowedValues must contain at least one value.");
        if (allowedValues.Count != new HashSet<string>(allowedValues, StringComparer.Ordinal).Count)
            throw new KejiToolContractException("AllowedValues must not contain duplicates.");
    }

    private static void ValidateDefaultValue(
        KejiToolParameterType type, object? value,
        int? minimum, int? maximum,
        int? minLength, int? maxLength,
        int? maxItems, int? maxItemLength,
        IReadOnlySet<string>? allowedValues, bool sensitive)
    {
        if (value is null)
            return;

        switch (type)
        {
            case KejiToolParameterType.String when value is string s:
            {
                if (maxLength.HasValue && s.Length > maxLength.Value)
                    Fail("Default value exceeds maximum length.", sensitive);
                if (minLength.HasValue && s.Length < minLength.Value)
                    Fail("Default value is too short.", sensitive);
                if (allowedValues is not null && !allowedValues.Contains(s))
                    Fail("Default value is not in the allowed values.", sensitive);
                break;
            }
            case KejiToolParameterType.Integer when value is int i:
            {
                if (minimum.HasValue && i < minimum.Value)
                    Fail("Default value is below minimum.", sensitive);
                if (maximum.HasValue && i > maximum.Value)
                    Fail("Default value exceeds maximum.", sensitive);
                break;
            }
            case KejiToolParameterType.Integer when value is long l:
            {
                if (minimum.HasValue && l < minimum.Value)
                    Fail("Default value is below minimum.", sensitive);
                if (maximum.HasValue && l > maximum.Value)
                    Fail("Default value exceeds maximum.", sensitive);
                break;
            }
            case KejiToolParameterType.Number when value is double d:
            {
                if (double.IsNaN(d) || double.IsInfinity(d))
                    Fail("Default value must not be NaN or Infinity.", sensitive);
                if (minimum.HasValue && d < (double)minimum.Value)
                    Fail("Default value is below minimum.", sensitive);
                if (maximum.HasValue && d > (double)maximum.Value)
                    Fail("Default value exceeds maximum.", sensitive);
                break;
            }
            case KejiToolParameterType.Number when value is float f:
            {
                if (float.IsNaN(f) || float.IsInfinity(f))
                    Fail("Default value must not be NaN or Infinity.", sensitive);
                if (minimum.HasValue && f < (float)minimum.Value)
                    Fail("Default value is below minimum.", sensitive);
                if (maximum.HasValue && f > (float)maximum.Value)
                    Fail("Default value exceeds maximum.", sensitive);
                break;
            }
            case KejiToolParameterType.Number when value is int i:
            {
                if (minimum.HasValue && i < minimum.Value)
                    Fail("Default value is below minimum.", sensitive);
                if (maximum.HasValue && i > maximum.Value)
                    Fail("Default value exceeds maximum.", sensitive);
                break;
            }
            case KejiToolParameterType.Number when value is long l:
            {
                if (minimum.HasValue && l < minimum.Value)
                    Fail("Default value is below minimum.", sensitive);
                if (maximum.HasValue && l > maximum.Value)
                    Fail("Default value exceeds maximum.", sensitive);
                break;
            }
            case KejiToolParameterType.StringArray when value is ImmutableArray<string> arr:
            {
                if (maxItems.HasValue && arr.Length > maxItems.Value)
                    Fail("Default value has too many items.", sensitive);
                for (int i = 0; i < arr.Length; i++)
                {
                    if (arr[i] is null)
                        Fail("Default value must not contain null items.", sensitive);
                    if (maxItemLength.HasValue && arr[i].Length > maxItemLength.Value)
                        Fail($"Default value item exceeds maximum item length.", sensitive);
                }
                break;
            }
            case KejiToolParameterType.IntegerArray when value is ImmutableArray<long> arr:
            {
                if (maxItems.HasValue && arr.Length > maxItems.Value)
                    Fail("Default value has too many items.", sensitive);
                break;
            }
        }
    }

    private static void Fail(string message, bool sensitive)
    {
        throw new KejiToolContractException(sensitive ? "Default value is invalid." : message);
    }

    private static object? TakeSnapshot(KejiToolParameterType type, object? value)
    {
        if (value is null)
            return null;

        return type switch
        {
            KejiToolParameterType.StringArray when value is IReadOnlyList<string> list
                => ImmutableArray.Create(list.ToArray()),
            KejiToolParameterType.IntegerArray when value is IReadOnlyList<int> list
                => ImmutableArray.CreateRange(list.Select(i => (long)i)),
            KejiToolParameterType.IntegerArray when value is IReadOnlyList<long> list
                => ImmutableArray.Create(list.ToArray()),
            _ => value,
        };
    }

    public static bool IsValidDefaultValueType(KejiToolParameterType type, object value)
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
