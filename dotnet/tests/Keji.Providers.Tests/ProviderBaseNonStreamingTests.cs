using System.Net;
using System.Text.Json;
using System.Collections.Immutable;
using Keji.Providers;

namespace Keji.Providers.Tests;

public sealed class ProviderBaseNonStreamingTests
{
    private sealed class MockProvider : ProviderBase
    {
        public override string ProviderName => "mock";
        protected override string BaseUri => "http://localhost/";
        protected override System.Net.Http.Headers.AuthenticationHeaderValue? AuthHeader => null;

        public MockProvider(IHttpClientFactory factory, TimeSpan timeout, int maxRetries = 0)
            : base(factory, timeout, maxRetries) { }
    }

    private static IHttpClientFactory CreateFactory(HttpResponseMessage response)
    {
        var handler = new MockHttpMessageHandler(response);
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var factory = new MockHttpClientFactory(client);
        return factory;
    }

    private static ChatCompletionRequest MakeRequest() => new()
    {
        Model = "test-model",
        Messages = new[] { new ChatMessage { Role = KejiChatRole.User, Content = "hello" } }.ToImmutableArray()
    };

    [Fact]
    public async Task CompleteAsync_Success_ReturnsContent()
    {
        var json = JsonSerializer.Serialize(new
        {
            id = "test-id",
            model = "test-model",
            choices = new[] { new { index = 0, message = new { role = "assistant", content = "Hello!" }, finish_reason = "stop" } },
            usage = new { prompt_tokens = 10, completion_tokens = 5 }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var result = await provider.CompleteAsync(MakeRequest());

        Assert.True(result.Success);
        Assert.Equal("Hello!", result.Content);
        Assert.Equal(10, result.Usage!.PromptTokens);
        Assert.Equal(5, result.Usage.CompletionTokens);
    }

    [Fact]
    public async Task CompleteAsync_WithReasoningContent()
    {
        var json = JsonSerializer.Serialize(new
        {
            id = "test-id",
            choices = new[] { new { index = 0, message = new { role = "assistant", content = "Answer", reasoning_content = "Thinking process" }, finish_reason = "stop" } },
            usage = new { prompt_tokens = 5, completion_tokens = 5 }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var result = await provider.CompleteAsync(MakeRequest());

        Assert.Equal("Thinking process", result.ReasoningContent);
    }

    [Fact]
    public async Task CompleteAsync_WithToolCalls()
    {
        var json = JsonSerializer.Serialize(new
        {
            id = "test-id",
            choices = new[] { new { index = 0, message = new { role = "assistant", content = (string?)null, tool_calls = new[] { new { id = "call_1", type = "function", function = new { name = "read_file", arguments = "{\"path\":\"/tmp\"}" } } } }, finish_reason = "tool_calls" } },
            usage = new { prompt_tokens = 10, completion_tokens = 5 }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var result = await provider.CompleteAsync(MakeRequest());

        Assert.False(result.ToolCalls.IsDefaultOrEmpty);
        Assert.Single(result.ToolCalls);
        Assert.Equal("read_file", result.ToolCalls[0].FunctionName);
    }

    [Fact]
    public async Task CompleteAsync_Unauthorized_ReturnsError()
    {
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var result = await provider.CompleteAsync(MakeRequest());

        Assert.False(result.Success);
        Assert.Equal(KejiProviderErrorCode.AuthFailed, result.ErrorCode);
    }

    [Fact]
    public async Task CompleteAsync_RateLimited_ReturnsError()
    {
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var result = await provider.CompleteAsync(MakeRequest());

        Assert.False(result.Success);
        Assert.Equal(KejiProviderErrorCode.RateLimited, result.ErrorCode);
    }

    [Fact]
    public async Task CompleteAsync_ServiceUnavailable_ReturnsError()
    {
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var result = await provider.CompleteAsync(MakeRequest());

        Assert.False(result.Success);
        Assert.Equal(KejiProviderErrorCode.ServiceUnavailable, result.ErrorCode);
    }

    [Fact]
    public async Task CompleteAsync_ServerError500_RetriesThenFails()
    {
        var handler = new MockHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var factory = new MockHttpClientFactory(client);

        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10), maxRetries: 2);

        var result = await provider.CompleteAsync(MakeRequest());

        Assert.False(result.Success);
        Assert.Equal(KejiProviderErrorCode.ServerError, result.ErrorCode);
    }

    [Fact]
    public async Task CompleteAsync_Cancelled_ReturnsCancelled()
    {
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.OK));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await provider.CompleteAsync(MakeRequest(), cts.Token);
        Assert.False(result.Success);
        Assert.Equal(KejiProviderErrorCode.Cancelled, result.ErrorCode);
    }

    [Fact]
    public async Task CompleteAsync_NullResponse_ReturnsError()
    {
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null") });
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var result = await provider.CompleteAsync(MakeRequest());
        Assert.False(result.Success);
    }

    [Fact]
    public async Task CompleteAsync_EmptyJson_ReturnsError()
    {
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var result = await provider.CompleteAsync(MakeRequest());
        Assert.False(result.Success);
    }

    [Fact]
    public async Task CompleteAsync_400Error_ReturnsRequestError()
    {
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.BadRequest));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var result = await provider.CompleteAsync(MakeRequest());
        Assert.False(result.Success);
        Assert.Equal(KejiProviderErrorCode.RequestError, result.ErrorCode);
    }

