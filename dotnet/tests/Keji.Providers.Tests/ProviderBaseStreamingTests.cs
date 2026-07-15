using System.Net;
using System.Text.Json;
using Keji.Providers;

namespace Keji.Providers.Tests;

public sealed class ProviderBaseStreamingTests
{
    private sealed class MockProvider : ProviderBase
    {
        public override string ProviderName => "mock-stream";
        protected override string BaseUri => "http://localhost/";
        protected override System.Net.Http.Headers.AuthenticationHeaderValue? AuthHeader => null;

        public MockProvider(IHttpClientFactory factory, TimeSpan timeout) : base(factory, timeout, 0) { }
    }

    private static IHttpClientFactory CreateFactory(HttpResponseMessage response)
    {
        var handler = new MockHttpMessageHandler(response);
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        return new MockHttpClientFactory(client);
    }

    private static ChatCompletionRequest MakeRequest() => new()
    {
        Model = "test-model",
        Messages = new[] { new ChatMessage { Role = "user", Content = "hi" } }
    };

    private static HttpResponseMessage MakeStreamResponse(params string[] chunks)
    {
        var content = string.Join("", chunks.Select(c => $"data: {c}\n\n")) + "data: [DONE]\n\n";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "text/event-stream")
        };
    }

    [Fact]
    public async Task StreamAsync_YieldsTokens()
    {
        var factory = CreateFactory(MakeStreamResponse(
            """{"choices":[{"index":0,"delta":{"content":"Hello"}}]}""",
            """{"choices":[{"index":0,"delta":{"content":" "}}]}""",
            """{"choices":[{"index":0,"delta":{"content":"World"}}]}"""));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var results = new List<ChatCompletionStreamEvent>();
        await foreach (var e in provider.StreamAsync(MakeRequest()))
            results.Add(e);

        var tokens = results.Where(r => r.Type == ChatCompletionStreamEventType.Token).ToList();
        Assert.Equal(3, tokens.Count);
        Assert.Equal("Hello", tokens[0].Content);
        Assert.Equal("World", tokens[2].Content);
    }

    [Fact]
    public async Task StreamAsync_YieldsReasoningToken()
    {
        var factory = CreateFactory(MakeStreamResponse(
            """{"choices":[{"index":0,"delta":{"reasoning_content":"Thinking step 1"}}]}""",
            """{"choices":[{"index":0,"delta":{"reasoning_content":" step 2"}}]}"""));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var results = new List<ChatCompletionStreamEvent>();
        await foreach (var e in provider.StreamAsync(MakeRequest()))
            results.Add(e);

        var reasoning = results.Where(r => r.Type == ChatCompletionStreamEventType.ReasoningToken).ToList();
        Assert.Equal(2, reasoning.Count);
        Assert.Equal("Thinking step 1", reasoning[0].Content);
    }

    [Fact]
    public async Task StreamAsync_YieldsDone()
    {
        var factory = CreateFactory(MakeStreamResponse("""{"choices":[{"index":0,"delta":{"content":"ok"}}]}"""));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var results = new List<ChatCompletionStreamEvent>();
        await foreach (var e in provider.StreamAsync(MakeRequest()))
            results.Add(e);

        Assert.Contains(results, r => r.Type == ChatCompletionStreamEventType.Done);
    }

    [Fact]
    public async Task StreamAsync_ErrorResponse_YieldsError()
    {
        var factory = CreateFactory(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var results = new List<ChatCompletionStreamEvent>();
        await foreach (var e in provider.StreamAsync(MakeRequest()))
            results.Add(e);

        Assert.Contains(results, r => r.Type == ChatCompletionStreamEventType.Error);
    }

    [Fact]
    public async Task StreamAsync_Cancelled_ThrowsOperationCanceled()
    {
        var factory = CreateFactory(MakeStreamResponse(
            """{"choices":[{"index":0,"delta":{"content":"a"}}]}"""));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var results = new List<ChatCompletionStreamEvent>();
        await foreach (var e in provider.StreamAsync(MakeRequest(), cts.Token))
            results.Add(e);

        Assert.Contains(results, r => r.Type == ChatCompletionStreamEventType.Error && r.ErrorCode == "CANCELLED");
    }

    [Fact]
    public async Task StreamAsync_ToolCall_YieldsBegin()
    {
        var factory = CreateFactory(MakeStreamResponse(
            """{"choices":[{"index":0,"delta":{"tool_calls":[{"id":"call_1","type":"function","function":{"name":"read_file","arguments":""}}]}}]}""",
            """{"choices":[{"index":0,"delta":{"tool_calls":[{"id":null,"function":{"arguments":"{\"path\":"}}]}}]}""",
            """{"choices":[{"index":0,"delta":{"tool_calls":[{"id":null,"function":{"arguments":"\"/tmp\"}"}}]}}]}""",
            """{"choices":[{"index":0,"finish_reason":"tool_calls"}]}"""));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var results = new List<ChatCompletionStreamEvent>();
        await foreach (var e in provider.StreamAsync(MakeRequest()))
            results.Add(e);

        var begin = results.FirstOrDefault(r => r.Type == ChatCompletionStreamEventType.ToolCallBegin);
        Assert.NotNull(begin);
        Assert.Equal("call_1", begin.ToolCallId);
        Assert.Equal("read_file", begin.ToolName);
    }

    [Fact]
    public async Task StreamAsync_ToolCallDeltasAccumulate()
    {
        var factory = CreateFactory(MakeStreamResponse(
            """{"choices":[{"index":0,"delta":{"tool_calls":[{"id":"call_1","type":"function","function":{"name":"read_file","arguments":""}}]}}]}""",
            """{"choices":[{"index":0,"delta":{"tool_calls":[{"id":null,"function":{"arguments":"{\"path\":"}}]}}]}""",
            """{"choices":[{"index":0,"delta":{"tool_calls":[{"id":null,"function":{"arguments":"\"/tmp\"}"}}]}}]}""",
            """{"choices":[{"index":0,"finish_reason":"tool_calls"}]}"""));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var results = new List<ChatCompletionStreamEvent>();
        await foreach (var e in provider.StreamAsync(MakeRequest()))
            results.Add(e);

        var deltas = results.Where(r => r.Type == ChatCompletionStreamEventType.ToolCallDelta).ToList();
        Assert.Equal(2, deltas.Count);
        Assert.Equal("{\"path\":", deltas[0].ToolArguments);
        Assert.Equal("\"/tmp\"}", deltas[1].ToolArguments);
    }

    [Fact]
    public async Task StreamAsync_ToolCall_YieldsEnd()
    {
        var factory = CreateFactory(MakeStreamResponse(
            """{"choices":[{"index":0,"delta":{"tool_calls":[{"id":"call_1","type":"function","function":{"name":"read_file","arguments":""}}]}}]}""",
            """{"choices":[{"index":0,"finish_reason":"tool_calls"}]}""",
            """{"choices":[{"index":0,"delta":{"content":"done"}}]}"""));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var results = new List<ChatCompletionStreamEvent>();
        await foreach (var e in provider.StreamAsync(MakeRequest()))
            results.Add(e);

        Assert.Contains(results, r => r.Type == ChatCompletionStreamEventType.ToolCallEnd);
    }

    [Fact]
    public async Task StreamAsync_YieldsUsage()
    {
        var factory = CreateFactory(MakeStreamResponse(
            """{"choices":[{"index":0,"delta":{"content":"hello"}}]}""",
            """{"usage":{"prompt_tokens":10,"completion_tokens":5}}"""));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var results = new List<ChatCompletionStreamEvent>();
        await foreach (var e in provider.StreamAsync(MakeRequest()))
            results.Add(e);

        var usage = results.FirstOrDefault(r => r.Type == ChatCompletionStreamEventType.Usage);
        Assert.NotNull(usage);
        Assert.Equal(10, usage.Usage!.PromptTokens);
        Assert.Equal(5, usage.Usage.CompletionTokens);
    }

    [Fact]
    public async Task StreamAsync_HttpException_YieldsError()
    {
        var handler = new MockHttpMessageHandler(new HttpRequestException("Connection failed"));
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var factory = new MockHttpClientFactory(client);
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var results = new List<ChatCompletionStreamEvent>();
        await foreach (var e in provider.StreamAsync(MakeRequest()))
            results.Add(e);

        Assert.Contains(results, r => r.Type == ChatCompletionStreamEventType.Error);
    }

    [Fact]
    public async Task StreamAsync_MalformedJson_SkipsLine()
    {
        var factory = CreateFactory(MakeStreamResponse(
            """{"choices":[{"index":0,"delta":{"content":"ok"}}]}""",
            "not valid json",
            """{"choices":[{"index":0,"delta":{"content":" done"}}]}"""));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var results = new List<ChatCompletionStreamEvent>();
        await foreach (var e in provider.StreamAsync(MakeRequest()))
            results.Add(e);

        var tokens = results.Where(r => r.Type == ChatCompletionStreamEventType.Token).ToList();
        Assert.Equal(2, tokens.Count);
    }

    [Fact]
    public async Task StreamAsync_NonDataLine_Ignored()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(":comment\n\nevent: ping\ndata: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"ok\"}}]}\n\ndata: [DONE]\n\n",
                System.Text.Encoding.UTF8, "text/event-stream")
        };
        var factory = CreateFactory(response);
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var results = new List<ChatCompletionStreamEvent>();
        await foreach (var e in provider.StreamAsync(MakeRequest()))
            results.Add(e);

        var tokens = results.Where(r => r.Type == ChatCompletionStreamEventType.Token).ToList();
        Assert.Single(tokens);
    }

    [Fact]
    public async Task StreamAsync_MultipleChoices()
    {
        var factory = CreateFactory(MakeStreamResponse(
            """{"choices":[{"index":0,"delta":{"content":"Hello"}},{"index":1,"delta":{"content":"World"}}]}"""));
        var provider = new MockProvider(factory, TimeSpan.FromSeconds(10));

        var results = new List<ChatCompletionStreamEvent>();
        await foreach (var e in provider.StreamAsync(MakeRequest()))
            results.Add(e);

        var tokens = results.Where(r => r.Type == ChatCompletionStreamEventType.Token).ToList();
        Assert.Equal(2, tokens.Count);
    }
}
