using Keji.Configuration.Models;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Keji.Configuration.Loading;

public class SafeYamlConfigurationLoader : ISafeYamlConfigurationLoader
{
    public ConfigNode Load(KejiConfigurationLoadOptions options, string? overrideConfigPath = null)
    {
        var configPath = overrideConfigPath ?? Path.Combine(options.ProjectRoot, options.ConfigFileName);

        if (!File.Exists(configPath))
        {
            if (options.RequireConfigFile)
                throw NewSanitizedException("CONFIG_FILE_NOT_FOUND", "Configuration file not found.", configPath, null, null);

            return new ConfigMap(Array.Empty<KeyValuePair<string, ConfigNode>>());
        }

        var fileInfo = new FileInfo(configPath);
        if (fileInfo.Length > options.MaxConfigFileBytes)
            throw NewSanitizedException("CONFIG_FILE_TOO_LARGE",
                $"Configuration file exceeds maximum size.", configPath, null, null);

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
                throw NewSanitizedException("ROOT_NOT_MAPPING",
                    "Configuration root must be a mapping.", configPath, null, null);

            return rootMap;
        }
        catch (YamlException ex)
        {
            throw NewSanitizedException("YAML_PARSE_ERROR",
                $"Invalid YAML syntax at line {ex.Start.Line}, column {ex.Start.Column}.",
                configPath, (int)ex.Start.Line, (int)ex.Start.Column);
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

    private static KejiConfigurationException NewSanitizedException(
        string code, string message, string configPath, int? line, int? column)
    {
        var full = $"{code}: {message}";
        var ex = new KejiConfigurationException(full, configPath: null, filePath: configPath, lineNumber: line, columnNumber: column);
        ex.Data["ErrorCode"] = code;
        return ex;
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
                throw NewSanitizedException("ROOT_NOT_MAPPING",
                    "Configuration root must be a mapping.", _configPath, null, null);

            return rootMap;
        }

        private ConfigNode ParseNode(IParser parser)
        {
            if (_depth >= _options.MaxDepth)
                throw NewSanitizedException("MAX_DEPTH_EXCEEDED",
                    $"Configuration exceeds maximum depth.", _configPath, null, null);

            if (_nodeCount >= _options.MaxNodeCount)
                throw NewSanitizedException("MAX_NODE_COUNT_EXCEEDED",
                    $"Configuration exceeds maximum node count.", _configPath, null, null);

            _depth++;

            try
            {
                if (parser.TryConsume<Scalar>(out var scalar))
                {
                    _nodeCount++;

                    if (!scalar.Tag.IsEmpty)
                        throw NewSanitizedException("CUSTOM_TAG",
                            "Custom YAML tags are not allowed.", _configPath, null, null);

                    if (!scalar.Anchor.IsEmpty)
                        throw NewSanitizedException("ANCHOR_NOT_ALLOWED",
                            "YAML anchors are not allowed.", _configPath, null, null);

                    return new ConfigScalar(scalar.Value);
                }

                if (parser.TryConsume<MappingStart>(out var mappingStart))
                {
                    _nodeCount++;

                    if (!mappingStart.Tag.IsEmpty)
                        throw NewSanitizedException("CUSTOM_TAG",
                            "Custom YAML tags are not allowed.", _configPath, null, null);

                    if (!mappingStart.Anchor.IsEmpty)
                        throw NewSanitizedException("ANCHOR_NOT_ALLOWED",
                            "YAML anchors are not allowed.", _configPath, null, null);

                    var entries = new List<KeyValuePair<string, ConfigNode>>();
                    var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    while (!parser.TryConsume<MappingEnd>(out _))
                    {
                        var keyNode = ParseNode(parser);

                        if (keyNode is not ConfigScalar keyScalar)
                            throw NewSanitizedException("INVALID_KEY_TYPE",
                                "Mapping keys must be scalar strings.", _configPath, null, null);

                        var key = keyScalar.Value ?? string.Empty;

                        if (!seenKeys.Add(key))
                            throw NewSanitizedException("DUPLICATE_KEY",
                                "Duplicate key in YAML mapping.", _configPath, null, null);

                        var value = ParseNode(parser);
                        entries.Add(new KeyValuePair<string, ConfigNode>(key, value));
                    }

                    return new ConfigMap(entries);
                }

                if (parser.TryConsume<SequenceStart>(out var seqStart))
                {
                    _nodeCount++;

                    if (!seqStart.Tag.IsEmpty)
                        throw NewSanitizedException("CUSTOM_TAG",
                            "Custom YAML tags are not allowed.", _configPath, null, null);

                    if (!seqStart.Anchor.IsEmpty)
                        throw NewSanitizedException("ANCHOR_NOT_ALLOWED",
                            "YAML anchors are not allowed.", _configPath, null, null);

                    var items = new List<ConfigNode>();

                    while (!parser.TryConsume<SequenceEnd>(out _))
                    {
                        items.Add(ParseNode(parser));
                    }

                    return new ConfigSequence(items);
                }

                if (parser.TryConsume<AnchorAlias>(out _))
                {
                    throw NewSanitizedException("ALIAS_NOT_ALLOWED",
                        "YAML aliases are not allowed.", _configPath, null, null);
                }

                throw NewSanitizedException("UNSUPPORTED_CONSTRUCT",
                    "Unsupported YAML construct.", _configPath, null, null);
            }
            finally
            {
                _depth--;
            }
        }
    }
}
