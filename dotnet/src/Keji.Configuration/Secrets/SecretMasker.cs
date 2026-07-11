using System.Collections.Frozen;
using Keji.Configuration.Models;

namespace Keji.Configuration.Secrets;

public class SecretMasker : ISecretMasker
{
    private static readonly FrozenSet<string> SensitiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "passwd",
        "pwd",
        "secret",
        "api_key",
        "apikey",
        "api-key",
        "token",
        "auth_token",
        "authtoken",
        "access_token",
        "accesstoken",
        "private_key",
        "privatekey",
        "connection_string",
        "connectionstring",
        "master_key",
        "masterkey",
    }.ToFrozenSet();

    public ConfigNode Mask(ConfigNode node)
    {
        return Visit(node);
    }

    private ConfigNode Visit(ConfigNode node)
    {
        switch (node)
        {
            case ConfigScalar scalar:
                return scalar;
            case ConfigMap map:
                return MaskMap(map);
            case ConfigSequence seq:
                return MaskSequence(seq);
            default:
                return node;
        }
    }

    private ConfigMap MaskMap(ConfigMap map)
    {
        var entries = new List<KeyValuePair<string, ConfigNode>>(map.Count);
        foreach (var kvp in map)
        {
            if (SensitiveKeys.Contains(kvp.Key))
            {
                entries.Add(new KeyValuePair<string, ConfigNode>(kvp.Key, new ConfigScalar("---")));
            }
            else
            {
                entries.Add(new KeyValuePair<string, ConfigNode>(kvp.Key, Visit(kvp.Value)));
            }
        }
        return new ConfigMap(entries);
    }

    private ConfigSequence MaskSequence(ConfigSequence seq)
    {
        var items = new List<ConfigNode>(seq.Count);
        foreach (var item in seq)
        {
            if (item is ConfigMap map)
            {
                items.Add(MaskMap(map));
            }
            else if (item is ConfigSequence innerSeq)
            {
                items.Add(MaskSequence(innerSeq));
            }
            else
            {
                items.Add(item);
            }
        }
        return new ConfigSequence(items);
    }
}
