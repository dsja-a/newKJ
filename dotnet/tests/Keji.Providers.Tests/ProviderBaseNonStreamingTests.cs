using System.Net;
using System.Text.Json;
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
        Messages = new[] { new ChatMessage { Role = "user", Content = "hello" } }
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

        Assert.NotNull(result.ToolCalls);
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
        Assert.Equal("AUTH_FAILED", result.ErrorCode);
    }

    [Fact]
    public async Task CompleteAsync_RateLimited_ReturnsError()
    {
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var result = await provider.CompleteAsync(MakeRequest());

        Assert.False(result.Success);
        Assert.Equal("RATE_LIMITED", result.ErrorCode);
    }

    [Fact]
    public async Task CompleteAsync_ServiceUnavailable_ReturnsError()
    {
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var result = await provider.CompleteAsync(MakeRequest());

        Assert.False(result.Success);
        Assert.Equal("SERVICE_UNAVAILABLE", result.ErrorCode);
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
        Assert.Equal("SERVER_ERROR", result.ErrorCode);
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
        Assert.Equal("CANCELLED", result.ErrorCode);
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
        Assert.Equal("REQUEST_ERROR", result.ErrorCode);
    }

    [Fact]
    public async Task CompleteAsync_GatewayTimeout_ReturnsError()
    {
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.GatewayTimeout));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var result = await provider.CompleteAsync(MakeRequest());
        Assert.False(result.Success);
        Assert.Equal("GATEWAY_TIMEOUT", result.ErrorCode);
    }

    [Fact]
    public async Task CompleteAsync_NotFound_ReturnsError()
    {
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.NotFound));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var result = await provider.CompleteAsync(MakeRequest());
        Assert.False(result.Success);
        Assert.Equal("ENDPOINT_NOT_FOUND", result.ErrorCode);
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
        Assert.Equal("CONNECTION_ERROR", result.ErrorCode);
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
}

internal sealed class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses;
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

        if (_responses.Count == 0)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        try
        {
            var response = _responses.Dequeue()();
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
