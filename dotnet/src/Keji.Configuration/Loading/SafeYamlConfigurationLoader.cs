using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using Keji.Configuration.Models;

namespace Keji.Configuration.Loading;

public class SafeYamlConfigurationLoader : IKejiConfigurationLoader
{
    private static readonly HashSet<Type> AllowedEvents = new()
    {
        typeof(StreamStart),
        typeof(StreamEnd),
        typeof(DocumentStart),
        typeof(DocumentEnd),
        typeof(Scalar),
        typeof(MappingStart),
        typeof(MappingEnd),
        typeof(SequenceStart),
        typeof(SequenceEnd),
        typeof(AnchorAlias),
    };

    public KejiConfigurationDocument Load(KejiConfigurationLoadOptions options)
    {
        var configPath = Path.Combine(options.ProjectRoot, options.ConfigFileName);

        if (!File.Exists(configPath))
        {
            if (options.RequireConfigFile)
                throw new KejiConfigurationException(
                    $"Configuration file not found: {configPath}");

            return new KejiConfigurationDocument(new ConfigMap(Array.Empty<KeyValuePair<string, ConfigNode>>()));
        }

        var fileInfo = new FileInfo(configPath);
        if (fileInfo.Length > options.MaxConfigFileBytes)
            throw new KejiConfigurationException(
                $"Configuration file exceeds maximum size of {options.MaxConfigFileBytes} bytes: {configPath}");

        string yamlContent;
        try
        {
            yamlContent = File.ReadAllText(configPath);
        }
        catch (Exception ex)
        {
            throw new KejiConfigurationException($"Failed to read configuration file: {configPath}", ex);
        }

        var visitor = new SafeYamlVisitor(options);
        var root = visitor.Visit(yamlContent);

        return new KejiConfigurationDocument(root);
    }

    private sealed class SafeYamlVisitor
    {
        private readonly KejiConfigurationLoadOptions _options;
        private int _depth;
        private int _nodeCount;
        private readonly HashSet<string> _seenAliases = new();

        public SafeYamlVisitor(KejiConfigurationLoadOptions options)
        {
            _options = options;
        }

        public ConfigMap Visit(string yamlContent)
        {
            var parser = new Parser(new StringReader(yamlContent));

            parser.Consume<StreamStart>();
            parser.Consume<DocumentStart>();

            var result = ParseMapping(parser);

            parser.Consume<DocumentEnd>();
            parser.Consume<StreamEnd>();

            return result;
        }

        private ConfigNode ParseNode(IParser parser)
        {
            if (_depth > _options.MaxDepth)
                throw new KejiConfigurationException(
                    $"Configuration exceeds maximum depth of {_options.MaxDepth}");

            if (_nodeCount > _options.MaxNodeCount)
                throw new KejiConfigurationException(
                    $"Configuration exceeds maximum node count of {_options.MaxNodeCount}");

            _depth++;

            try
            {
                if (parser.TryConsume<Scalar>(out var scalar))
                {
                    _nodeCount++;
                    return new ConfigScalar(scalar.Value);
                }

                if (parser.TryConsume<MappingStart>(out var mappingStart))
                {
                    _nodeCount++;

                    if (mappingStart.IsCanonical)
                        throw new KejiConfigurationException("YAML mapping with canonical format is not allowed.");

                    var entries = new List<KeyValuePair<string, ConfigNode>>();
                    var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    while (!parser.TryConsume<MappingEnd>(out _))
                    {
                        if (!parser.TryConsume<Scalar>(out var keyScalar))
                            throw new KejiConfigurationException(
                                "Mapping keys must be scalar strings.");

                        if (keyScalar.Style != ScalarStyle.Plain && keyScalar.Style != ScalarStyle.SingleQuoted && keyScalar.Style != ScalarStyle.DoubleQuoted)
                            throw new KejiConfigurationException(
                                $"Unsupported key style: {keyScalar.Style}");

                        var key = keyScalar.Value;

                        if (!seenKeys.Add(key))
                            throw new KejiConfigurationException(
                                $"Duplicate key '{key}' in YAML mapping.");

                        var value = ParseNode(parser);
                        entries.Add(new KeyValuePair<string, ConfigNode>(key, value));
                    }

                    return new ConfigMap(entries);
                }

                if (parser.TryConsume<SequenceStart>(out var seqStart))
                {
                    _nodeCount++;
                    var items = new List<ConfigNode>();

                    while (!parser.TryConsume<SequenceEnd>(out _))
                    {
                        items.Add(ParseNode(parser));
                    }

                    return new ConfigSequence(items);
                }

                if (parser.TryConsume<AnchorAlias>(out var alias))
                {
                    var aliasValue = alias.Value.Value ?? string.Empty;

                    if (!_seenAliases.Add(aliasValue))
                        throw new KejiConfigurationException(
                            $"Duplicate YAML alias '{aliasValue}' detected.");

                    _nodeCount++;

                    if (aliasValue.Length > 0)
                    {
                        return new ConfigScalar(null);
                    }

                    throw new KejiConfigurationException(
                        "YAML anchors/aliases are not supported for security.");
                }

                throw new KejiConfigurationException(
                    "Unsupported YAML construct encountered.");
            }
            finally
            {
                _depth--;
            }
        }

        private ConfigMap ParseMapping(IParser parser)
        {
            parser.Consume<MappingStart>();
            var entries = new List<KeyValuePair<string, ConfigNode>>();
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            while (!parser.TryConsume<MappingEnd>(out _))
            {
                var keyScalar = parser.Consume<Scalar>();
                var key = keyScalar.Value;

                if (!seenKeys.Add(key))
                    throw new KejiConfigurationException(
                        $"Duplicate key '{key}' in YAML mapping.");

                var value = ParseNode(parser);
                entries.Add(new KeyValuePair<string, ConfigNode>(key, value));
            }

            return new ConfigMap(entries);
        }
    }
}
