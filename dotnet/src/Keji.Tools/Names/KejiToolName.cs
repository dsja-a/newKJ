using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Keji.Tools.Names;

public sealed class KejiToolName : IEquatable<KejiToolName>
{
    private static readonly Regex Pattern = new(
        @"^[a-z][a-z0-9_]{0,63}$",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public string Value { get; }

    private KejiToolName(string value)
    {
        Value = value;
    }

    public static KejiToolName Create(string? name)
    {
        if (name is null)
            throw new KejiToolContractException("Tool name must not be null.");

        if (name.Length == 0)
            throw new KejiToolContractException("Tool name must not be empty.");

        if (name.Length > 64)
            throw new KejiToolContractException("Tool name must not exceed 64 characters.");

        if (!Pattern.IsMatch(name))
            throw new KejiToolContractException("Tool name must match pattern: ^[a-z][a-z0-9_]{0,63}$");

        return new KejiToolName(name);
    }

    public static bool TryCreate(string? name, [NotNullWhen(true)] out KejiToolName? result)
    {
        if (name is null || name.Length == 0 || name.Length > 64 || !Pattern.IsMatch(name))
        {
            result = null;
            return false;
        }
        result = new KejiToolName(name);
        return true;
    }

    public bool Equals(KejiToolName? other) => other is not null && StringComparer.Ordinal.Equals(Value, other.Value);
    public override bool Equals(object? obj) => obj is KejiToolName other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value;

    public static bool operator ==(KejiToolName? left, KejiToolName? right) =>
        EqualityComparer<KejiToolName>.Default.Equals(left, right);
    public static bool operator !=(KejiToolName? left, KejiToolName? right) => !(left == right);
}
