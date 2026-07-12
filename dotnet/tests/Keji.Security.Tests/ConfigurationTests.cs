using System.Collections.ObjectModel;
using Keji.Configuration.Loading;
using Keji.Configuration.Models;
using Keji.Configuration.Secrets;
using Microsoft.Extensions.DependencyInjection;

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
        var inner = new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("b", new ConfigScalar("2")) });
        var outer = new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("a", inner) });

        Assert.True(outer.TryGetValue("a", out var aNode));
        var aMap = aNode as ConfigMap;
        Assert.NotNull(aMap);
        Assert.True(aMap.TryGetValue("b", out var bNode));
        Assert.Equal("2", ((ConfigScalar)bNode).Value);
    }

    [Fact]
    public void ConfigSequence_Create_ReturnsItems()
    {
        var seq = new ConfigSequence(new ConfigNode[] { new ConfigScalar("a"), new ConfigScalar("b") });
        Assert.Equal(2, seq.Count);
        Assert.Equal("a", ((ConfigScalar)seq[0]).Value);
        Assert.Equal("b", ((ConfigScalar)seq[1]).Value);
    }

    [Fact]
    public void ConfigSequence_Enumerate_Works()
    {
        var seq = new ConfigSequence(new ConfigNode[] { new ConfigScalar("x") });
        int count = 0;
        foreach (var item in seq) { Assert.Equal("x", ((ConfigScalar)item).Value); count++; }
        Assert.Equal(1, count);
    }

    [Fact]
    public void ConfigNode_AsScalar_ReturnsTyped()
    {
        ConfigNode node = new ConfigScalar("test");
        Assert.Equal("test", node.AsScalar().Value);
    }

    [Fact]
    public void ConfigNode_AsMap_ReturnsTyped()
    {
        ConfigNode node = new ConfigMap(Array.Empty<KeyValuePair<string, ConfigNode>>());
        Assert.NotNull(node.AsMap());
    }

    [Fact]
    public void ConfigNode_AsSequence_ReturnsTyped()
    {
        ConfigNode node = new ConfigSequence(Array.Empty<ConfigNode>());
        Assert.NotNull(node.AsSequence());
    }

    [Fact]
    public void Document_GetOptionalString_Existing_ReturnsValue()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("key", new ConfigScalar("value")) }));
        Assert.Equal("value", doc.GetOptionalString("key"));
    }

    [Fact]
    public void Document_GetOptionalString_Missing_ReturnsNull()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(Array.Empty<KeyValuePair<string, ConfigNode>>()));
        Assert.Null(doc.GetOptionalString("missing"));
    }

    [Fact]
    public void Document_GetRequiredString_Existing_ReturnsValue()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("key", new ConfigScalar("value")) }));
        Assert.Equal("value", doc.GetRequiredString("key"));
    }

    [Fact]
    public void Document_GetRequiredString_Missing_Throws()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(Array.Empty<KeyValuePair<string, ConfigNode>>()));
        Assert.Throws<KejiConfigurationException>(() => doc.GetRequiredString("missing"));
    }

    [Fact]
    public void Document_GetBoolean_Default_ReturnsDefault()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(Array.Empty<KeyValuePair<string, ConfigNode>>()));
        Assert.True(doc.GetBoolean("missing", true));
    }

    [Fact]
    public void Document_GetBoolean_Parsed_ReturnsValue()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("flag", new ConfigScalar("true")) }));
        Assert.True(doc.GetBoolean("flag", false));
    }

    [Fact]
    public void Document_GetInt32_Default_ReturnsDefault()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(Array.Empty<KeyValuePair<string, ConfigNode>>()));
        Assert.Equal(42, doc.GetInt32("missing", 42));
    }

    [Fact]
    public void Document_GetInt32_Parsed_ReturnsValue()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("port", new ConfigScalar("8080")) }));
        Assert.Equal(8080, doc.GetInt32("port", 0));
    }

    [Fact]
    public void Document_GetStringList_Existing_ReturnsList()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("items", new ConfigSequence(new ConfigNode[] { new ConfigScalar("a"), new ConfigScalar("b") })) }));
        var list = doc.GetStringList("items");
        Assert.Equal(2, list.Count);
        Assert.Contains("a", list);
    }

    [Fact]
    public void Document_GetStringList_Missing_ReturnsEmpty()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(Array.Empty<KeyValuePair<string, ConfigNode>>()));
        Assert.Empty(doc.GetStringList("missing"));
    }

    [Fact]
    public void Document_TryGetNode_DottedPath_ReturnsNode()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("a", new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("b", new ConfigScalar("c")) })) }));
        Assert.True(doc.TryGetNode("a.b", out var node));
        Assert.Equal("c", ((ConfigScalar)node!).Value);
    }

    [Fact]
    public void Document_GetInt32_NotInt_Throws()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("val", new ConfigScalar("not_a_number")) }));
        Assert.Throws<KejiConfigurationException>(() => doc.GetInt32("val", 0));
    }

    [Fact]
    public void Document_GetBoolean_NotBool_Throws()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("flag", new ConfigScalar("not_bool")) }));
        Assert.Throws<KejiConfigurationException>(() => doc.GetBoolean("flag", false));
    }

    [Fact]
    public void Document_GetStringList_NotSequence_Throws()
    {
        var doc = new KejiConfigurationDocument(new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("item", new ConfigScalar("not_a_list")) }));
        Assert.Throws<KejiConfigurationException>(() => doc.GetStringList("item"));
    }

    [Fact]
    public void KejiConfigurationLoadOptions_Defaults()
    {
        var o = new KejiConfigurationLoadOptions();
        Assert.Equal(1 * 1024 * 1024, o.MaxConfigFileBytes);
        Assert.Equal(1 * 1024 * 1024, o.MaxDotEnvFileBytes);
        Assert.Equal(16384, o.MaxDotEnvLineLength);
        Assert.Equal(32, o.MaxDepth);
        Assert.Equal(10000, o.MaxNodeCount);
        Assert.True(o.RequireConfigFile);
        Assert.True(o.FailOnMissingEnvironmentVariable);
        Assert.Equal("config.yaml", o.ConfigFileName);
        Assert.Equal(".env", o.DotEnvFileName);
    }

    [Fact]
    public void SafeYamlLoader_LoadBasicYaml()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "key: value\nn: 42\n");
        var loader = new SafeYamlConfigurationLoader();
        var node = loader.Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true });
        var map = node as ConfigMap;
        Assert.NotNull(map);
        Assert.Equal("value", ((ConfigScalar)map["key"]).Value);
        Assert.Equal("42", ((ConfigScalar)map["n"]).Value);
    }

    [Fact]
    public void SafeYamlLoader_LoadNestedYaml()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "app:\n  name: keji\n");
        var loader = new SafeYamlConfigurationLoader();
        var map = loader.Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true }) as ConfigMap;
        Assert.NotNull(map);
        var app = map["app"] as ConfigMap;
        Assert.NotNull(app);
        Assert.Equal("keji", ((ConfigScalar)app["name"]).Value);
    }

    [Fact]
    public void SafeYamlLoader_LoadSequenceYaml()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "items:\n  - a\n  - b\n");
        var loader = new SafeYamlConfigurationLoader();
        var map = loader.Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true }) as ConfigMap;
        Assert.NotNull(map);
        var seq = map["items"] as ConfigSequence;
        Assert.NotNull(seq);
        Assert.Equal(2, seq.Count);
    }

    [Fact]
    public void SafeYamlLoader_MissingFile_Throws()
    {
        using var td = new TempDir();
        var loader = new SafeYamlConfigurationLoader();
        Assert.Throws<KejiConfigurationException>(() =>
            loader.Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true }));
    }

    [Fact]
    public void SafeYamlLoader_MissingFile_Optional_ReturnsEmpty()
    {
        using var td = new TempDir();
        var loader = new SafeYamlConfigurationLoader();
        var node = loader.Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = false });
        Assert.IsType<ConfigMap>(node);
    }

    [Fact]
    public void SafeYamlLoader_DuplicateKeys_Throws()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "key: first\nkey: second\n");
        var loader = new SafeYamlConfigurationLoader();
        Assert.Throws<KejiConfigurationException>(() =>
            loader.Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true }));
    }

    [Fact]
    public void SafeYamlLoader_CustomScalarTag_Rejected()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "key: !!str value\n");
        var ex = Assert.Throws<KejiConfigurationException>(() =>
            new SafeYamlConfigurationLoader().Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true }));
        Assert.Contains("CUSTOM_TAG", ex.Message);
    }

    [Fact]
    public void SafeYamlLoader_CustomMappingTag_Rejected()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "!!map\n  key: value\n");
        var ex = Assert.Throws<KejiConfigurationException>(() =>
            new SafeYamlConfigurationLoader().Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true }));
        Assert.Contains("CUSTOM_TAG", ex.Message);
    }

    [Fact]
    public void SafeYamlLoader_CustomSequenceTag_Rejected()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "!!seq\n  - a\n  - b\n");
        var ex = Assert.Throws<KejiConfigurationException>(() =>
            new SafeYamlConfigurationLoader().Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true }));
        Assert.Contains("CUSTOM_TAG", ex.Message);
    }

    [Fact]
    public void SafeYamlLoader_Anchor_Rejected()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "d: &a\n  key: value\n");
        var ex = Assert.Throws<KejiConfigurationException>(() =>
            new SafeYamlConfigurationLoader().Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true }));
        Assert.Contains("ANCHOR", ex.Message);
    }

    [Fact]
    public void SafeYamlLoader_Alias_Rejected()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "key: *some_alias\n");
        var ex = Assert.Throws<KejiConfigurationException>(() =>
            new SafeYamlConfigurationLoader().Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true }));
        Assert.Contains("ALIAS", ex.Message);
    }

    [Fact]
    public void SafeYamlLoader_ExceedsMaxDepth_Throws()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), BuildDeepYaml(40));
        Assert.Throws<KejiConfigurationException>(() =>
            new SafeYamlConfigurationLoader().Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true, MaxDepth = 10 }));
    }

    [Fact]
    public void SafeYamlLoader_ExceedsMaxNodeCount_Throws()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), string.Join("\n", Enumerable.Range(0, 200).Select(i => $"k{i}: v{i}")));
        Assert.Throws<KejiConfigurationException>(() =>
            new SafeYamlConfigurationLoader().Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true, MaxNodeCount = 50 }));
    }

    [Fact]
    public void SafeYamlLoader_ExceedsMaxSize_Throws()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), new string('x', 2000));
        Assert.Throws<KejiConfigurationException>(() =>
            new SafeYamlConfigurationLoader().Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true, MaxConfigFileBytes = 100 }));
    }

    [Fact]
    public void SafeYamlLoader_RootNotMapping_Throws()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "[1, 2, 3]\n");
        Assert.Throws<KejiConfigurationException>(() =>
            new SafeYamlConfigurationLoader().Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true }));
    }

    [Fact]
    public void SafeYamlLoader_MalformedYaml_WrapsException()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "key: value\nunbalanced: [\n");
        var ex = Assert.Throws<KejiConfigurationException>(() =>
            new SafeYamlConfigurationLoader().Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true }));
        Assert.Contains("YAML_PARSE_ERROR", ex.Message);
    }

    [Fact]
    public void SafeYamlLoader_ExceptionDoesNotContainSecrets()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "password: my_super_secret_value\nkey: *missing_alias\n");
        var ex = Assert.Throws<KejiConfigurationException>(() =>
            new SafeYamlConfigurationLoader().Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true }));
        Assert.DoesNotContain("my_super_secret_value", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SafeYamlLoader_MalformedExceptionMessage_NoSecretLeak()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "secret_key: my_top_secret\nkey: *bad_alias\n");
        var ex = Assert.Throws<KejiConfigurationException>(() =>
            new SafeYamlConfigurationLoader().Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true }));
        Assert.DoesNotContain("my_top_secret", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("my_top_secret", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnvRef_ResolvesSimpleVar()
    {
        var src = new TestEnvSrc(new Dictionary<string, string> { { "MY_VAR", "resolved" } });
        var r = new EnvironmentReferenceResolver(src, true);
        var result = r.Resolve(new ConfigScalar("${MY_VAR}"));
        Assert.Equal("resolved", ((ConfigScalar)result.Root).Value);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void EnvRef_PrefixInterpolation_NotResolved()
    {
        var src = new TestEnvSrc(new Dictionary<string, string> { { "NAME", "World" } });
        var r = new EnvironmentReferenceResolver(src, true);
        var result = r.Resolve(new ConfigScalar("prefix-${NAME}"));
        Assert.Equal("prefix-${NAME}", ((ConfigScalar)result.Root).Value);
    }

    [Fact]
    public void EnvRef_SuffixInterpolation_NotResolved()
    {
        var src = new TestEnvSrc(new Dictionary<string, string> { { "NAME", "World" } });
        var r = new EnvironmentReferenceResolver(src, true);
        var result = r.Resolve(new ConfigScalar("${NAME}-suffix"));
        Assert.Equal("${NAME}-suffix", ((ConfigScalar)result.Root).Value);
    }

    [Fact]
    public void EnvRef_DefaultSyntax_NotResolved()
    {
        var src = new TestEnvSrc(new Dictionary<string, string>());
        var r = new EnvironmentReferenceResolver(src, true);
        var result = r.Resolve(new ConfigScalar("${MISSING|default_val}"));
        Assert.Equal("${MISSING|default_val}", ((ConfigScalar)result.Root).Value);
    }

    [Fact]
    public void EnvRef_InvalidName_NotResolved()
    {
        var src = new TestEnvSrc(new Dictionary<string, string>());
        var r = new EnvironmentReferenceResolver(src, true);
        var result = r.Resolve(new ConfigScalar("${1BAD}"));
        Assert.Equal("${1BAD}", ((ConfigScalar)result.Root).Value);
    }

    [Fact]
    public void EnvRef_MissingVar_Strict_Throws()
    {
        var src = new TestEnvSrc(new Dictionary<string, string>());
        var r = new EnvironmentReferenceResolver(src, true);
        var ex = Assert.Throws<KejiConfigurationException>(() => r.Resolve(new ConfigScalar("${MISSING}")));
        Assert.Contains("MISSING", ex.Message);
    }

    [Fact]
    public void EnvRef_MissingVar_NonStrict_ReturnsEmptyAndDiag()
    {
        var src = new TestEnvSrc(new Dictionary<string, string>());
        var r = new EnvironmentReferenceResolver(src, false);
        var result = r.Resolve(new ConfigScalar("${MISSING}"));
        Assert.Equal(string.Empty, ((ConfigScalar)result.Root).Value);
        Assert.Single(result.Diagnostics);
        Assert.Equal("MISSING", result.Diagnostics[0].EnvironmentVariableName);
    }

    [Fact]
    public void EnvRef_TracksDottedPath()
    {
        var src = new TestEnvSrc(new Dictionary<string, string>());
        var r = new EnvironmentReferenceResolver(src, false);
        var map = new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("app", new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("secret_key", new ConfigScalar("${MISSING}")) })) });
        var result = r.Resolve(map);
        Assert.Single(result.Diagnostics);
        Assert.Equal("app.secret_key", result.Diagnostics[0].ConfigPath);
    }

    [Fact]
    public void EnvRef_ResolvesInMap()
    {
        var src = new TestEnvSrc(new Dictionary<string, string> { { "PORT", "3000" } });
        var r = new EnvironmentReferenceResolver(src, true);
        var map = new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("port", new ConfigScalar("${PORT}")) });
        var result = r.Resolve(map);
        var m = result.Root as ConfigMap;
        Assert.NotNull(m);
        Assert.Equal("3000", ((ConfigScalar)m["port"]).Value);
    }

    [Fact]
    public void EnvRef_ResolvesInSequence()
    {
        var src = new TestEnvSrc(new Dictionary<string, string> { { "HOST", "localhost" } });
        var r = new EnvironmentReferenceResolver(src, true);
        var result = r.Resolve(new ConfigSequence(new ConfigNode[] { new ConfigScalar("${HOST}") }));
        var seq = result.Root as ConfigSequence;
        Assert.NotNull(seq);
        Assert.Equal("localhost", ((ConfigScalar)seq[0]).Value);
    }

    [Fact]
    public void EnvRef_NoVarNoChange()
    {
        var r = new EnvironmentReferenceResolver(new TestEnvSrc(new Dictionary<string, string>()), true);
        var result = r.Resolve(new ConfigScalar("plain text"));
        Assert.Equal("plain text", ((ConfigScalar)result.Root).Value);
    }

    [Fact]
    public void EnvRef_MultipleCalls_DiagnosticsNotAccumulated()
    {
        var src = new TestEnvSrc(new Dictionary<string, string>());
        var r = new EnvironmentReferenceResolver(src, false);

        var r1 = r.Resolve(new ConfigScalar("${MISSING}"));
        Assert.Single(r1.Diagnostics);

        var r2 = r.Resolve(new ConfigScalar("plain"));
        Assert.Empty(r2.Diagnostics);
    }

    [Fact]
    public void EnvRef_DiagnosticsDoNotContainSecrets()
    {
        var src = new TestEnvSrc(new Dictionary<string, string>());
        var r = new EnvironmentReferenceResolver(src, false);
        var result = r.Resolve(new ConfigScalar("${SECRET_VAR}"));
        foreach (var d in result.Diagnostics)
        {
            Assert.Equal("SECRET_VAR", d.EnvironmentVariableName);
        }
    }

    [Fact]
    public void ProcessEnvSrc_ReturnsValue()
    {
        var key = "KJ_TEST_" + Guid.NewGuid().ToString("N")[..8];
        Environment.SetEnvironmentVariable(key, "val");
        try
        {
            Assert.Equal("val", new ProcessEnvironmentValueSource().GetValue(key));
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void ProcessEnvSrc_Missing_ReturnsNull()
    {
        Assert.Null(new ProcessEnvironmentValueSource().GetValue("KJ_NONEXIST_" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public void CompositeEnvSrc_FirstWins()
    {
        var c = new CompositeEnvironmentValueSource(
            new TestEnvSrc(new Dictionary<string, string> { { "V", "first" } }),
            new TestEnvSrc(new Dictionary<string, string> { { "V", "second" } }));
        Assert.Equal("first", c.GetValue("V"));
    }

    [Fact]
    public void CompositeEnvSrc_Fallback()
    {
        var c = new CompositeEnvironmentValueSource(
            new TestEnvSrc(new Dictionary<string, string>()),
            new TestEnvSrc(new Dictionary<string, string> { { "V", "fallback" } }));
        Assert.Equal("fallback", c.GetValue("V"));
    }

    [Fact]
    public void CompositeEnvSrc_AllMissing_ReturnsNull()
    {
        var c = new CompositeEnvironmentValueSource(
            new TestEnvSrc(new Dictionary<string, string>()),
            new TestEnvSrc(new Dictionary<string, string>()));
        Assert.Null(c.GetValue("MISSING"));
    }

    [Fact]
    public void DotEnv_ReadsFile()
    {
        using var td = new TempDir();
        File.WriteAllLines(Path.Combine(td.Path, ".env"), new[] { "KEY=value", "E=", "# comment" });
        var s = new DotEnvStore(Path.Combine(td.Path, ".env"));
        Assert.Equal("value", s.GetValue("KEY"));
        Assert.Equal("", s.GetValue("E"));
    }

    [Fact]
    public void DotEnv_CaseInsensitiveKeys()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, ".env"), "API_KEY=secret\n");
        var s = new DotEnvStore(Path.Combine(td.Path, ".env"));
        Assert.Equal("secret", s.GetValue("api_key"));
    }

    [Fact]
    public void DotEnv_MissingFile_ReturnsEmpty()
    {
        using var td = new TempDir();
        var s = new DotEnvStore(Path.Combine(td.Path, ".env"));
        Assert.Null(s.GetValue("ANY"));
    }

    [Fact]
    public void DotEnv_GetSnapshot_ReadOnlyCopy()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, ".env"), "K=v\n");
        var s = new DotEnvStore(Path.Combine(td.Path, ".env"));
        var snap = s.GetSnapshot();
        Assert.IsNotType<Dictionary<string, string>>(snap);
        Assert.Equal("v", snap["K"]);
    }

    [Fact]
    public void DotEnv_NullCharacters_Rejected()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, ".env"), "K=val\0ue\n");
        Assert.Throws<KejiConfigurationException>(() => new DotEnvStore(Path.Combine(td.Path, ".env")));
    }

    [Fact]
    public void DotEnv_InvalidVarName_Throws()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, ".env"), "1INVALID=val\n");
        Assert.Throws<KejiConfigurationException>(() => new DotEnvStore(Path.Combine(td.Path, ".env")));
    }

    [Fact]
    public void DotEnv_ExportPrefix_Throws()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, ".env"), "export KEY=val\n");
        Assert.Throws<KejiConfigurationException>(() => new DotEnvStore(Path.Combine(td.Path, ".env")));
    }

    [Fact]
    public void DotEnv_DuplicateKey_Throws()
    {
        using var td = new TempDir();
        File.WriteAllLines(Path.Combine(td.Path, ".env"), new[] { "K=first", "K=second" });
        Assert.Throws<KejiConfigurationException>(() => new DotEnvStore(Path.Combine(td.Path, ".env")));
    }

    [Fact]
    public void DotEnv_ExceedsMaxLineLength_Throws()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, ".env"), $"K={new string('x', 200)}\n");
        Assert.Throws<KejiConfigurationException>(() => new DotEnvStore(Path.Combine(td.Path, ".env"), maxLineLength: 50));
    }

    [Fact]
    public void DotEnv_QuotedValues_StripsQuotes()
    {
        using var td = new TempDir();
        File.WriteAllLines(Path.Combine(td.Path, ".env"), new[] { "DQ=\"double\"", "SQ='single'" });
        var s = new DotEnvStore(Path.Combine(td.Path, ".env"));
        Assert.Equal("double", s.GetValue("DQ"));
        Assert.Equal("single", s.GetValue("SQ"));
    }

    [Fact]
    public void DotEnv_ExceedsMaxSize_Throws()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, ".env"), new string('x', 200));
        Assert.Throws<KejiConfigurationException>(() => new DotEnvStore(Path.Combine(td.Path, ".env"), maxFileBytes: 50));
    }

    [Fact]
    public void DotEnv_DoesNotModifyProcessEnv()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, ".env"), "PATH=should_not_affect\n");
        var s = new DotEnvStore(Path.Combine(td.Path, ".env"));
        Assert.Equal("should_not_affect", s.GetValue("PATH"));
        Assert.NotEqual("should_not_affect", Environment.GetEnvironmentVariable("PATH"));
    }

    [Fact]
    public async Task DotEnv_Upsert_CreatesNewKey()
    {
        using var td = new TempDir();
        var s = new DotEnvStore(Path.Combine(td.Path, ".env"));
        var r = await s.UpsertAsync("NEW_KEY", "new_value");
        Assert.True(r.Created);
        Assert.False(r.Updated);
        Assert.Equal("new_value", new DotEnvStore(Path.Combine(td.Path, ".env")).GetValue("NEW_KEY"));
    }

    [Fact]
    public async Task DotEnv_Upsert_UpdatesExistingKey()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, ".env"), "KEY=original\n");
        var s = new DotEnvStore(Path.Combine(td.Path, ".env"));
        var r = await s.UpsertAsync("KEY", "updated");
        Assert.False(r.Created);
        Assert.True(r.Updated);
        Assert.Equal("updated", new DotEnvStore(Path.Combine(td.Path, ".env")).GetValue("KEY"));
    }

    [Fact]
    public async Task DotEnv_Upsert_RejectsCR() { using var td = new TempDir(); var s = new DotEnvStore(Path.Combine(td.Path, ".env")); await Assert.ThrowsAsync<ArgumentException>(() => s.UpsertAsync("K", "va\rue")); }
    [Fact]
    public async Task DotEnv_Upsert_RejectsLF() { using var td = new TempDir(); var s = new DotEnvStore(Path.Combine(td.Path, ".env")); await Assert.ThrowsAsync<ArgumentException>(() => s.UpsertAsync("K", "va\nue")); }
    [Fact]
    public async Task DotEnv_Upsert_RejectsNUL() { using var td = new TempDir(); var s = new DotEnvStore(Path.Combine(td.Path, ".env")); await Assert.ThrowsAsync<ArgumentException>(() => s.UpsertAsync("K", "va\0ue")); }

    [Fact]
    public async Task DotEnv_Upsert_PreservesComments()
    {
        using var td = new TempDir();
        var p = Path.Combine(td.Path, ".env");
        File.WriteAllLines(p, new[] { "# comment", "KEY=value" });
        var s = new DotEnvStore(p);
        await s.UpsertAsync("KEY", "updated");
        var content = File.ReadAllText(p);
        Assert.Contains("# comment", content);
    }

    [Fact]
    public async Task DotEnv_Upsert_PreservesBlankLines()
    {
        using var td = new TempDir();
        var p = Path.Combine(td.Path, ".env");
        File.WriteAllLines(p, new[] { "A=1", "", "B=2" });
        var s = new DotEnvStore(p);
        await s.UpsertAsync("B", "updated");
        var lines = File.ReadAllLines(p);
        var aIdx = Array.IndexOf(lines, "A=1");
        var bIdx = Array.IndexOf(lines, "B=updated");
        Assert.True(aIdx >= 0 && bIdx >= 0);
        Assert.True(bIdx - aIdx > 1, "Blank line between A and B was not preserved");
    }

    [Fact]
    public async Task DotEnv_Upsert_PreservesOrder()
    {
        using var td = new TempDir();
        var p = Path.Combine(td.Path, ".env");
        File.WriteAllLines(p, new[] { "A=1", "B=2", "C=3" });
        var s = new DotEnvStore(p);
        await s.UpsertAsync("B", "updated");
        var lines = File.ReadAllLines(p);
        var aIdx = Array.IndexOf(lines, "A=1");
        var bIdx = Array.IndexOf(lines, "B=updated");
        var cIdx = Array.IndexOf(lines, "C=3");
        Assert.True(aIdx < bIdx && bIdx < cIdx);
    }

    [Fact]
    public async Task DotEnv_Upsert_NoDuplicateKeys()
    {
        using var td = new TempDir();
        var p = Path.Combine(td.Path, ".env");
        File.WriteAllText(p, "K=original\n");
        var s = new DotEnvStore(p);
        await s.UpsertAsync("K", "updated");
        await s.UpsertAsync("K", "final");
        var cnt = File.ReadAllLines(p).Count(l => l.StartsWith("K="));
        Assert.Equal(1, cnt);
    }

    [Fact]
    public async Task DotEnv_Upsert_NewKeyAppendedToEnd()
    {
        using var td = new TempDir();
        var p = Path.Combine(td.Path, ".env");
        File.WriteAllLines(p, new[] { "A=1" });
        var s = new DotEnvStore(p);
        await s.UpsertAsync("B", "2");
        var lines = File.ReadAllLines(p);
        Assert.Equal("B=2", lines[^1]);
    }

    [Fact]
    public async Task DotEnv_Upsert_ResultNoValue()
    {
        using var td = new TempDir();
        var s = new DotEnvStore(Path.Combine(td.Path, ".env"));
        var r = await s.UpsertAsync("S", "secret_val");
        Assert.Equal("S", r.Key);
        Assert.Null(r.GetType().GetProperty("Value"));
    }

    [Fact]
    public async Task DotEnv_Upsert_NoBOM()
    {
        using var td = new TempDir();
        var p = Path.Combine(td.Path, ".env");
        var s = new DotEnvStore(p);
        await s.UpsertAsync("K", "v");
        var bytes = File.ReadAllBytes(p);
        if (bytes.Length >= 3)
            Assert.NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
    }

    [Fact]
    public async Task DotEnv_Upsert_LongLineRejected_RollsBack()
    {
        using var td = new TempDir();
        var p = Path.Combine(td.Path, ".env");
        File.WriteAllText(p, "OLD=keep\n");
        var s = new DotEnvStore(p, maxLineLength: 10);

        var ex = await Assert.ThrowsAsync<KejiConfigurationException>(() => s.UpsertAsync("KEY", "very_long_value_here"));
        Assert.Contains("length", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("keep", s.GetValue("OLD"));
        Assert.Equal("keep", new DotEnvStore(p).GetValue("OLD"));
        Assert.Null(s.GetValue("KEY"));
    }

    [Fact]
    public async Task DotEnv_Upsert_TotalSizeExceeded_RollsBack()
    {
        using var td = new TempDir();
        var p = Path.Combine(td.Path, ".env");
        File.WriteAllText(p, "OLD=keep\n");
        var s = new DotEnvStore(p, maxFileBytes: 50);

        var ex = await Assert.ThrowsAsync<KejiConfigurationException>(() => s.UpsertAsync("KEY", new string('x', 100)));
        Assert.Contains("size", ex.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("keep", s.GetValue("OLD"));
        Assert.Equal("keep", new DotEnvStore(p).GetValue("OLD"));
    }

    [Fact]
    public async Task DotEnv_Upsert_Cancellation_RollsBack()
    {
        using var td = new TempDir();
        var p = Path.Combine(td.Path, ".env");
        File.WriteAllText(p, "OLD=keep\n");
        var s = new DotEnvStore(p);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => s.UpsertAsync("KEY", "value", cts.Token));

        Assert.Equal("keep", s.GetValue("OLD"));
        Assert.Null(s.GetValue("KEY"));
        var content = File.ReadAllText(p);
        Assert.DoesNotContain("KEY=", content);
    }

    [Fact]
    public void Provider_Deepseek_Correct() { Assert.Equal("DEEPSEEK_API_KEY", ProviderSecretName.GetEnvironmentVariableName("deepseek")); }
    [Fact]
    public void Provider_OpenAI_Correct() { Assert.Equal("OPENAI_API_KEY", ProviderSecretName.GetEnvironmentVariableName("openai")); }
    [Fact]
    public void Provider_CaseInsensitive() { Assert.Equal("DEEPSEEK_API_KEY", ProviderSecretName.GetEnvironmentVariableName("DeepSeek")); }
    [Fact]
    public void Provider_Generic_Hyphen() { Assert.Equal("MY_PROVIDER_API_KEY", ProviderSecretName.GetEnvironmentVariableName("my-provider")); }
    [Fact]
    public void Provider_Generic_Underscore() { Assert.Equal("AZURE_OPENAI_API_KEY", ProviderSecretName.GetEnvironmentVariableName("azure_openai")); }
    [Fact]
    public void Provider_Empty_Throws() { Assert.Throws<ArgumentException>(() => ProviderSecretName.GetEnvironmentVariableName("")); }
    [Fact]
    public void Provider_Space_Throws() { Assert.Throws<ArgumentException>(() => ProviderSecretName.GetEnvironmentVariableName("my provider")); }
    [Fact]
    public void Provider_Newline_Throws() { Assert.Throws<ArgumentException>(() => ProviderSecretName.GetEnvironmentVariableName("my\nprovider")); }
    [Fact]
    public void Provider_Equals_Throws() { Assert.Throws<ArgumentException>(() => ProviderSecretName.GetEnvironmentVariableName("my=provider")); }
    [Fact]
    public void Provider_Slash_Throws() { Assert.Throws<ArgumentException>(() => ProviderSecretName.GetEnvironmentVariableName("my/provider")); }
    [Fact]
    public void Provider_Backslash_Throws() { Assert.Throws<ArgumentException>(() => ProviderSecretName.GetEnvironmentVariableName("my\\provider")); }
    [Fact]
    public void Provider_Dot_Throws() { Assert.Throws<ArgumentException>(() => ProviderSecretName.GetEnvironmentVariableName("my.provider")); }
    [Fact]
    public void Provider_Colon_Throws() { Assert.Throws<ArgumentException>(() => ProviderSecretName.GetEnvironmentVariableName("my:provider")); }

    [Fact]
    public void Mask_MasksPassword()
    {
        var m = new SecretMasker();
        var r = m.Mask(new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("password", new ConfigScalar("secret123")) })) as ConfigMap;
        Assert.NotNull(r);
        Assert.Equal("***", ((ConfigScalar)r["password"]).Value);
    }

    [Fact]
    public void Mask_MasksApiKey()
    {
        var m = new SecretMasker();
        var r = m.Mask(new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("api_key", new ConfigScalar("sk-abc")) })) as ConfigMap;
        Assert.NotNull(r);
        Assert.Equal("***", ((ConfigScalar)r["api_key"]).Value);
    }

    [Fact]
    public void Mask_NonSensitive_Unchanged()
    {
        var m = new SecretMasker();
        var r = m.Mask(new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("name", new ConfigScalar("keji")) })) as ConfigMap;
        Assert.NotNull(r);
        Assert.Equal("keji", ((ConfigScalar)r["name"]).Value);
    }

    [Fact]
    public void Mask_NestedMap()
    {
        var m = new SecretMasker();
        var inner = new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("token", new ConfigScalar("eyJhbGci")), new KeyValuePair<string, ConfigNode>("public", new ConfigScalar("visible")) });
        var r = m.Mask(new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("app", inner) })) as ConfigMap;
        Assert.NotNull(r);
        var app = r["app"] as ConfigMap;
        Assert.NotNull(app);
        Assert.Equal("***", ((ConfigScalar)app["token"]).Value);
        Assert.Equal("visible", ((ConfigScalar)app["public"]).Value);
    }

    [Fact]
    public void Mask_AllRequiredKeys()
    {
        var keys = new[] { "api_key", "apikey", "app_secret", "client_secret", "secret", "password", "token", "access_token", "refresh_token", "verification_token", "encrypt_key", "work_secret", "jwt_secret", "private_key", "connection_string" };
        var m = new SecretMasker();
        var r = m.Mask(new ConfigMap(keys.Select(k => new KeyValuePair<string, ConfigNode>(k, new ConfigScalar("v"))))) as ConfigMap;
        Assert.NotNull(r);
        foreach (var k in keys) Assert.Equal("***", ((ConfigScalar)r[k]).Value);
    }

    [Fact]
    public void Mask_Dictionary()
    {
        var m = new SecretMasker();
        var r = m.Mask(new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?> { { "password", "secret" }, { "name", "keji" } }));
        Assert.Equal("***", r["password"]);
        Assert.Equal("keji", r["name"]);
    }

    [Fact]
    public void Mask_List()
    {
        var m = new SecretMasker();
        var inner = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?> { { "password", "s1" } });
        var r = m.Mask(new List<object?> { inner });
        var item = r[0] as IReadOnlyDictionary<string, object?>;
        Assert.NotNull(item);
        Assert.Equal("***", item["password"]);
    }

    [Fact]
    public void Mask_ToStringNoLeak()
    {
        var m = new SecretMasker();
        var r = m.Mask(new ConfigMap(new[] { new KeyValuePair<string, ConfigNode>("password", new ConfigScalar("super_secret_value")) })) as ConfigMap;
        Assert.NotNull(r);
        var scalar = r["password"] as ConfigScalar;
        Assert.NotNull(scalar);
        Assert.DoesNotContain("super_secret_value", scalar.ToString());
    }

    [Fact]
    public void MaskApiKey_Null_NotConfigured()
    {
        var mask = SecretMasker.MaskApiKeyForSettings(null);
        Assert.False(mask.IsConfigured);
        Assert.Equal("", mask.DisplayValue);
    }

    [Fact]
    public void MaskApiKey_Empty_NotConfigured()
    {
        var mask = SecretMasker.MaskApiKeyForSettings("");
        Assert.False(mask.IsConfigured);
        Assert.Equal("", mask.DisplayValue);
    }

    [Fact]
    public void MaskApiKey_AnyValue_Configured()
    {
        var mask = SecretMasker.MaskApiKeyForSettings("sk-abc123");
        Assert.True(mask.IsConfigured);
        Assert.Equal("***", mask.DisplayValue);
    }

    [Fact]
    public void MaskApiKey_NoLeak()
    {
        var mask = SecretMasker.MaskApiKeyForSettings("sk-abc123xyz");
        Assert.Equal("***", mask.DisplayValue);
        Assert.DoesNotContain("sk-", mask.DisplayValue);
    }

    [Fact]
    public void FullLoader_ResolvesFromDotEnv()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, ".env"), "TEST_SECRET=from_dotenv\n");
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "security:\n  jwt_secret: ${TEST_SECRET}\n");

        var loader = new KejiConfigurationLoader(new SafeYamlConfigurationLoader());

        var result = loader.Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true });

        Assert.Equal("from_dotenv", result.Document.GetRequiredString("security.jwt_secret"));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void FullLoader_ProcessEnvOverridesDotEnv()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, ".env"), "TEST_SECRET=from_dotenv\n");
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "security:\n  jwt_secret: ${TEST_SECRET}\n");

        var envKey = "TEST_SECRET";
        Environment.SetEnvironmentVariable(envKey, "from_process");
        try
        {
            var loader = new KejiConfigurationLoader(new SafeYamlConfigurationLoader());

            var result = loader.Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true });

            Assert.Equal("from_process", result.Document.GetRequiredString("security.jwt_secret"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, null);
        }
    }

    [Fact]
    public void FullLoader_NonStrict_ReturnsDiagnostics()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "key: ${MISSING_VAR}\n");

        var loader = new KejiConfigurationLoader(new SafeYamlConfigurationLoader());

        var result = loader.Load(new KejiConfigurationLoadOptions
        {
            ProjectRoot = td.Path,
            RequireConfigFile = true,
            FailOnMissingEnvironmentVariable = false,
        });

        Assert.Equal(string.Empty, result.Document.GetOptionalString("key"));
        Assert.NotEmpty(result.Diagnostics);
        Assert.Equal("MISSING_VAR", result.Diagnostics[0].EnvironmentVariableName);
    }

    [Fact]
    public void FullLoader_StrictMode_ThrowsWithDottedPath()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "security:\n  jwt_secret: ${MISSING}\n");

        var loader = new KejiConfigurationLoader(new SafeYamlConfigurationLoader());

        var ex = Assert.Throws<KejiConfigurationException>(() =>
            loader.Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true }));

        Assert.Contains("MISSING", ex.Message);
    }

    [Fact]
    public void FullLoader_DoesNotModifyProcessEnv()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, ".env"), "PATH=should_not_leak\n");
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "key: value\n");

        var loader = new KejiConfigurationLoader(new SafeYamlConfigurationLoader());

        loader.Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true });

        Assert.NotEqual("should_not_leak", Environment.GetEnvironmentVariable("PATH"));
    }

    [Fact]
    public void LoaderUsesOptionsPassedToEachCall()
    {
        using var root = new TempDir();
        var projectA = System.IO.Path.Combine(root.Path, "ProjectA");
        var projectB = System.IO.Path.Combine(root.Path, "ProjectB");
        Directory.CreateDirectory(projectA);
        Directory.CreateDirectory(projectB);

        File.WriteAllText(System.IO.Path.Combine(projectA, ".env"), "TEST_SECRET=secret_a\n");
        File.WriteAllText(System.IO.Path.Combine(projectA, "config.yaml"), "value: ${TEST_SECRET}\n");
        File.WriteAllText(System.IO.Path.Combine(projectB, ".env"), "TEST_SECRET=secret_b\n");
        File.WriteAllText(System.IO.Path.Combine(projectB, "config.yaml"), "value: ${TEST_SECRET}\n");

        var loader = new KejiConfigurationLoader(new SafeYamlConfigurationLoader());

        var resultA = loader.Load(new KejiConfigurationLoadOptions { ProjectRoot = projectA, RequireConfigFile = true });
        Assert.Equal("secret_a", resultA.Document.GetRequiredString("value"));

        var resultB = loader.Load(new KejiConfigurationLoadOptions { ProjectRoot = projectB, RequireConfigFile = true });
        Assert.Equal("secret_b", resultB.Document.GetRequiredString("value"));
    }

    [Fact]
    public void LoaderReadsFreshDotEnvOnEveryLoad()
    {
        using var td = new TempDir();
        var envPath = System.IO.Path.Combine(td.Path, ".env");
        var configPath = System.IO.Path.Combine(td.Path, "config.yaml");

        File.WriteAllText(envPath, "TEST_SECRET=old_value\n");
        File.WriteAllText(configPath, "value: ${TEST_SECRET}\n");

        var loader = new KejiConfigurationLoader(new SafeYamlConfigurationLoader());
        var opts = new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true };

        var result1 = loader.Load(opts);
        Assert.Equal("old_value", result1.Document.GetRequiredString("value"));

        File.WriteAllText(envPath, "TEST_SECRET=new_value\n");

        var result2 = loader.Load(opts);
        Assert.Equal("new_value", result2.Document.GetRequiredString("value"));
    }

    [Fact]
    public void DI_Lazy_IllegalDotEnv_ResolveLoaderDoesNotThrow_LoadThrows()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, ".env"), "1INVALID=value\n");
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "key: value\n");

        var services = new ServiceCollection();
        services.AddKejiConfigurationFoundation(o =>
        {
            o.ProjectRoot = td.Path;
            o.RequireConfigFile = false;
        });

        var sp = services.BuildServiceProvider();

        var loader = sp.GetRequiredService<IKejiConfigurationLoader>();

        Assert.Throws<KejiConfigurationException>(() =>
            loader.Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = false }));
    }

    [Fact]
    public void DI_Lazy_MalformedConfig_ResolveLoaderDoesNotThrow_LoadThrows()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "key: value\nunbalanced: [\n");

        var services = new ServiceCollection();
        services.AddKejiConfigurationFoundation(o =>
        {
            o.ProjectRoot = td.Path;
            o.RequireConfigFile = false;
        });

        var sp = services.BuildServiceProvider();

        var loader = sp.GetRequiredService<IKejiConfigurationLoader>();

        Assert.Throws<KejiConfigurationException>(() =>
            loader.Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true }));
    }

    [Fact]
    public void MalformedYamlException_MessageNoSecret()
    {
        using var td = new TempDir();
        File.WriteAllText(Path.Combine(td.Path, "config.yaml"), "password: my_secret_value\nsecret: ${MISSING}\nkey: *bad_alias\n");
        var loader = new SafeYamlConfigurationLoader();
        var ex = Assert.Throws<KejiConfigurationException>(() =>
            loader.Load(new KejiConfigurationLoadOptions { ProjectRoot = td.Path, RequireConfigFile = true }));
        Assert.DoesNotContain("my_secret_value", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("my_secret_value", ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SafeYamlLoader_OverrideConfigPath_Works()
    {
        using var td = new TempDir();
        var customPath = Path.Combine(td.Path, "custom.yaml");
        File.WriteAllText(customPath, "key: val\n");
        var loader = new SafeYamlConfigurationLoader();
        var node = loader.Load(new KejiConfigurationLoadOptions { RequireConfigFile = false }, customPath);
        var map = node as ConfigMap;
        Assert.NotNull(map);
        Assert.Equal("val", ((ConfigScalar)map["key"]).Value);
    }

    private static string BuildDeepYaml(int depth)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("a:\n");
        for (int i = 0; i < depth; i++) { sb.Append(new string(' ', (i + 1) * 2)); sb.Append("a:\n"); }
        sb.Append(new string(' ', (depth + 1) * 2)); sb.Append("v: x\n");
        return sb.ToString();
    }

    private sealed class TestEnvSrc : IEnvironmentValueSource
    {
        private readonly Dictionary<string, string> _v;
        public TestEnvSrc(Dictionary<string, string> v) { _v = new Dictionary<string, string>(v, StringComparer.OrdinalIgnoreCase); }
        public string? GetValue(string n) => _v.TryGetValue(n, out var val) ? val : null;
    }
}

internal sealed class TempDir : IDisposable
{
    public string Path { get; }
    public TempDir() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KJ_TEST_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
    public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
}
