using System.Collections;

namespace Keji.Configuration.Models;

public sealed class ConfigMap : ConfigNode, IReadOnlyDictionary<string, ConfigNode>
{
    private readonly Dictionary<string, ConfigNode> _entries;

    public ConfigMap(IEnumerable<KeyValuePair<string, ConfigNode>> entries)
    {
        _entries = new Dictionary<string, ConfigNode>(StringComparer.OrdinalIgnoreCase!);
        foreach (var kvp in entries)
        {
            _entries[kvp.Key] = kvp.Value;
        }
    }

    public override ConfigNodeType NodeType => ConfigNodeType.Map;

    public ConfigNode this[string key] => _entries[key];
    public IEnumerable<string> Keys => _entries.Keys;
    public IEnumerable<ConfigNode> Values => _entries.Values;
    public int Count => _entries.Count;

    public bool ContainsKey(string key) => _entries.ContainsKey(key);
    public IEnumerator<KeyValuePair<string, ConfigNode>> GetEnumerator() => _entries.GetEnumerator();
    public bool TryGetValue(string key, out ConfigNode value)
    {
        var result = _entries.TryGetValue(key, out var v);
        value = v!;
        return result;
    }
    IEnumerator IEnumerable.GetEnumerator() => _entries.GetEnumerator();

    public override string ToString() => $"{{Count = {Count}}}";
}