    [Fact]
    public async Task CompleteAsync_GatewayTimeout_ReturnsError()
    {
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.GatewayTimeout));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var result = await provider.CompleteAsync(MakeRequest());
        Assert.False(result.Success);
        Assert.Equal(KejiProviderErrorCode.GatewayTimeout, result.ErrorCode);
    }

    [Fact]
    public async Task CompleteAsync_NotFound_ReturnsError()
    {
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.NotFound));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var result = await provider.CompleteAsync(MakeRequest());
        Assert.False(result.Success);
        Assert.Equal(KejiProviderErrorCode.EndpointNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task CompleteAsync_HttpRequestException_ReturnsConnectionError()
    {
        var handler = new MockHttpMessageHandler(new HttpRequestException("Connection refused"));
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var factory = new MockHttpClientFactory(client);

        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var result = await provider.CompleteAsync(MakeRequest());
        Assert.False(result.Success);
        Assert.Equal(KejiProviderErrorCode.ConnectionError, result.ErrorCode);
    }

    [Fact]
    public async Task CompleteAsync_WithModelName()
    {
        var json = JsonSerializer.Serialize(new
        {
            id = "test-id",
            model = "gpt-4o",
            choices = new[] { new { index = 0, message = new { role = "assistant", content = "Hi" }, finish_reason = "stop" } }
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var result = await provider.CompleteAsync(MakeRequest());
        Assert.Equal("gpt-4o", result.Model);
    }

    [Fact]
    public async Task CompleteAsync_Timeout_ReturnsError()
    {
        var handler = new MockHttpMessageHandler(new TaskCanceledException("Timeout"));
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var factory = new MockHttpClientFactory(client);

        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var result = await provider.CompleteAsync(MakeRequest());
        Assert.False(result.Success);
    }

    [Fact]
    public async Task CompleteAsync_RetriesOn503ThenSucceeds()
    {
        var handler = new MockHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { index = 0,             message = new { role = "assistant", content = "OK" }, finish_reason = "stop" } }
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }))
            });
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var factory = new MockHttpClientFactory(client);

        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10), maxRetries: 2);

        var result = await provider.CompleteAsync(MakeRequest());
        Assert.True(result.Success);
        Assert.Equal("OK", result.Content);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_400ErrorDoesNotRetry()
    {
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.BadRequest));
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var factory = new MockHttpClientFactory(client);

        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10), maxRetries: 3);

        var result = await provider.CompleteAsync(MakeRequest());
        Assert.False(result.Success);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_RetryOn408ThenSucceeds()
    {
        var handler = new MockHttpMessageHandler(
            new HttpResponseMessage((HttpStatusCode)408),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { index = 0,             message = new { role = "assistant", content = "OK" }, finish_reason = "stop" } }
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }))
            });
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var factory = new MockHttpClientFactory(client);
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10), maxRetries: 1);

        var result = await provider.CompleteAsync(MakeRequest());
        Assert.True(result.Success);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_RetryOn409ThenSucceeds()
    {
        var handler = new MockHttpMessageHandler(
            new HttpResponseMessage((HttpStatusCode)409),
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { index = 0,             message = new { role = "assistant", content = "OK" }, finish_reason = "stop" } }
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }))
            });
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var factory = new MockHttpClientFactory(client);
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10), maxRetries: 2);

        var result = await provider.CompleteAsync(MakeRequest());
        Assert.True(result.Success);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_DoesNotRetryOn400()
    {
        var handler = new MockHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.BadRequest),
            new HttpResponseMessage(HttpStatusCode.OK));
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var factory = new MockHttpClientFactory(client);
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10), maxRetries: 3);

        var result = await provider.CompleteAsync(MakeRequest());
        Assert.False(result.Success);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_CancelDuringStream_ReturnsCancelled()
    {
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.OK));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await provider.CompleteAsync(MakeRequest(), cts.Token);
        Assert.False(result.Success);
        Assert.Equal(KejiProviderErrorCode.Cancelled, result.ErrorCode);
    }

    [Fact]
    public async Task CompleteAsync_429RateLimited_RetriesThenFails()
    {
        var body = JsonSerializer.Serialize(new { error = new { type = "rate_limit_exceeded" } });
        var handler = new MockHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent(body) },
            new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent(body) },
            new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent(body) });
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var factory = new MockHttpClientFactory(client);
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10), maxRetries: 2);

        var result = await provider.CompleteAsync(MakeRequest());

        Assert.False(result.Success);
        Assert.Equal(KejiProviderErrorCode.RateLimited, result.ErrorCode);
        Assert.Equal(3, handler.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_429RateLimited_SucceedsOnRetry()
    {
        var body = JsonSerializer.Serialize(new { error = new { type = "rate_limit_exceeded" } });
        var handler = new MockHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent(body) },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { index = 0, message = new { role = "assistant", content = "OK" }, finish_reason = "stop" } }
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }))
            });
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var factory = new MockHttpClientFactory(client);
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10), maxRetries: 2);

        var result = await provider.CompleteAsync(MakeRequest());

        Assert.True(result.Success);
        Assert.Equal("OK", result.Content);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_429QuotaExceeded_ReturnsImmediately()
    {
        var body = JsonSerializer.Serialize(new { error = new { type = "insufficient_quota" } });
        var handler = new MockHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent(body) },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    choices = new[] { new { index = 0, message = new { role = "assistant", content = "OK" }, finish_reason = "stop" } }
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }))
            });
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var factory = new MockHttpClientFactory(client);
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10), maxRetries: 2);

        var result = await provider.CompleteAsync(MakeRequest());

        Assert.False(result.Success);
        Assert.Equal(KejiProviderErrorCode.QuotaExceeded, result.ErrorCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void Config_HttpsRequired_RejectsPlainHttp()
    {
        Assert.Throws<ArgumentException>(() =>
            ModelProviderConfig.Create("openai", "key", "http://api.example.com", "model"));
    }

    [Fact]
    public void Config_LoopbackHttp_Allowed()
    {
        var cfg = ModelProviderConfig.Create("ollama", "", "http://127.0.0.1:11434", "model");
        Assert.NotNull(cfg);
    }

    [Fact]
    public void Registry_ImmutableDictionary_Frozen()
    {
        var dict = new Dictionary<string, IModelProvider>
        {
            ["openai"] = new SimpleMockProvider("openai")
        };
        var registry = new ModelProviderRegistry(dict);
        dict["openai"] = new SimpleMockProvider("hacked");
        var provider = registry.GetProvider("openai");
        Assert.Equal("openai", provider!.ProviderName);
    }

    [Fact]
    public void Config_UnresolvedSecretReference_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            ModelProviderConfig.Create("openai", "${OPENAI_API_KEY}", "http://localhost", "model"));
    }

    [Theory]
    [InlineData("${/absolute/path/secret}")]
    [InlineData("${./relative/path}")]
    [InlineData("${}")]
    [InlineData("${   }")]
    public void Config_InvalidSecretReferenceFormat_IsRejected(string apiKey)
    {
        Assert.Throws<ArgumentException>(() =>
            ModelProviderConfig.Create("openai", apiKey, "http://localhost", "model"));
    }

    private sealed class SimpleMockProvider : IModelProvider
    {
        public string ProviderName { get; }
        public SimpleMockProvider(string name) => ProviderName = name;
        public Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default)
            => Task.FromResult(ChatCompletionResponse.Succeeded("mock"));
        public async IAsyncEnumerable<ChatCompletionStreamEvent> StreamAsync(ChatCompletionRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return ChatCompletionStreamEvent.Done();
        }
    }
}

internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses;
    private Func<HttpResponseMessage>? _lastResponse;
    public int CallCount { get; private set; }

    public MockHttpMessageHandler(params HttpResponseMessage[] responses)
    {
        _responses = new Queue<Func<HttpResponseMessage>>(
            responses.Select<HttpResponseMessage, Func<HttpResponseMessage>>(r => () => r));
    }

    public MockHttpMessageHandler(HttpRequestException exception)
    {
        _responses = new Queue<Func<HttpResponseMessage>>();
        _responses.Enqueue(() => throw exception);
    }

    public MockHttpMessageHandler(TaskCanceledException exception)
    {
        _responses = new Queue<Func<HttpResponseMessage>>();
        _responses.Enqueue(() => throw exception);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);

        Func<HttpResponseMessage> responseFunc;
        if (_responses.Count > 0)
        {
            responseFunc = _responses.Dequeue();
            _lastResponse = responseFunc;
        }
        else if (_lastResponse is not null)
        {
            responseFunc = _lastResponse;
        }
        else
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }

        try
        {
            var response = responseFunc();
            return Task.FromResult(response);
        }
        catch (TaskCanceledException ex)
        {
            return Task.FromException<HttpResponseMessage>(ex);
        }
        catch (HttpRequestException ex)
        {
            return Task.FromException<HttpResponseMessage>(ex);
        }
    }
}

internal sealed class MockHttpClientFactory : IHttpClientFactory
{
    private readonly HttpClient _client;
    public MockHttpClientFactory(HttpClient client) => _client = client;
    public HttpClient CreateClient(string name) => _client;
}
