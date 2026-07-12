using System.Collections.Frozen;
using Keji.Configuration.Models;

namespace Keji.Configuration.Secrets;

public class SecretMasker : ISecretMasker
{
    private static readonly FrozenSet<string> SensitiveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "api_key",
        "apikey",
        "app_secret",
        "client_secret",
        "secret",
        "password",
        "token",
        "access_token",
        "refresh_token",
        "verification_token",
        "encrypt_key",
        "work_secret",
        "jwt_secret",
        "private_key",
        "connection_string",
    }.ToFrozenSet();

    private const string MaskValue = "***";

    public ConfigNode Mask(ConfigNode node)
    {
        return Visit(node);
    }

    public IReadOnlyDictionary<string, object?> Mask(IReadOnlyDictionary<string, object?> dictionary)
    {
        var result = new Dictionary<string, object?>(dictionary.Count, StringComparer.Ordinal);
        foreach (var kvp in dictionary)
        {
            if (SensitiveKeys.Contains(kvp.Key))
            {
                result[kvp.Key] = MaskValue;
            }
            else if (kvp.Value is IReadOnlyDictionary<string, object?> nestedDict)
            {
                result[kvp.Key] = Mask(nestedDict);
            }
            else if (kvp.Value is IEnumerable<object?> list)
            {
                result[kvp.Key] = Mask(list);
            }
            else
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(result);
    }

    public IReadOnlyList<object?> Mask(IEnumerable<object?> sequence)
    {
        var result = new List<object?>();
        foreach (var item in sequence)
        {
            if (item is IReadOnlyDictionary<string, object?> nestedDict)
            {
                result.Add(Mask(nestedDict));
            }
            else if (item is IEnumerable<object?> list)
            {
                result.Add(Mask(list));
            }
            else
            {
                result.Add(item);
            }
        }
        return result.AsReadOnly();
    }

    public static ApiKeySettingsMask MaskApiKeyForSettings(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return new ApiKeySettingsMask(false, string.Empty);

        return new ApiKeySettingsMask(true, "***");
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
                entries.Add(new KeyValuePair<string, ConfigNode>(kvp.Key, new ConfigScalar(MaskValue)));
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
