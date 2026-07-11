namespace Keji.Configuration.Models;

public abstract class ConfigNode
{
    public abstract ConfigNodeType NodeType { get; }

    public ConfigScalar AsScalar() => (ConfigScalar)this;
    public ConfigMap AsMap() => (ConfigMap)this;
    public ConfigSequence AsSequence() => (ConfigSequence)this;
}

public enum ConfigNodeType
{
    Scalar,
    Map,
    Sequence
}
