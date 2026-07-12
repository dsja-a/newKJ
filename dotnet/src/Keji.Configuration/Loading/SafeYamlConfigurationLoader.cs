using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using Keji.Configuration.Models;

namespace Keji.Configuration.Loading;

public class SafeYamlConfigurationLoader : IKejiConfigurationLoader
{
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

        try
        {
            var visitor = new SafeYamlVisitor(options, configPath);
            var root = visitor.Visit(yamlContent);

            if (root is not ConfigMap rootMap)
                throw new KejiConfigurationException(
                    $"Configuration file root must be a mapping, but got {root.NodeType}.", configPath);

            return new KejiConfigurationDocument(rootMap);
        }
        catch (YamlException ex)
        {
            throw new KejiConfigurationException(
                $"YAML parse error in '{configPath}' at line {ex.Start.Line}, column {ex.Start.Column}: {ex.Message}",
                configPath);
        }
        catch (KejiConfigurationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new KejiConfigurationException($"Unexpected error parsing configuration: {configPath}", ex);
        }
    }

    private sealed class SafeYamlVisitor
    {
        private readonly KejiConfigurationLoadOptions _options;
        private readonly string _configPath;
        private int _depth;
        private int _nodeCount;

        public SafeYamlVisitor(KejiConfigurationLoadOptions options, string configPath)
        {
            _options = options;
            _configPath = configPath;
        }

        public ConfigMap Visit(string yamlContent)
        {
            var parser = new Parser(new StringReader(yamlContent));

            parser.Consume<StreamStart>();
            parser.Consume<DocumentStart>();

            var result = ParseNode(parser);

            parser.Consume<DocumentEnd>();
            parser.Consume<StreamEnd>();

            if (result is not ConfigMap rootMap)
                throw new KejiConfigurationException(
                    "Root of configuration file must be a mapping.", _configPath);

            return rootMap;
        }

        private ConfigNode ParseNode(IParser parser)
        {
            if (_depth >= _options.MaxDepth)
                throw new KejiConfigurationException(
                    $"Configuration exceeds maximum depth of {_options.MaxDepth}.", _configPath);

            if (_nodeCount >= _options.MaxNodeCount)
                throw new KejiConfigurationException(
                    $"Configuration exceeds maximum node count of {_options.MaxNodeCount}.", _configPath);

            _depth++;

            try
            {
                if (parser.TryConsume<Scalar>(out var scalar))
                {
                    _nodeCount++;

                    if (!scalar.Tag.IsEmpty)
                        throw new KejiConfigurationException(
                            $"Custom YAML tag '{scalar.Tag}' on scalar is not allowed.", _configPath);

                    if (!scalar.Anchor.IsEmpty)
                        throw new KejiConfigurationException(
                            "YAML anchors are not allowed for security.", _configPath);

                    return new ConfigScalar(scalar.Value);
                }

                if (parser.TryConsume<MappingStart>(out var mappingStart))
                {
                    _nodeCount++;

                    if (!mappingStart.Tag.IsEmpty)
                        throw new KejiConfigurationException(
                            $"Custom YAML tag '{mappingStart.Tag}' on mapping is not allowed.", _configPath);

                    if (!mappingStart.Anchor.IsEmpty)
                        throw new KejiConfigurationException(
                            "YAML anchors are not allowed for security.", _configPath);

                    var entries = new List<KeyValuePair<string, ConfigNode>>();
                    var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    while (!parser.TryConsume<MappingEnd>(out _))
                    {
                        var keyNode = ParseNode(parser);

                        if (keyNode is not ConfigScalar keyScalar)
                            throw new KejiConfigurationException(
                                "Mapping keys must be scalar strings.", _configPath);

                        var key = keyScalar.Value ?? string.Empty;

                        if (!seenKeys.Add(key))
                            throw new KejiConfigurationException(
                                $"Duplicate key '{key}' (case-insensitive) in YAML mapping.", _configPath);

                        var value = ParseNode(parser);
                        entries.Add(new KeyValuePair<string, ConfigNode>(key, value));
                    }

                    return new ConfigMap(entries);
                }

                if (parser.TryConsume<SequenceStart>(out var seqStart))
                {
                    _nodeCount++;

                    if (!seqStart.Tag.IsEmpty)
                        throw new KejiConfigurationException(
                            $"Custom YAML tag '{seqStart.Tag}' on sequence is not allowed.", _configPath);

                    if (!seqStart.Anchor.IsEmpty)
                        throw new KejiConfigurationException(
                            "YAML anchors are not allowed for security.", _configPath);

                    var items = new List<ConfigNode>();

                    while (!parser.TryConsume<SequenceEnd>(out _))
                    {
                        items.Add(ParseNode(parser));
                    }

                    return new ConfigSequence(items);
                }

                if (parser.TryConsume<AnchorAlias>(out _))
                {
                    throw new KejiConfigurationException(
                        "YAML aliases (*) are not allowed for security.", _configPath);
                }

                throw new KejiConfigurationException(
                    "Unsupported YAML construct encountered.", _configPath);
            }
            finally
            {
                _depth--;
            }
        }
    }
}
