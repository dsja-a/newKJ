using Keji.Providers;

namespace Keji.Providers.Tests;

public class ModelProviderRegistryTests
{
    [Fact]
    public void Registry_Empty_HasNoProviders()
    {
        var registry = new ModelProviderRegistry(new Dictionary<string, IModelProvider>());
        Assert.Empty(registry.RegisteredProviders);
    }

    [Fact]
    public void Registry_GetProvider_ReturnsNullForUnknown()
    {
        var registry = new ModelProviderRegistry(new Dictionary<string, IModelProvider>());
        Assert.Null(registry.GetProvider("nonexistent"));
    }

    [Fact]
    public void Registry_GetProvider_ReturnsRegistered()
    {
        var mock = new MockModelProvider("openai");
        var registry = new ModelProviderRegistry(new Dictionary<string, IModelProvider> { ["openai"] = mock });
        Assert.Same(mock, registry.GetProvider("openai"));
    }

    [Fact]
    public void Registry_RegisteredProviders_ContainsKeys()
    {
        var registry = new ModelProviderRegistry(new Dictionary<string, IModelProvider>
        {
            ["openai"] = new MockModelProvider("openai"),
            ["deepseek"] = new MockModelProvider("deepseek")
        });
        Assert.Equal(2, registry.RegisteredProviders.Count);
        Assert.Contains("openai", registry.RegisteredProviders);
        Assert.Contains("deepseek", registry.RegisteredProviders);
    }

    [Fact]
    public void Provider_ProviderName_Matches()
    {
        var mock = new MockModelProvider("test-provider");
        Assert.Equal("test-provider", mock.ProviderName);
    }

    private sealed class MockModelProvider : IModelProvider
    {
        public string ProviderName { get; }
        public MockModelProvider(string name) => ProviderName = name;
        public Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default)
            => Task.FromResult(ChatCompletionResponse.Succeeded("mock"));
        public async IAsyncEnumerable<ChatCompletionStreamEvent> StreamAsync(ChatCompletionRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return ChatCompletionStreamEvent.Done();
        }
    }
}
