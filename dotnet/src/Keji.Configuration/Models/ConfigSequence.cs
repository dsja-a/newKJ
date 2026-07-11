using System.Collections;

namespace Keji.Configuration.Models;

public sealed class ConfigSequence : ConfigNode, IReadOnlyList<ConfigNode>
{
    private readonly IReadOnlyList<ConfigNode> _items;

    public ConfigSequence(IReadOnlyList<ConfigNode> items)
    {
        _items = items;
    }

    public override ConfigNodeType NodeType => ConfigNodeType.Sequence;

    public ConfigNode this[int index] => _items[index];
    public int Count => _items.Count;

    public IEnumerator<ConfigNode> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

    public override string ToString() => $"[Count = {Count}]";
}
