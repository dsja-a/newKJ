using System.Collections.Frozen;
using Keji.Tools.Definitions;
using Keji.Tools.Names;

namespace Keji.Tools.Registry;

internal sealed class KejiFrozenToolRegistry : IKejiToolRegistry
{
    private readonly FrozenDictionary<string, KejiToolDefinition> _definitions;

    public KejiFrozenToolRegistry(FrozenDictionary<string, KejiToolDefinition> definitions)
    {
        _definitions = definitions;
    }

    public KejiToolResolution Resolve(KejiToolName name)
    {
        if (_definitions.TryGetValue(name.Value, out var definition))
            return KejiToolResolution.Found(definition);
        return KejiToolResolution.NotRegistered(name);
    }

    public bool Contains(KejiToolName name) => _definitions.ContainsKey(name.Value);

    public IReadOnlyList<KejiToolDefinition> GetAll() =>
        _definitions.Values.OrderBy(d => d.Name.Value, StringComparer.Ordinal).ToList();
}
