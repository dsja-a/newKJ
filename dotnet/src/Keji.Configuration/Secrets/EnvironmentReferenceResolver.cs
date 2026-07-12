using System.Text.RegularExpressions;
using Keji.Configuration.Models;

namespace Keji.Configuration.Secrets;

public partial class EnvironmentReferenceResolver
{
    private static readonly Regex EnvVarRegex = EnvironmentVariablePattern();

    private readonly IEnvironmentValueSource _valueSource;
    private readonly bool _failOnMissing;
    private readonly List<EnvironmentResolutionDiagnostic> _diagnostics;

    public EnvironmentReferenceResolver(IEnvironmentValueSource valueSource, bool failOnMissing)
    {
        _valueSource = valueSource;
        _failOnMissing = failOnMissing;
        _diagnostics = new List<EnvironmentResolutionDiagnostic>();
    }

    public EnvironmentResolutionResult Resolve(ConfigNode node)
    {
        var resolved = Visit(node, string.Empty);
        return new EnvironmentResolutionResult(resolved, _diagnostics.AsReadOnly());
    }

    private ConfigNode Visit(ConfigNode node, string currentPath)
    {
        switch (node)
        {
            case ConfigScalar scalar:
                return ResolveScalar(scalar, currentPath);
            case ConfigMap map:
                return ResolveMap(map, currentPath);
            case ConfigSequence seq:
                return ResolveSequence(seq, currentPath);
            default:
                return node;
        }
    }

    private ConfigNode ResolveScalar(ConfigScalar scalar, string configPath)
    {
        if (scalar.Value is null)
            return scalar;

        var match = EnvVarRegex.Match(scalar.Value);
        if (!match.Success)
            return scalar;

        var varName = match.Groups[1].Value;
        var resolved = _valueSource.GetValue(varName);

        if (resolved is not null)
            return new ConfigScalar(resolved);

        if (_failOnMissing)
        {
            throw new KejiConfigurationException(
                $"Environment variable '{varName}' is required but not set.",
                configPath.Length > 0 ? configPath : null);
        }

        _diagnostics.Add(new EnvironmentResolutionDiagnostic(
            configPath,
            varName,
            "ENV_MISSING"));
        return new ConfigScalar(string.Empty);
    }

    private ConfigMap ResolveMap(ConfigMap map, string currentPath)
    {
        var entries = new List<KeyValuePair<string, ConfigNode>>(map.Count);
        foreach (var kvp in map)
        {
            var childPath = currentPath.Length == 0 ? kvp.Key : $"{currentPath}.{kvp.Key}";
            entries.Add(new KeyValuePair<string, ConfigNode>(kvp.Key, Visit(kvp.Value, childPath)));
        }
        return new ConfigMap(entries);
    }

    private ConfigSequence ResolveSequence(ConfigSequence seq, string currentPath)
    {
        var items = new List<ConfigNode>(seq.Count);
        for (int i = 0; i < seq.Count; i++)
        {
            var childPath = $"{currentPath}[{i}]";
            items.Add(Visit(seq[i], childPath));
        }
        return new ConfigSequence(items);
    }

    [GeneratedRegex(@"^\$\{([A-Za-z_][A-Za-z0-9_]*)\}$", RegexOptions.Compiled)]
    private static partial Regex EnvironmentVariablePattern();
}
