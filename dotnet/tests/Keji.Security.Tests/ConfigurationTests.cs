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
        Assert.Equal("value", ((ConfigScalar)map["KEY"]).Value);
        Assert.Equal("value", ((ConfigScalar)map["Key"]).Value);
    }

    [Fact]
    public void ConfigMap_DuplicateKeyCaseInsensitive_LastWins()
    {
        var map = new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("key", new ConfigScalar("first")),
            new KeyValuePair<string, ConfigNode>("KEY", new ConfigScalar("second")),
        });

        Assert.Equal("second", ((ConfigScalar)map["key"]).Value);
        Assert.Single(map);
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
        var inner = new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("name", new ConfigScalar("Keji")),
        });
        var doc = new KejiConfigurationDocument(new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("app", inner),
        }));

        Assert.Equal("Keji", doc.GetOptionalString("app.name"));
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
        Assert.False(doc.GetBoolean("missing", false));
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
    public void KejiConfigurationLoadOptions_Defaults()
    {
        var options = new KejiConfigurationLoadOptions();
        Assert.Equal(1 * 1024 * 1024, options.MaxConfigFileBytes);
        Assert.Equal(1 * 1024 * 1024, options.MaxDotEnvFileBytes);
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
    public void SafeYamlConfigurationLoader_LoadFullConfigExample()
    {
        using var tempDir = new TempDirectory();
        var yamlPath = Path.Combine(tempDir.Path, "config.yaml");
        File.WriteAllText(yamlPath, @"
app:
  name: keji
  port: 8080
  debug: true
database:
  host: localhost
  port: 5432
logging:
  level: info
  outputs:
    - console
    - file
");

        var loader = new SafeYamlConfigurationLoader();
        var doc = loader.Load(new KejiConfigurationLoadOptions
        {
            ProjectRoot = tempDir.Path,
            RequireConfigFile = true,
        });

        Assert.Equal("keji", doc.GetOptionalString("app.name"));
        Assert.Equal("8080", doc.GetOptionalString("app.port"));
        Assert.Equal("true", doc.GetOptionalString("app.debug"));
        Assert.Equal("localhost", doc.GetOptionalString("database.host"));
        Assert.Equal("5432", doc.GetOptionalString("database.port"));
        Assert.Equal("info", doc.GetOptionalString("logging.level"));
        var outputs = doc.GetStringList("logging.outputs");
        Assert.Equal(2, outputs.Count);
        Assert.Contains("console", outputs);
        Assert.Contains("file", outputs);
    }

    [Fact]
    public void SafeYamlConfigurationLoader_ExceedsMaxDepth_Throws()
    {
        using var tempDir = new TempDirectory();
        var yamlPath = Path.Combine(tempDir.Path, "config.yaml");
        var deepYaml = BuildDeepYaml(40);
        File.WriteAllText(yamlPath, deepYaml);

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
    public void SafeYamlConfigurationLoader_LoadEmptyDocument_Throws()
    {
        using var tempDir = new TempDirectory();
        var yamlPath = Path.Combine(tempDir.Path, "config.yaml");
        File.WriteAllText(yamlPath, "{}");

        var loader = new SafeYamlConfigurationLoader();
        var doc = loader.Load(new KejiConfigurationLoadOptions
        {
            ProjectRoot = tempDir.Path,
            RequireConfigFile = true,
        });

        Assert.NotNull(doc);
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

        Assert.Equal("resolved_value", ((ConfigScalar)result).Value);
    }

    [Fact]
    public void EnvironmentReferenceResolver_ResolvesVarInString()
    {
        var source = new TestEnvironmentValueSource(new Dictionary<string, string>
        {
            { "NAME", "World" },
        });
        var resolver = new EnvironmentReferenceResolver(source, failOnMissing: true);

        var result = resolver.Resolve(new ConfigScalar("Hello, ${NAME}!"));

        Assert.Equal("Hello, World!", ((ConfigScalar)result).Value);
    }

    [Fact]
    public void EnvironmentReferenceResolver_UsesDefaultValue()
    {
        var source = new TestEnvironmentValueSource(new Dictionary<string, string>());
        var resolver = new EnvironmentReferenceResolver(source, failOnMissing: true);

        var result = resolver.Resolve(new ConfigScalar("${MISSING|default_val}"));

        Assert.Equal("default_val", ((ConfigScalar)result).Value);
    }

    [Fact]
    public void EnvironmentReferenceResolver_MissingVarWithNoDefault_Throws()
    {
        var source = new TestEnvironmentValueSource(new Dictionary<string, string>());
        var resolver = new EnvironmentReferenceResolver(source, failOnMissing: true);

        Assert.Throws<KejiConfigurationException>(() =>
            resolver.Resolve(new ConfigScalar("${MISSING}")));
    }

    [Fact]
    public void EnvironmentReferenceResolver_MissingVarNoFail_KeepsOriginal()
    {
        var source = new TestEnvironmentValueSource(new Dictionary<string, string>());
        var resolver = new EnvironmentReferenceResolver(source, failOnMissing: false);

        var result = resolver.Resolve(new ConfigScalar("${MISSING}"));

        Assert.Equal("${MISSING}", ((ConfigScalar)result).Value);
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

        var result = resolver.Resolve(map) as ConfigMap;
        Assert.NotNull(result);
        Assert.Equal("3000", ((ConfigScalar)result["port"]).Value);
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

        var result = resolver.Resolve(seq) as ConfigSequence;
        Assert.NotNull(result);
        Assert.Equal("localhost", ((ConfigScalar)result[0]).Value);
    }

    [Fact]
    public void EnvironmentReferenceResolver_NoVarNoChange()
    {
        var source = new TestEnvironmentValueSource(new Dictionary<string, string>());
        var resolver = new EnvironmentReferenceResolver(source, failOnMissing: true);

        var result = resolver.Resolve(new ConfigScalar("plain text"));

        Assert.Equal("plain text", ((ConfigScalar)result).Value);
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
        Assert.Equal("secret", store.GetValue("Api_Key"));
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
    public void DotEnvStore_SetValueAndPersists()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        var store = new DotEnvStore(envPath);

        store.SetValue("NEW_KEY", "new_value");

        var store2 = new DotEnvStore(envPath);
        Assert.Equal("new_value", store2.GetValue("NEW_KEY"));
    }

    [Fact]
    public void DotEnvStore_RemoveValue()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllText(envPath, "TO_REMOVE=value\nKEEP=stay\n");

        var store = new DotEnvStore(envPath);
        Assert.True(store.RemoveValue("TO_REMOVE"));
        Assert.Null(store.GetValue("TO_REMOVE"));
        Assert.Equal("stay", store.GetValue("KEEP"));
    }

    [Fact]
    public void DotEnvStore_RemoveMissing_ReturnsFalse()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        var store = new DotEnvStore(envPath);

        Assert.False(store.RemoveValue("NONEXISTENT"));
    }

    [Fact]
    public void DotEnvStore_Reload_ReflectsFileChanges()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllText(envPath, "VAR=original\n");

        var store = new DotEnvStore(envPath);
        Assert.Equal("original", store.GetValue("VAR"));

        File.WriteAllText(envPath, "VAR=updated\n");
        store.Reload();
        Assert.Equal("updated", store.GetValue("VAR"));
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
    public void DotEnvStore_ExceedsMaxSize_Throws()
    {
        using var tempDir = new TempDirectory();
        var envPath = Path.Combine(tempDir.Path, ".env");
        File.WriteAllText(envPath, new string('x', 200));

        Assert.Throws<KejiConfigurationException>(() => new DotEnvStore(envPath, maxFileBytes: 50));
    }

    [Fact]
    public void ProviderSecretName_Deepseek_ReturnsCorrectEnvVar()
    {
        var envVar = ProviderSecretName.GetSecretEnvironmentVariable("deepseek");
        Assert.Equal("DEEPSEEK_API_KEY", envVar);
    }

    [Fact]
    public void ProviderSecretName_OpenAI_ReturnsCorrectEnvVar()
    {
        var envVar = ProviderSecretName.GetSecretEnvironmentVariable("openai");
        Assert.Equal("OPENAI_API_KEY", envVar);
    }

    [Fact]
    public void ProviderSecretName_CaseInsensitive()
    {
        var envVar = ProviderSecretName.GetSecretEnvironmentVariable("DeepSeek");
        Assert.Equal("DEEPSEEK_API_KEY", envVar);
    }

    [Fact]
    public void ProviderSecretName_UnknownProvider_ReturnsNull()
    {
        var envVar = ProviderSecretName.GetSecretEnvironmentVariable("unknown");
        Assert.Null(envVar);
    }

    [Fact]
    public void ProviderSecretName_TryGet_ReturnsTrueForKnown()
    {
        Assert.True(ProviderSecretName.TryGetSecretEnvironmentVariable("openai", out var envVar));
        Assert.Equal("OPENAI_API_KEY", envVar);
    }

    [Fact]
    public void ProviderSecretName_TryGet_ReturnsFalseForUnknown()
    {
        Assert.False(ProviderSecretName.TryGetSecretEnvironmentVariable("nope", out _));
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
        Assert.Equal("---", ((ConfigScalar)result["password"]).Value);
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
        Assert.Equal("---", ((ConfigScalar)result["api_key"]).Value);
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
        Assert.Equal("---", ((ConfigScalar)app["token"]).Value);
        Assert.Equal("visible", ((ConfigScalar)app["public"]).Value);
    }

    [Fact]
    public void SecretMasker_MasksItemsInSequence()
    {
        var seq = new ConfigSequence(new ConfigNode[]
        {
            new ConfigMap(new[]
            {
                new KeyValuePair<string, ConfigNode>("password", new ConfigScalar("secret1")),
            }),
            new ConfigMap(new[]
            {
                new KeyValuePair<string, ConfigNode>("password", new ConfigScalar("secret2")),
            }),
        });

        var masker = new SecretMasker();
        var result = masker.Mask(seq) as ConfigSequence;
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        foreach (var item in result)
        {
            var map = item as ConfigMap;
            Assert.NotNull(map);
            Assert.Equal("---", ((ConfigScalar)map["password"]).Value);
        }
    }

    [Fact]
    public void SecretMasker_RecursiveSequenceOfSequences()
    {
        var innerSeq = new ConfigSequence(new ConfigNode[]
        {
            new ConfigMap(new[]
            {
                new KeyValuePair<string, ConfigNode>("secret", new ConfigScalar("s1")),
            }),
        });
        var outerSeq = new ConfigSequence(new ConfigNode[] { innerSeq });

        var masker = new SecretMasker();
        var result = masker.Mask(outerSeq) as ConfigSequence;
        Assert.NotNull(result);
        var inner = result[0] as ConfigSequence;
        Assert.NotNull(inner);
        var map = inner[0] as ConfigMap;
        Assert.NotNull(map);
        Assert.Equal("---", ((ConfigScalar)map["secret"]).Value);
    }

    [Fact]
    public void SecretMasker_MasksAllSensitiveKeys()
    {
        var sensitiveKeys = new[]
        {
            "password", "passwd", "pwd", "secret", "api_key", "apikey",
            "api-key", "token", "auth_token", "authtoken", "access_token",
            "accesstoken", "private_key", "privatekey", "connection_string",
            "connectionstring", "master_key", "masterkey",
        };

        var entries = sensitiveKeys.Select(k =>
            new KeyValuePair<string, ConfigNode>(k, new ConfigScalar("value")));

        var map = new ConfigMap(entries);
        var masker = new SecretMasker();
        var result = masker.Mask(map) as ConfigMap;
        Assert.NotNull(result);

        foreach (var key in sensitiveKeys)
        {
            Assert.Equal("---", ((ConfigScalar)result[key]).Value);
        }
    }

    [Fact]
    public void EnvironmentReferenceResolver_ResolvesInNestedMap()
    {
        var source = new TestEnvironmentValueSource(new Dictionary<string, string>
        {
            { "DB_HOST", "localhost" },
            { "DB_PORT", "5432" },
        });
        var resolver = new EnvironmentReferenceResolver(source, failOnMissing: true);

        var inner = new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("host", new ConfigScalar("${DB_HOST}")),
            new KeyValuePair<string, ConfigNode>("port", new ConfigScalar("${DB_PORT}")),
        });
        var outer = new ConfigMap(new[]
        {
            new KeyValuePair<string, ConfigNode>("database", inner),
        });

        var result = resolver.Resolve(outer) as ConfigMap;
        Assert.NotNull(result);
        var db = result["database"] as ConfigMap;
        Assert.NotNull(db);
        Assert.Equal("localhost", ((ConfigScalar)db["host"]).Value);
        Assert.Equal("5432", ((ConfigScalar)db["port"]).Value);
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
