using System.Text.RegularExpressions;
using Keji.Configuration.Models;

namespace Keji.Configuration.Secrets;

public partial class EnvironmentReferenceResolver
{
    private static readonly Regex EnvVarRegex = EnvironmentVariablePattern();

    private readonly IEnvironmentValueSource _valueSource;
    private readonly bool _failOnMissing;

    public EnvironmentReferenceResolver(IEnvironmentValueSource valueSource, bool failOnMissing)
    {
        _valueSource = valueSource;
        _failOnMissing = failOnMissing;
    }

    public ConfigNode Resolve(ConfigNode node)
    {
        return Visit(node);
    }

    private ConfigNode Visit(ConfigNode node)
    {
        switch (node)
        {
            case ConfigScalar scalar:
                return ResolveScalar(scalar);
            case ConfigMap map:
                return ResolveMap(map);
            case ConfigSequence seq:
                return ResolveSequence(seq);
            default:
                return node;
        }
    }

    private ConfigNode ResolveScalar(ConfigScalar scalar)
    {
        if (scalar.Value is null)
            return scalar;

        var result = EnvVarRegex.Replace(scalar.Value, match =>
        {
            var varName = match.Groups[1].Value;
            var defaultValue = match.Groups[2].Success ? match.Groups[2].Value : null;

            var resolved = _valueSource.GetValue(varName);

            if (resolved is not null)
                return resolved;

            if (defaultValue is not null)
                return defaultValue;

            if (_failOnMissing)
                throw new KejiConfigurationException(
                    $"Environment variable '{varName}' is not set and no default value was provided.");

            return match.Value;
        });

        return ReferenceEquals(result, scalar.Value) ? scalar : new ConfigScalar(result);
    }

    private ConfigMap ResolveMap(ConfigMap map)
    {
        var entries = new List<KeyValuePair<string, ConfigNode>>(map.Count);
        foreach (var kvp in map)
        {
            entries.Add(new KeyValuePair<string, ConfigNode>(kvp.Key, Visit(kvp.Value)));
        }
        return new ConfigMap(entries);
    }

    private ConfigSequence ResolveSequence(ConfigSequence seq)
    {
        var items = new List<ConfigNode>(seq.Count);
        foreach (var item in seq)
        {
            items.Add(Visit(item));
        }
        return new ConfigSequence(items);
    }

    [GeneratedRegex(@"\$\{(.+?)(?:\|([^}]*))?\}", RegexOptions.Compiled)]
    private static partial Regex EnvironmentVariablePattern();
}
