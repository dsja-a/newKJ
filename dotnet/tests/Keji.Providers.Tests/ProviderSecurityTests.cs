using System.Net;
using System.Text.Json;
using Keji.Providers;

namespace Keji.Providers.Tests;

public class ProviderSecurityTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Fact]
    public async Task ErrorResponse_DoesNotLeakRawBody()
    {
        var json = JsonSerializer.Serialize(new { error = new { message = "Secret API key invalid: sk-12345" } }, JsonOpts);
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent(json) });
        var provider = CreateMockProvider(factory);

        var result = await provider.CompleteAsync(MakeRequest());

        Assert.False(result.Success);
        Assert.DoesNotContain("sk-12345", result.ErrorMessage);
        Assert.DoesNotContain("Secret", result.ErrorMessage);
    }

    [Fact]
    public async Task ErrorResponse_DoesNotLeakExceptionMessage()
    {
        var handler = new MockHttpMessageHandler(new HttpRequestException("API Key: sk-12345 rejected"));
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var factory = new MockHttpClientFactory(client);
        var provider = CreateMockProvider(factory);

        var result = await provider.CompleteAsync(MakeRequest());

        Assert.False(result.Success);
        Assert.DoesNotContain("sk-12345", result.ErrorMessage);
    }

    [Fact]
    public async Task ErrorResponse_DoesNotLeakProviderResponseBodyInStream()
    {
        var json = JsonSerializer.Serialize(new { error = new { message = "Key: sk-abc123" } }, JsonOpts);
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(json) });
        var provider = CreateMockProvider(factory);

        var results = new List<ChatCompletionStreamEvent>();
        await foreach (var e in provider.StreamAsync(MakeRequest()))
            results.Add(e);

        var error = results.FirstOrDefault(r => r.Type == ChatCompletionStreamEventType.Error);
        Assert.NotNull(error);
        Assert.DoesNotContain("sk-abc123", error.ErrorMessage);
    }

    [Fact]
    public async Task SuccessResponse_DoesNotLeakRequestContent()
    {
        var json = JsonSerializer.Serialize(new
        {
            choices = new[] { new { index = 0, message = new { role = "assistant", content = "Hello" }, finish_reason = "stop" } }
        }, JsonOpts);
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        var provider = CreateMockProvider(factory);

        var result = await provider.CompleteAsync(new ChatCompletionRequest
        {
            Model = "test",
            Messages = new[] { new ChatMessage { Role = "user", Content = "My password is secret123" } }
        });

        Assert.True(result.Success);
        Assert.DoesNotContain("secret123", result.Content);
    }

    [Fact]
    public void ProviderConfig_DoesNotExposeApiKeyInToString()
    {
        var cfg = ModelProviderConfig.Create("openai", "sk-secret-key-12345", "https://api.openai.com", "gpt-4o");
        var str = cfg.ToString();

        Assert.DoesNotContain("sk-secret-key-12345", str);
    }

    [Fact]
    public async Task ErrorFromStream_DoesNotLeakRawProviderBody()
    {
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"error\":{\"message\":\"Forbidden: key sk-xxx\"}}")
        });
        var provider = CreateMockProvider(factory);

        var results = new List<ChatCompletionStreamEvent>();
        await foreach (var e in provider.StreamAsync(MakeRequest()))
            results.Add(e);

        var error = results.FirstOrDefault(r => r.Type == ChatCompletionStreamEventType.Error);
        Assert.NotNull(error);
        Assert.DoesNotContain("sk-xxx", error.ErrorMessage);
    }

    private static IHttpClientFactory CreateFactory(HttpResponseMessage response)
    {
        var handler = new MockHttpMessageHandler(response);
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        return new MockHttpClientFactory(client);
    }

    private static MockProvider CreateMockProvider(IHttpClientFactory factory) =>
        new(factory, TimeSpan.FromSeconds(10));

    private static ChatCompletionRequest MakeRequest() => new()
    {
        Model = "test",
        Messages = new[] { new ChatMessage { Role = "user", Content = "hello" } }
    };

    private sealed class MockProvider : ProviderBase
    {
        public override string ProviderName => "mock";
        protected override string BaseUri => "http://localhost/";
        protected override System.Net.Http.Headers.AuthenticationHeaderValue? AuthHeader => null;

        public MockProvider(IHttpClientFactory factory, TimeSpan timeout) : base(factory, timeout, 0) { }
    }
}
