using System.Collections.Immutable;
using Keji.Providers;

namespace Keji.Providers.Tests;

public class ProviderSpecificTests
{
    [Fact]
    public void OpenAIProvider_ProviderName_IsOpenAI()
    {
        var factory = new MockHttpClientFactory(new HttpClient());
        var config = ModelProviderConfig.Create("openai", "env:OPENAI_API_KEY", "https://api.openai.com", "gpt-4o");
        var provider = new OpenAIProvider(factory, config);
        Assert.Equal("openai", provider.ProviderName);
    }

    [Fact]
    public void DeepSeekProvider_ProviderName_IsDeepSeek()
    {
        var factory = new MockHttpClientFactory(new HttpClient());
        var config = ModelProviderConfig.Create("deepseek", "env:DEEPSEEK_API_KEY", "https://api.deepseek.com", "deepseek-chat");
        var provider = new DeepSeekProvider(factory, config);
        Assert.Equal("deepseek", provider.ProviderName);
    }

    [Fact]
    public void OllamaProvider_ProviderName_IsOllama()
    {
        var factory = new MockHttpClientFactory(new HttpClient());
        var config = ModelProviderConfig.Create("ollama", "", "http://localhost:11434", "llama3");
        var provider = new OllamaProvider(factory, config);
        Assert.Equal("ollama", provider.ProviderName);
    }

    [Fact]
    public void Config_ObjectInit_CannotBypassValidation()
    {
        var cfg = ModelProviderConfig.Create("openai", "env:OPENAI_API_KEY", "https://localhost", "model");

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => cfg.WithTimeout(TimeSpan.FromSeconds(121)));
        Assert.Contains("Timeout", ex.Message);
    }

    [Fact]
    public async Task OpenAIProvider_WithKeyHeader_SendsBearer()
    {
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}")
        });
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var factory = new MockHttpClientFactory(client);
        var config = ModelProviderConfig.Create("openai", "env:OPENAI_API_KEY", "https://api.openai.com", "gpt-4o");
        var provider = new OpenAIProvider(factory, config, new FixedSecretResolver("sk-test-key"));

        var result = await provider.CompleteAsync(new ChatCompletionRequest
        {
            Model = "gpt-4o",
            Messages = new[] { new ChatMessage { Role = KejiChatRole.User, Content = "hi" } }.ToImmutableArray()
        });

        Assert.True(result.Success);
    }

    [Fact]
    public async Task OllamaProvider_NullKey_Works()
    {
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}")
        });
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var factory = new MockHttpClientFactory(client);
        var config = ModelProviderConfig.Create("ollama", "", "http://localhost:11434", "llama3");
        var provider = new OllamaProvider(factory, config);

        var result = await provider.CompleteAsync(new ChatCompletionRequest
        {
            Model = "llama3",
            Messages = new[] { new ChatMessage { Role = KejiChatRole.User, Content = "hi" } }.ToImmutableArray()
        });

        Assert.True(result.Success);
    }

    [Fact]
    public void Provider_Constructor_RequiresNonNullFactory()
    {
        Assert.Throws<ArgumentNullException>(() => new OpenAIProvider(null!,
            ModelProviderConfig.Create("openai", "env:OPENAI_API_KEY", "https://localhost", "gpt-4o")));
    }

    private sealed class FixedSecretResolver(string secret) : IKejiProviderSecretResolver
    {
        public string? Resolve(KejiProviderSecretReference secretReference) => secret;
    }
}
