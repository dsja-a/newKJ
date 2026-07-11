namespace Keji.Configuration.Models;

public sealed class ConfigScalar : ConfigNode
{
    public string? Value { get; }

    public ConfigScalar(string? value)
    {
        Value = value;
    }

    public override ConfigNodeType NodeType => ConfigNodeType.Scalar;

    public override string ToString() => Value ?? "null";
}
