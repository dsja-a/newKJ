using System.Collections.ObjectModel;
using Keji.Configuration.Loading;
using Keji.Configuration.Models;
using Keji.Configuration.Secrets;

namespace Keji.Security.Tests;

public class ConfigurationTests
{
    [Fact]
    public void ConfigScalar_CreateWithValue_ReturnsValue()
    {
        var scalar = new ConfigScalar("hello");
        Assert.Equal("hello", scalar.Value);
    }

    [Fact]
    public void ConfigScalar_CreateWithNull_ReturnsNull()
    {
        var scalar = new ConfigScalar(null);
        Assert.Null(scalar.Value);
    }

    [Fact]
    public void ConfigScalar_NodeType_IsScalar()
    {
        var scalar = new ConfigScalar("x");
        Assert.Equal(ConfigNodeType.Scalar, scalar.NodeType);
    }

    [Fact]
    public void ConfigMap_CreateWithEntries_CanIndex()
    {
        var map = new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("key1", new ConfigScalar("value1")),
        });

        Assert.Equal("value1", ((ConfigScalar)map["key1"]).Value);
    }

    [Fact]
    public void ConfigMap_CaseInsensitiveKeys()
    {
        var map = new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("KEY", new ConfigScalar("value")),
        });

        Assert.Equal("value", ((ConfigScalar)map["key"]).Value);
    }

    [Fact]
    public void ConfigMap_TryGetValue_ExistingKey_ReturnsTrue()
    {
        var map = new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("a", new ConfigScalar("1")),
        });

        Assert.True(map.TryGetValue("a", out var val));
        Assert.Equal("1", ((ConfigScalar)val).Value);
    }

    [Fact]
    public void ConfigMap_TryGetValue_MissingKey_ReturnsFalse()
    {
        var map = new ConfigMap(Array.Empty<KeyValuePair<string, ConfigNode>>());
        Assert.False(map.TryGetValue("missing", out _));
    }

    [Fact]
    public void ConfigMap_NavigateNestedMap()
    {
        var inner = new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("b", new ConfigScalar("2")),
        });
        var outer = new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("a", inner),
        });

        Assert.True(outer.TryGetValue("a", out var aNode));
        var aMap = aNode as ConfigMap;
        Assert.NotNull(aMap);
        Assert.True(aMap.TryGetValue("b", out var bNode));
        Assert.Equal("2", ((ConfigScalar)bNode).Value);
    }

    [Fact]
    public void ConfigSequence_Create_ReturnsItems()
    {
        var seq = new ConfigSequence(new ConfigNode[]
        {
            new ConfigScalar("a"),
            new ConfigScalar("b"),
        });

        Assert.Equal(2, seq.Count);
        Assert.Equal("a", ((ConfigScalar)seq[0]).Value);
        Assert.Equal("b", ((ConfigScalar)seq[1]).Value);
    }

    [Fact]
    public void ConfigSequence_Enumerate_Works()
    {
        var seq = new ConfigSequence(new ConfigNode[]
        {
            new ConfigScalar("x"),
        });

        var count = 0;
        foreach (var item in seq)
        {
            Assert.Equal("x", ((ConfigScalar)item).Value);
            count++;
        }
        Assert.Equal(1, count);
    }

    [Fact]
    public void ConfigNode_AsScalar_ReturnsTyped()
    {
        ConfigNode node = new ConfigScalar("test");
        var scalar = node.AsScalar();
        Assert.Equal("test", scalar.Value);
    }

    [Fact]
    public void ConfigNode_AsMap_ReturnsTyped()
    {
        ConfigNode node = new ConfigMap(Array.Empty<KeyValuePair<string, ConfigNode>>());
        var map = node.AsMap();
        Assert.NotNull(map);
    }

    [Fact]
    public void ConfigNode_AsSequence_ReturnsTyped()
    {
        ConfigNode node = new ConfigSequence(Array.Empty<ConfigNode>());
        var seq = node.AsSequence();
        Assert.NotNull(seq);
    }

    [Fact]
    public void KejiConfigurationDocument_GetOptionalString_Existing_ReturnsValue()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("key", new ConfigScalar("value")),
        }));

        Assert.Equal("value", doc.GetOptionalString("key"));
    }

    [Fact]
    public void KejiConfigurationDocument_GetOptionalString_Missing_ReturnsNull()
    {
        var doc = new KejiConfigurationDocument(
            new ConfigMap(Array.Empty<KeyValuePair<string, ConfigNode>>()));

        Assert.Null(doc.GetOptionalString("missing.key"));
    }

    [Fact]
    public void KejiConfigurationDocument_GetRequiredString_Existing_ReturnsValue()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("key", new ConfigScalar("value")),
        }));

        Assert.Equal("value", doc.GetRequiredString("key"));
    }

    [Fact]
    public void KejiConfigurationDocument_GetRequiredString_Missing_Throws()
    {
        var doc = new KejiConfigurationDocument(
            new ConfigMap(Array.Empty<KeyValuePair<string, ConfigNode>>()));

        Assert.Throws<KejiConfigurationException>(() => doc.GetRequiredString("missing"));
    }

    [Fact]
    public void KejiConfigurationDocument_GetBoolean_Default_ReturnsDefault()
    {
        var doc = new KejiConfigurationDocument(
            new ConfigMap(Array.Empty<KeyValuePair<string, ConfigNode>>()));

        Assert.True(doc.GetBoolean("missing", true));
    }

    [Fact]
    public void KejiConfigurationDocument_GetBoolean_Parsed_ReturnsValue()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("flag", new ConfigScalar("true")),
        }));

        Assert.True(doc.GetBoolean("flag", false));
    }

    [Fact]
    public void KejiConfigurationDocument_GetInt32_Default_ReturnsDefault()
    {
        var doc = new KejiConfigurationDocument(
            new ConfigMap(Array.Empty<KeyValuePair<string, ConfigNode>>()));

        Assert.Equal(42, doc.GetInt32("missing", 42));
    }

    [Fact]
    public void KejiConfigurationDocument_GetInt32_Parsed_ReturnsValue()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("port", new ConfigScalar("8080")),
        }));

        Assert.Equal(8080, doc.GetInt32("port", 0));
    }

    [Fact]
    public void KejiConfigurationDocument_GetStringList_Existing_ReturnsList()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("items", new ConfigSequence(new ConfigNode[]
            {
                new ConfigScalar("a"),
                new ConfigScalar("b"),
            })),
        }));

        var list = doc.GetStringList("items");
        Assert.Equal(2, list.Count);
        Assert.Contains("a", list);
        Assert.Contains("b", list);
    }

    [Fact]
    public void KejiConfigurationDocument_GetStringList_Missing_ReturnsEmpty()
    {
        var doc = new KejiConfigurationDocument(
            new ConfigMap(Array.Empty<KeyValuePair<string, ConfigNode>>()));

        Assert.Empty(doc.GetStringList("missing"));
    }

    [Fact]
    public void KejiConfigurationDocument_TryGetNode_DottedPath_ReturnsNode()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("a", new ConfigMap(new[]
            {
                new KeyValuePair<string, ConfigNode>("b", new ConfigScalar("c")),
            })),
        }));

        Assert.True(doc.TryGetNode("a.b", out var node));
        Assert.Equal("c", ((ConfigScalar)node!).Value);
    }

    [Fact]
    public void KejiConfigurationDocument_GetInt32_NotInt_Throws()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("val", new ConfigScalar("not_a_number")),
        }));

        Assert.Throws<KejiConfigurationException>(() => doc.GetInt32("val", 0));
    }

    [Fact]
    public void KejiConfigurationDocument_GetBoolean_NotBool_Throws()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("flag", new ConfigScalar("not_bool")),
        }));

        Assert.Throws<KejiConfigurationException>(() => doc.GetBoolean("flag", false));
    }

    [Fact]
    public void KejiConfigurationDocument_GetStringList_NotSequence_Throws()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("item", new ConfigScalar("not_a_list")),
        }));

        Assert.Throws<KejiConfigurationException>(() => doc.GetStringList("item"));
    }

    [Fact]
    public void KejiConfigurationLoadOptions_Defaults()
    {
        var options = new KejiConfigurationLoadOptions();
        Assert.Equal(1 * 1024 * 1024, options.MaxConfigFileBytes);
        Assert.Equal(1 * 1024 * 1024, options.MaxDotEnvFileBytes);
        Assert.Equal(16384, options.MaxDotEnvLineLength);
        Assert.Equal(32, options.MaxDepth);
        Assert.Equal(10000, options.MaxNodeCount);
        Assert.True(options.RequireConfigFile);
        Assert.True(options.FailOnMissingEnvironmentVariable);
        Assert.Equal("config.yaml", options.ConfigFileName);
        Assert.Equal(".env", options.DotEnvFileName);
    }

    [Fact]
    public void SafeYamlConfigurationLoader_LoadBasicYaml()
    {
        using var tempDir = new TempDirectory();
        var yamlPath = Path.Combine(tempDir.Path, "config.yaml");
        File.WriteAllText(yamlPath, "key: value\nnumber: 42\nflag: true\n");

        var loader = new SafeYamlConfigurationLoader();
        var doc = loader.Load(new KejiConfigurationLoadOptions
        {
            ProjectRoot = tempDir.Path,
            RequireConfigFile = true,
        });

        Assert.Equal("value", doc.GetOptionalString("key"));
        Assert.Equal("42", doc.GetOptionalString("number"));
        Assert.Equal("true", doc.GetOptionalString("flag"));
    }

    [Fact]
    public void SafeYamlConfigurationLoader_LoadNestedYaml()
    {
        using var tempDir = new TempDirectory();
        var yamlPath = Path.Combine(tempDir.Path, "config.yaml");
        File.WriteAllText(yamlPath, "app:\n  name: keji\n  port: 8080\n");

        var loader = new SafeYamlConfigurationLoader();
        var doc = loader.Load(new KejiConfigurationLoadOptions
        {
            ProjectRoot = tempDir.Path,
            RequireConfigFile = true,
        });

        Assert.Equal("keji", doc.GetOptionalString("app.name"));
        Assert.Equal("8080", doc.GetOptionalString("app.port"));
    }

    [Fact]
    public void SafeYamlConfigurationLoader_LoadSequenceYaml()
    {
        using var tempDir = new TempDirectory();
        var yamlPath = Path.Combine(tempDir.Path, "config.yaml");
        File.WriteAllText(yamlPath, "items:\n  - a\n  - b\n  - c\n");

        var loader = new SafeYamlConfigurationLoader();
        var doc = loader.Load(new KejiConfigurationLoadOptions
        {
            ProjectRoot = tempDir.Path,
            RequireConfigFile = true,
        });

        var list = doc.GetStringList("items");
        Assert.Equal(3, list.Count);
        Assert.Equal("a", list[0]);
        Assert.Equal("b", list[1]);
        Assert.Equal("c", list[2]);
    }

    [Fact]
    public void SafeYamlConfigurationLoader_MissingFile_Throws()
    {
        using var tempDir = new TempDirectory();
        var loader = new SafeYamlConfigurationLoader();

        Assert.Throws<KejiConfigurationException>(() =>
            loader.Load(new KejiConfigurationLoadOptions
            {
                ProjectRoot = tempDir.Path,
                RequireConfigFile = true,
            }));
    }

    [Fact]
    public void SafeYamlConfigurationLoader_MissingFile_Optional_ReturnsEmpty()
    {
        using var tempDir = new TempDirectory();
        var loader = new SafeYamlConfigurationLoader();

        var doc = loader.Load(new KejiConfigurationLoadOptions
        {
            ProjectRoot = tempDir.Path,
            RequireConfigFile = false,
        });

        Assert.NotNull(doc);
    }

    [Fact]
    public void SafeYamlConfigurationLoader_DuplicateKeys_Throws()
    {
        using var tempDir = new TempDirectory();
        var yamlPath = Path.Combine(tempDir.Path, "config.yaml");
        File.WriteAllText(yamlPath, "key: first\nkey: second\n");

        var loader = new SafeYamlConfigurationLoader();

        Assert.Throws<KejiConfigurationException>(() =>
            loader.Load(new KejiConfigurationLoadOptions
            {
                ProjectRoot = tempDir.Path,
                RequireConfigFile = true,
            }));
    }

    [Fact]
    public void SafeYamlConfigurationLoader_CustomScalarTag_Rejected()
    {
        using var tempDir = new TempDirectory();
        var yamlPath = Path.Combine(tempDir.Path, "config.yaml");
        File.WriteAllText(yamlPath, "key: !!str value\n");

        var loader = new SafeYamlConfigurationLoader();

        var ex = Assert.Throws<KejiConfigurationException>(() =>
            loader.Load(new KejiConfigurationLoadOptions
            {
                ProjectRoot = tempDir.Path,
                RequireConfigFile = true,
            }));

        Assert.Contains("tag", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SafeYamlConfigurationLoader_CustomMappingTag_Rejected()
    {
        using var tempDir = new TempDirectory();
        var yamlPath = Path.Combine(tempDir.Path, "config.yaml");
        File.WriteAllText(yamlPath, "!!map\n  key: value\n");

        var loader = new SafeYamlConfigurationLoader();

        var ex = Assert.Throws<KejiConfigurationException>(() =>
            loader.Load(new KejiConfigurationLoadOptions
            {
                ProjectRoot = tempDir.Path,
                RequireConfigFile = true,
            }));

        Assert.Contains("tag", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SafeYamlConfigurationLoader_CustomSequenceTag_Rejected()
    {
        using var tempDir = new TempDirectory();
        var yamlPath = Path.Combine(tempDir.Path, "config.yaml");
        File.WriteAllText(yamlPath, "!!seq\n  - a\n  - b\n");

        var loader = new SafeYamlConfigurationLoader();

        var ex = Assert.Throws<KejiConfigurationException>(() =>
            loader.Load(new KejiConfigurationLoadOptions
            {
                ProjectRoot = tempDir.Path,
                RequireConfigFile = true,
            }));

        Assert.Contains("tag", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SafeYamlConfigurationLoader_AnchorDeclaration_Rejected()
    {
        using var tempDir = new TempDirectory();
        var yamlPath = Path.Combine(tempDir.Path, "config.yaml");
        File.WriteAllText(yamlPath, "defaults: &d\n  key: value\n");

        var loader = new SafeYamlConfigurationLoader();

        var ex = Assert.Throws<KejiConfigurationException>(() =>
            loader.Load(new KejiConfigurationLoadOptions
            {
                ProjectRoot = tempDir.Path,
                RequireConfigFile = true,
            }));

        Assert.Contains("anchor", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SafeYamlConfigurationLoader_Alias_Rejected()
    {
        using var tempDir = new TempDirectory();
        var yamlPath = Path.Combine(tempDir.Path, "config.yaml");
        File.WriteAllText(yamlPath, "key: *some_alias\n");

        var loader = new SafeYamlConfigurationLoader();

        var ex = Assert.Throws<KejiConfigurationException>(() =>
            loader.Load(new KejiConfigurationLoadOptions
            {
                ProjectRoot = tempDir.Path,
                RequireConfigFile = true,
            }));

        Assert.Contains("alias", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SafeYamlConfigurationLoader_MalformedYaml_WrapsException()
    {
        using var tempDir = new TempDirectory();
        var yamlPath = Path.Combine(tempDir.Path, "config.yaml");
        File.WriteAllText(yamlPath, "key: value\nunbalanced: [\n");

        var loader = new SafeYamlConfigurationLoader();

        var ex = Assert.Throws<KejiConfigurationException>(() =>
            loader.Load(new KejiConfigurationLoadOptions
            {
                ProjectRoot = tempDir.Path,
                RequireConfigFile = true,
            }));

        Assert.NotNull(ex.Message);
    }

    [Fact]
    public void SafeYamlConfigurationLoader_ExceptionDoesNotContainSecrets()
    {
        using var tempDir = new TempDirectory();
        var yamlPath = Path.Combine(tempDir.Path, "config.yaml");
        File.WriteAllText(yamlPath, "password: my_super_secret_value\nkey: *missing_alias\n");

        var loader = new SafeYamlConfigurationLoader();

        var ex = Assert.Throws<KejiConfigurationException>(() =>
            loader.Load(new KejiConfigurationLoadOptions
            {
                ProjectRoot = tempDir.Path,
                RequireConfigFile = true,
            }));

        Assert.DoesNotContain("my_super_secret_value", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SafeYamlConfigurationLoader_RootNotMapping_Throws()
    {
        using var tempDir = new TempDirectory();
        var yamlPath = Path.Combine(tempDir.Path, "config.yaml");
        File.WriteAllText(yamlPath, "[1, 2, 3]\n");

        var loader = new SafeYamlConfigurationLoader();

        Assert.Throws<KejiConfigurationException>(() =>
            loader.Load(new KejiConfigurationLoadOptions
            {
                ProjectRoot = tempDir.Path,
                RequireConfigFile = true,
            }));
    }

    [Fact]
    public void SafeYamlConfigurationLoader_ExceedsMaxDepth_Throws()
    {
        using var tempDir = new TempDirectory();
        var yamlPath = Path.Combine(tempDir.Path, "config.yaml");
        File.WriteAllText(yamlPath, BuildDeepYaml(40));

        var loader = new SafeYamlConfigurationLoader();

        Assert.Throws<KejiConfigurationException>(() =>
            loader.Load(new KejiConfigurationLoadOptions
            {
                ProjectRoot = tempDir.Path,
                RequireConfigFile = true,
                MaxDepth = 10,
            }));
    }

    [Fact]
    public void SafeYamlConfigurationLoader_ExceedsMaxNodeCount_Throws()
    {
        using var tempDir = new TempDirectory();
        var yamlPath = Path.Combine(tempDir.Path, "config.yaml");
        var manyKeys = string.Join("\n", Enumerable.Range(0, 200).Select(i => $"key{i}: value{i}"));
        File.WriteAllText(yamlPath, manyKeys);

        var loader = new SafeYamlConfigurationLoader();

        Assert.Throws<KejiConfigurationException>(() =>
            loader.Load(new KejiConfigurationLoadOptions
            {
                ProjectRoot = tempDir.Path,
                RequireConfigFile = true,
                MaxNodeCount = 50,
            }));
    }

    [Fact]
    public void SafeYamlConfigurationLoader_ExceedsMaxSize_Throws()
    {
        using var tempDir = new TempDirectory();
        var yamlPath = Path.Combine(tempDir.Path, "config.yaml");
        File.WriteAllText(yamlPath, new string('x', 2000));

        var loader = new SafeYamlConfigurationLoader();

        Assert.Throws<KejiConfigurationException>(() =>
            loader.Load(new KejiConfigurationLoadOptions
            {
                ProjectRoot = tempDir.Path,
                RequireConfigFile = true,
                MaxConfigFileBytes = 100,
            }));
    }

    [Fact]
    public void EnvironmentReferenceResolver_ResolvesSimpleVar()
    {
        var source = new TestEnvironmentValueSource(new Dictionary<string, string>
        {
            { "MY_VAR", "resolved_value" },
        });
        var resolver = new EnvironmentReferenceResolver(source, failOnMissing: true);

        var result = resolver.Resolve(new ConfigScalar("${MY_VAR}"));

        Assert.Equal("resolved_value", ((ConfigScalar)result.Root).Value);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void EnvironmentReferenceResolver_PrefixInterpolation_NotResolved()
    {
        var source = new TestEnvironmentValueSource(new Dictionary<string, string>
        {
            { "NAME", "World" },
        });
        var resolver = new EnvironmentReferenceResolver(source, failOnMissing: true);

        var result = resolver.Resolve(new ConfigScalar("prefix-${NAME}"));

        Assert.Equal("prefix-${NAME}", ((ConfigScalar)result.Root).Value);
    }

    [Fact]
    public void EnvironmentReferenceResolver_SuffixInterpolation_NotResolved()
    {
        var source = new TestEnvironmentValueSource(new Dictionary<string, string>
        {
            { "NAME", "World" },
        });
        var resolver = new EnvironmentReferenceResolver(source, failOnMissing: true);

        var result = resolver.Resolve(new ConfigScalar("${NAME}-suffix"));

        Assert.Equal("${NAME}-suffix", ((ConfigScalar)result.Root).Value);
    }

    [Fact]
    public void EnvironmentReferenceResolver_DefaultSyntax_NotResolved()
    {
        var source = new TestEnvironmentValueSource(new Dictionary<string, string>());
        var resolver = new EnvironmentReferenceResolver(source, failOnMissing: true);

        var result = resolver.Resolve(new ConfigScalar("${MISSING|default_val}"));

        Assert.Equal("${MISSING|default_val}", ((ConfigScalar)result.Root).Value);
    }

    [Fact]
    public void EnvironmentReferenceResolver_InvalidName_NotResolved()
    {
        var source = new TestEnvironmentValueSource(new Dictionary<string, string>());
        var resolver = new EnvironmentReferenceResolver(source, failOnMissing: true);

        var result = resolver.Resolve(new ConfigScalar("${1BAD}"));

        Assert.Equal("${1BAD}", ((ConfigScalar)result.Root).Value);
    }

    [Fact]
    public void EnvironmentReferenceResolver_MissingVar_StrictMode_Throws()
    {
        var source = new TestEnvironmentValueSource(new Dictionary<string, string>());
        var resolver = new EnvironmentReferenceResolver(source, failOnMissing: true);

        var ex = Assert.Throws<KejiConfigurationException>(() =>
            resolver.Resolve(new ConfigScalar("${MISSING}")));

        Assert.Contains("MISSING", ex.Message);
    }

    [Fact]
    public void EnvironmentReferenceResolver_MissingVar_NonStrict_ReturnsEmptyAndDiagnostic()
    {
        var source = new TestEnvironmentValueSource(new Dictionary<string, string>());
        var resolver = new EnvironmentReferenceResolver(source, failOnMissing: false);

        var result = resolver.Resolve(new ConfigScalar("${MISSING}"));

        Assert.Equal(string.Empty, ((ConfigScalar)result.Root).Value);
        Assert.Single(result.Diagnostics);
        Assert.Equal("MISSING", result.Diagnostics[0].EnvironmentVariableName);
        Assert.Equal("ENV_MISSING", result.Diagnostics[0].DiagnosticCode);
    }

    [Fact]
    public void EnvironmentReferenceResolver_DiagnosticsDoNotContainSecrets()
    {
        var source = new TestEnvironmentValueSource(new Dictionary<string, string>());
        var resolver = new EnvironmentReferenceResolver(source, failOnMissing: false);

        var result = resolver.Resolve(new ConfigScalar("${SECRET_VAR}"));

        foreach (var d in result.Diagnostics)
        {
            Assert.DoesNotContain("SECRET_VAR", d.ConfigPath);
            Assert.Equal("SECRET_VAR", d.EnvironmentVariableName);
        }
    }

    [Fact]
    public void EnvironmentReferenceResolver_TracksDottedPath()
    {
        var source = new TestEnvironmentValueSource(new Dictionary<string, string>());
        var resolver = new EnvironmentReferenceResolver(source, failOnMissing: false);

        var map = new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("app", new ConfigMap(new[]
            {
                new KeyValuePair<string, ConfigNode>("secret_key", new ConfigScalar("${MISSING}")),
            })),
        });

        var result = resolver.Resolve(map);
        Assert.Single(result.Diagnostics);
        Assert.Equal("app.secret_key", result.Diagnostics[0].ConfigPath);
    }

    [Fact]
    public void EnvironmentReferenceResolver_ResolvesInMap()
    {
        var source = new TestEnvironmentValueSource(new Dictionary<string, string>
        {
            { "PORT", "3000" },
        });
        var resolver = new EnvironmentReferenceResolver(source, failOnMissing: true);

        var map = new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("port", new ConfigScalar("${PORT}")),
        });

        var result = resolver.Resolve(map);
        var resolvedMap = result.Root as ConfigMap;
        Assert.NotNull(resolvedMap);
        Assert.Equal("3000", ((ConfigScalar)resolvedMap["port"]).Value);
    }

    [Fact]
    public void EnvironmentReferenceResolver_ResolvesInSequence()
    {
        var source = new TestEnvironmentValueSource(new Dictionary<string, string>
        {
            { "HOST", "localhost" },
        });
        var resolver = new EnvironmentReferenceResolver(source, failOnMissing: true);

        var seq = new ConfigSequence(new ConfigNode[]
        {
            new ConfigScalar("${HOST}"),
        });

        var result = resolver.Resolve(seq);
        var resolvedSeq = result.Root as ConfigSequence;
        Assert.NotNull(resolvedSeq);
        Assert.Equal("localhost", ((ConfigScalar)resolvedSeq[0]).Value);
    }

    [Fact]
    public void EnvironmentReferenceResolver_NoVarNoChange()
    {
        var source = new TestEnvironmentValueSource(new Dictionary<string, string>());
        var resolver = new EnvironmentReferenceResolver(source, failOnMissing: true);

        var result = resolver.Resolve(new ConfigScalar("plain text"));

        Assert.Equal("plain text", ((ConfigScalar)result.Root).Value);
    }

    [Fact]
    public void ProcessEnvironmentValueSource_ReturnsValue()
    {
        var key = "KJ_TEST_VAR_" + Guid.NewGuid().ToString("N")[..8];
        Environment.SetEnvironmentVariable(key, "process_val");

        try
        {
            var source = new ProcessEnvironmentValueSource();
            Assert.Equal("process_val", source.GetValue(key));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void ProcessEnvironmentValueSource_Missing_ReturnsNull()
    {
        var source = new ProcessEnvironmentValueSource();
        Assert.Null(source.GetValue("KJ_NONEXISTENT_" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public void CompositeEnvironmentValueSource_FirstSourceWins()
    {
        var first = new TestEnvironmentValueSource(new Dictionary<string, string>
        {
            { "VAR", "from_first" },
        });
        var second = new TestEnvironmentValueSource(new Dictionary<string, string>
        {
            { "VAR", "from_second" },
        });

        var composite = new CompositeEnvironmentValueSource(first, second);
        Assert.Equal("from_first", composite.GetValue("VAR"));
    }

    [Fact]
    public void CompositeEnvironmentValueSource_FallbackToSecond()
    {
        var first = new TestEnvironmentValueSource(new Dictionary<string, string>());
        var second = new TestEnvironmentValueSource(new Dictionary<string, string>
        {
            { "VAR", "from_second" },
        });

        var composite = new CompositeEnvironmentValueSource(first, second);
        Assert.Equal("from_second", composite.GetValue("VAR"));
    }

    [Fact]
    public void CompositeEnvironmentValueSource_AllMissing_ReturnsNull()
    {
        var composite = new CompositeEnvironmentValueSource(
            new TestEnvironmentValueSource(new Dictionary<string, string>()),
            new TestEnvironmentValueSource(new Dictionary<string, string>()));

        Assert.Null(composite.GetValue("MISSING"));
    }

    [Fact]
    public void DotEnvStore_ReadsFile()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllLines(envPath, new[]
        {
            "KEY=value",
            "EMPTY=",
            "# comment",
        });

        var store = new DotEnvStore(envPath);
        Assert.Equal("value", store.GetValue("KEY"));
        Assert.Equal("", store.GetValue("EMPTY"));
    }

    [Fact]
    public void DotEnvStore_CaseInsensitiveKeys()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllText(envPath, "API_KEY=secret\n");

        var store = new DotEnvStore(envPath);
        Assert.Equal("secret", store.GetValue("api_key"));
        Assert.Equal("secret", store.GetValue("API_KEY"));
    }

    [Fact]
    public void DotEnvStore_MissingFile_ReturnsEmpty()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");

        var store = new DotEnvStore(envPath);
        Assert.Null(store.GetValue("ANY"));
    }

    [Fact]
    public void DotEnvStore_GetSnapshot_DoesNotExposeInternalDictionary()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllText(envPath, "KEY=value\n");

        var store = new DotEnvStore(envPath);
        var snapshot = store.GetSnapshot();

        Assert.IsNotType<Dictionary<string, string>>(snapshot);
        Assert.Equal("value", snapshot["KEY"]);
    }

    [Fact]
    public void DotEnvStore_NullCharacters_Rejected()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllText(envPath, "KEY=val\0ue\n");

        Assert.Throws<KejiConfigurationException>(() => new DotEnvStore(envPath));
    }

    [Fact]
    public void DotEnvStore_InvalidVariableName_Throws()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllText(envPath, "1INVALID=value\n");

        Assert.Throws<KejiConfigurationException>(() => new DotEnvStore(envPath));
    }

    [Fact]
    public void DotEnvStore_ExportPrefix_Throws()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllText(envPath, "export KEY=value\n");

        Assert.Throws<KejiConfigurationException>(() => new DotEnvStore(envPath));
    }

    [Fact]
    public void DotEnvStore_DuplicateKey_Throws()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllLines(envPath, new[] { "KEY=first", "KEY=second" });

        Assert.Throws<KejiConfigurationException>(() => new DotEnvStore(envPath));
    }

    [Fact]
    public void DotEnvStore_ExceedsMaxLineLength_Throws()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllText(envPath, $"K={new string('x', 200)}\n");

        Assert.Throws<KejiConfigurationException>(() => new DotEnvStore(envPath, maxLineLength: 50));
    }

    [Fact]
    public void DotEnvStore_QuotedValues_StripsQuotes()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllLines(envPath, new[]
        {
            "DQ=\"double quoted\"",
            "SQ='single quoted'",
        });

        var store = new DotEnvStore(envPath);
        Assert.Equal("double quoted", store.GetValue("DQ"));
        Assert.Equal("single quoted", store.GetValue("SQ"));
    }

    [Fact]
    public async Task DotEnvStore_Upsert_CreatesNewKey()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        var store = new DotEnvStore(envPath);

        var result = await store.UpsertAsync("NEW_KEY", "new_value");

        Assert.True(result.Created);
        Assert.False(result.Updated);
        Assert.Equal("NEW_KEY", result.Key);

        var store2 = new DotEnvStore(envPath);
        Assert.Equal("new_value", store2.GetValue("NEW_KEY"));
    }

    [Fact]
    public async Task DotEnvStore_Upsert_UpdatesExistingKey()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllText(envPath, "KEY=original\n");
        var store = new DotEnvStore(envPath);

        var result = await store.UpsertAsync("KEY", "updated");

        Assert.False(result.Created);
        Assert.True(result.Updated);
        Assert.Equal("KEY", result.Key);

        var store2 = new DotEnvStore(envPath);
        Assert.Equal("updated", store2.GetValue("KEY"));
    }

    [Fact]
    public async Task DotEnvStore_Upsert_RejectsCarriageReturn()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        var store = new DotEnvStore(envPath);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.UpsertAsync("KEY", "val\rue"));
    }

    [Fact]
    public async Task DotEnvStore_Upsert_RejectsLineFeed()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        var store = new DotEnvStore(envPath);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.UpsertAsync("KEY", "val\nue"));
    }

    [Fact]
    public async Task DotEnvStore_Upsert_RejectsNullCharacter()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        var store = new DotEnvStore(envPath);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.UpsertAsync("KEY", "val\0ue"));
    }

    [Fact]
    public async Task DotEnvStore_Upsert_PreservesComments()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllLines(envPath, new[]
        {
            "# This is a comment",
            "KEY=value",
        });
        var store = new DotEnvStore(envPath);

        await store.UpsertAsync("KEY", "updated");

        var content = File.ReadAllText(envPath);
        Assert.Contains("# This is a comment", content);
    }

    [Fact]
    public async Task DotEnvStore_Upsert_PreservesBlankLines()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllLines(envPath, new[]
        {
            "KEY1=a",
            "",
            "KEY2=b",
        });
        var store = new DotEnvStore(envPath);

        await store.UpsertAsync("KEY2", "updated");

        var rawContent = File.ReadAllText(envPath);
        Assert.Contains("KEY1=a", rawContent);
        Assert.Contains("KEY2=updated", rawContent);
        var lineArray = File.ReadAllLines(envPath);
        var aIdx = Array.IndexOf(lineArray, "KEY1=a");
        var bIdx = Array.IndexOf(lineArray, "KEY2=updated");
        Assert.True(aIdx >= 0 && bIdx >= 0);
        Assert.True(bIdx - aIdx > 1, "Blank line between KEY1 and KEY2 was not preserved");
    }

    [Fact]
    public async Task DotEnvStore_Upsert_PreservesVariableOrder()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllLines(envPath, new[] { "A=1", "B=2", "C=3" });
        var store = new DotEnvStore(envPath);

        await store.UpsertAsync("B", "updated");

        var lines = File.ReadAllLines(envPath);
        Assert.Contains("A=1", lines);
        Assert.Contains("B=updated", lines);
        Assert.Contains("C=3", lines);
        var aIdx = Array.IndexOf(lines, "A=1");
        var bIdx = Array.IndexOf(lines, "B=updated");
        var cIdx = Array.IndexOf(lines, "C=3");
        Assert.True(aIdx < bIdx);
        Assert.True(bIdx < cIdx);
    }

    [Fact]
    public async Task DotEnvStore_Upsert_DoesNotCreateDuplicateKeys()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllText(envPath, "KEY=original\n");
        var store = new DotEnvStore(envPath);

        await store.UpsertAsync("KEY", "updated");
        await store.UpsertAsync("KEY", "final");

        var lines = File.ReadAllLines(envPath);
        Assert.Single(lines, l => l.StartsWith("KEY="));
    }

    [Fact]
    public async Task DotEnvStore_Upsert_NewKeyAppendedToEnd()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllLines(envPath, new[] { "A=1", "B=2" });
        var store = new DotEnvStore(envPath);

        await store.UpsertAsync("C", "3");

        var lines = File.ReadAllLines(envPath);
        Assert.Equal("C=3", lines[^1]);
    }

    [Fact]
    public async Task DotEnvStore_Upsert_ResultDoesNotContainValue()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        var store = new DotEnvStore(envPath);

        var result = await store.UpsertAsync("SECRET", "my_secret_value");

        Assert.Equal("SECRET", result.Key);
        var resultType = result.GetType();
        Assert.Null(resultType.GetProperty("Value"));
    }

    [Fact]
    public void DotEnvStore_ExceedsMaxSize_Throws()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllText(envPath, new string('x', 200));

        Assert.Throws<KejiConfigurationException>(() => new DotEnvStore(envPath, maxFileBytes: 50));
    }

    [Fact]
    public void DotEnvStore_DoesNotModifyProcessEnvironment()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllText(envPath, "PATH=should_not_affect\n");

        var store = new DotEnvStore(envPath);
        Assert.Equal("should_not_affect", store.GetValue("PATH"));

        var procValue = Environment.GetEnvironmentVariable("PATH");
        Assert.NotNull(procValue);
        Assert.NotEqual("should_not_affect", procValue);
    }

    [Fact]
    public void ProviderSecretName_Deepseek_ReturnsCorrect()
    {
        var result = ProviderSecretName.GetEnvironmentVariableName("deepseek");
        Assert.Equal("DEEPSEEK_API_KEY", result);
    }

    [Fact]
    public void ProviderSecretName_OpenAI_ReturnsCorrect()
    {
        var result = ProviderSecretName.GetEnvironmentVariableName("openai");
        Assert.Equal("OPENAI_API_KEY", result);
    }

    [Fact]
    public void ProviderSecretName_CaseInsensitive()
    {
        Assert.Equal("DEEPSEEK_API_KEY", ProviderSecretName.GetEnvironmentVariableName("DeepSeek"));
    }

    [Fact]
    public void ProviderSecretName_GenericProvider_UppercaseAndUnderscore()
    {
        var result = ProviderSecretName.GetEnvironmentVariableName("my-provider");
        Assert.Equal("MY_PROVIDER_API_KEY", result);
    }

    [Fact]
    public void ProviderSecretName_GenericProvider_UnderscorePreserved()
    {
        var result = ProviderSecretName.GetEnvironmentVariableName("azure_openai");
        Assert.Equal("AZURE_OPENAI_API_KEY", result);
    }

    [Fact]
    public void ProviderSecretName_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => ProviderSecretName.GetEnvironmentVariableName(""));
    }

    [Fact]
    public void ProviderSecretName_WithSpace_Throws()
    {
        Assert.Throws<ArgumentException>(() => ProviderSecretName.GetEnvironmentVariableName("my provider"));
    }

    [Fact]
    public void ProviderSecretName_WithNewline_Throws()
    {
        Assert.Throws<ArgumentException>(() => ProviderSecretName.GetEnvironmentVariableName("my\nprovider"));
    }

    [Fact]
    public void ProviderSecretName_WithEquals_Throws()
    {
        Assert.Throws<ArgumentException>(() => ProviderSecretName.GetEnvironmentVariableName("my=provider"));
    }

    [Fact]
    public void ProviderSecretName_WithSlash_Throws()
    {
        Assert.Throws<ArgumentException>(() => ProviderSecretName.GetEnvironmentVariableName("my/provider"));
    }

    [Fact]
    public void ProviderSecretName_WithBackslash_Throws()
    {
        Assert.Throws<ArgumentException>(() => ProviderSecretName.GetEnvironmentVariableName("my\\provider"));
    }

    [Fact]
    public void ProviderSecretName_WithDot_Throws()
    {
        Assert.Throws<ArgumentException>(() => ProviderSecretName.GetEnvironmentVariableName("my.provider"));
    }

    [Fact]
    public void ProviderSecretName_WithColon_Throws()
    {
        Assert.Throws<ArgumentException>(() => ProviderSecretName.GetEnvironmentVariableName("my:provider"));
    }

    [Fact]
    public void SecretMasker_MasksPasswordKey()
    {
        var map = new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("password", new ConfigScalar("super_secret_123")),
        });

        var masker = new SecretMasker();
        var result = masker.Mask(map) as ConfigMap;
        Assert.NotNull(result);
        Assert.Equal("***", ((ConfigScalar)result["password"]).Value);
    }

    [Fact]
    public void SecretMasker_MasksApiKey()
    {
        var map = new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("api_key", new ConfigScalar("sk-abc123")),
        });

        var masker = new SecretMasker();
        var result = masker.Mask(map) as ConfigMap;
        Assert.NotNull(result);
        Assert.Equal("***", ((ConfigScalar)result["api_key"]).Value);
    }

    [Fact]
    public void SecretMasker_DoesNotMaskNonSensitiveKeys()
    {
        var map = new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("name", new ConfigScalar("keji")),
            new KeyValuePair<string, ConfigNode>("version", new ConfigScalar("1.0")),
        });

        var masker = new SecretMasker();
        var result = masker.Mask(map) as ConfigMap;
        Assert.NotNull(result);
        Assert.Equal("keji", ((ConfigScalar)result["name"]).Value);
        Assert.Equal("1.0", ((ConfigScalar)result["version"]).Value);
    }

    [Fact]
    public void SecretMasker_MasksInNestedMap()
    {
        var inner = new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("token", new ConfigScalar("eyJhbGci")),
            new KeyValuePair<string, ConfigNode>("public", new ConfigScalar("visible")),
        });
        var outer = new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("app", inner),
        });

        var masker = new SecretMasker();
        var result = masker.Mask(outer) as ConfigMap;
        Assert.NotNull(result);

        var app = result["app"] as ConfigMap;
        Assert.NotNull(app);
        Assert.Equal("***", ((ConfigScalar)app["token"]).Value);
        Assert.Equal("visible", ((ConfigScalar)app["public"]).Value);
    }

    [Fact]
    public void SecretMasker_MasksAllRequiredKeys()
    {
        var requiredKeys = new[]
        {
            "api_key", "apikey", "app_secret", "client_secret", "secret",
            "password", "token", "access_token", "refresh_token",
            "verification_token", "encrypt_key", "work_secret", "jwt_secret",
            "private_key", "connection_string",
        };

        var entries = requiredKeys.Select(k =>
            new KeyValuePair<string, ConfigNode>(k, new ConfigScalar("value")));

        var map = new ConfigMap(entries);
        var masker = new SecretMasker();
        var result = masker.Mask(map) as ConfigMap;
        Assert.NotNull(result);

        foreach (var key in requiredKeys)
        {
            Assert.Equal("***", ((ConfigScalar)result[key]).Value);
        }
    }

    [Fact]
    public void SecretMasker_MasksDictionary()
    {
        var dict = new Dictionary<string, object?>
        {
            { "password", "secret123" },
            { "name", "keji" },
        };

        var masker = new SecretMasker();
        var result = masker.Mask(new ReadOnlyDictionary<string, object?>(dict));

        Assert.Equal("***", result["password"]);
        Assert.Equal("keji", result["name"]);
    }

    [Fact]
    public void SecretMasker_MasksNestedDictionary()
    {
        var inner = new Dictionary<string, object?>
        {
            { "token", "eyJhbGci" },
            { "user", "admin" },
        };
        var outer = new Dictionary<string, object?>
        {
            { "app", new ReadOnlyDictionary<string, object?>(inner) },
        };

        var masker = new SecretMasker();
        var result = masker.Mask(new ReadOnlyDictionary<string, object?>(outer));

        var app = result["app"] as IReadOnlyDictionary<string, object?>;
        Assert.NotNull(app);
        Assert.Equal("***", app["token"]);
        Assert.Equal("admin", app["user"]);
    }

    [Fact]
    public void SecretMasker_MasksList()
    {
        var inner = new Dictionary<string, object?>
        {
            { "password", "secret1" },
        };
        var list = new List<object?>
        {
            new ReadOnlyDictionary<string, object?>(inner),
        };

        var masker = new SecretMasker();
        var result = masker.Mask(list);

        var item = result[0] as IReadOnlyDictionary<string, object?>;
        Assert.NotNull(item);
        Assert.Equal("***", item["password"]);
    }

    [Fact]
    public void SecretMasker_ToStringDoesNotLeakSecret()
    {
        var map = new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("password", new ConfigScalar("super_secret_value")),
        });

        var masker = new SecretMasker();
        var result = masker.Mask(map) as ConfigMap;
        Assert.NotNull(result);

        var scalar = result["password"] as ConfigScalar;
        Assert.NotNull(scalar);
        Assert.DoesNotContain("super_secret_value", scalar.ToString());
    }

    [Fact]
    public void MaskApiKeyForSettings_Null_ReturnsNotConfigured()
    {
        var mask = SecretMasker.MaskApiKeyForSettings(null);
        Assert.False(mask.IsConfigured);
        Assert.Equal("", mask.DisplayValue);
    }

    [Fact]
    public void MaskApiKeyForSettings_Empty_ReturnsNotConfigured()
    {
        var mask = SecretMasker.MaskApiKeyForSettings("");
        Assert.False(mask.IsConfigured);
        Assert.Equal("", mask.DisplayValue);
    }

    [Fact]
    public void MaskApiKeyForSettings_AnyValue_ReturnsConfigured()
    {
        var mask = SecretMasker.MaskApiKeyForSettings("sk-abc123");
        Assert.True(mask.IsConfigured);
        Assert.Equal("***", mask.DisplayValue);
    }

    [Fact]
    public void MaskApiKeyForSettings_DoesNotLeakPrefixOrSuffix()
    {
        var mask = SecretMasker.MaskApiKeyForSettings("sk-abc123xyz");
        Assert.Equal("***", mask.DisplayValue);
        Assert.DoesNotContain("sk-", mask.DisplayValue);
        Assert.DoesNotContain("abc123", mask.DisplayValue);
    }

    private static string BuildDeepYaml(int depth)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("a:\n");
        for (int i = 0; i < depth; i++)
        {
            sb.Append(new string(' ', (i + 1) * 2));
            sb.Append("a:\n");
        }
        sb.Append(new string(' ', (depth + 1) * 2));
        sb.Append("v: x\n");
        return sb.ToString();
    }

    private sealed class TestEnvironmentValueSource : IEnvironmentValueSource
    {
        private readonly Dictionary<string, string> _values;

        public TestEnvironmentValueSource(Dictionary<string, string> values)
        {
            _values = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        }

        public string? GetValue(string variableName)
        {
            return _values.TryGetValue(variableName, out var val) ? val : null;
        }
    }
}

internal sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KJ_TEST_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { }
    }
}
