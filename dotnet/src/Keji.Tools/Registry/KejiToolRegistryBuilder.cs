using System.Collections.Frozen;
using Keji.Tools.Definitions;
using Keji.Tools.Names;

namespace Keji.Tools.Registry;

public sealed class KejiToolRegistryBuilder
{
    private readonly Dictionary<string, KejiToolDefinition> _definitions = new(StringComparer.Ordinal);
    private bool _built;

    public void Register(KejiToolDefinition definition)
    {
        if (_built)
            throw new InvalidOperationException("Cannot register after the registry has been built.");

        if (definition is null)
            throw new ArgumentNullException(nameof(definition));

        var name = definition.Name.Value;
        if (_definitions.ContainsKey(name))
            throw new KejiToolContractException($"Duplicate tool name: '{name}'.");

        _definitions.Add(name, definition);
    }

    public IKejiToolRegistry Build()
    {
        if (_built)
            throw new InvalidOperationException("Registry has already been built.");

        _built = true;
        var frozen = _definitions.ToFrozenDictionary(StringComparer.Ordinal);
        return new KejiFrozenToolRegistry(frozen);
    }
}
