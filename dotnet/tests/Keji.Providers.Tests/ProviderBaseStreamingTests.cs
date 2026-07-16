using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Keji.Providers;

namespace Keji.Providers.Tests;

public sealed class ProviderBaseStreamingTests
{
    [Fact]
    public async Task StreamAsync_ProducesFirstEventBeforeResponseCompletes()
    {
        var stream = new GatedStream(
            Utf8(Sse(TokenChunk("first"))),
            Utf8(SuccessfulTail()));
        var handler = new DelegateHandler((_, _) => Task.FromResult(StreamResponse(stream)));
        var provider = CreateProvider(handler);

        await using var enumerator = provider.StreamAsync(Request()).GetAsyncEnumerator();
        var firstMove = enumerator.MoveNextAsync().AsTask();

        Assert.Same(firstMove, await Task.WhenAny(firstMove, Task.Delay(TimeSpan.FromSeconds(2))));
        Assert.True(await firstMove);
        Assert.Equal(ChatCompletionStreamEventType.Token, enumerator.Current.Type);
        Assert.Equal("first", enumerator.Current.Content);
        Assert.True(stream.SecondReadStarted.Task.IsCompletedSuccessfully);

        stream.Release();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(ChatCompletionStreamEventType.ChoiceFinished, enumerator.Current.Type);
        Assert.Equal("stop", enumerator.Current.FinishReason);
        Assert.False(enumerator.Current.HasToolCalls);
        Assert.False(enumerator.Current.ShouldExecuteTools);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(ChatCompletionStreamEventType.Done, enumerator.Current.Type);
        Assert.False(await enumerator.MoveNextAsync());
    }

    [Fact]
    public async Task StreamAsync_ConsumerExitCancelsAndDisposesProducerStream()
    {
        var stream = new GatedStream(Utf8(Sse(TokenChunk("first"))), Utf8(SuccessfulTail()));
        var provider = CreateProvider(new DelegateHandler((_, _) => Task.FromResult(StreamResponse(stream))));

        await using (var enumerator = provider.StreamAsync(Request()).GetAsyncEnumerator())
        {
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(ChatCompletionStreamEventType.Token, enumerator.Current.Type);
        }

        Assert.Same(stream.CancellationObserved.Task,
            await Task.WhenAny(stream.CancellationObserved.Task, Task.Delay(TimeSpan.FromSeconds(2))));
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task StreamAsync_ParsesCrLfCrLfAndDataWithoutSpace()
    {
        var body =
            $"data:{TokenChunk("crlf")}\r\n\r\n" +
            $"data: {TokenChunk("cr")}\r\r" +
            $"data: {TokenChunk("lf")}\n\n" +
            $"data: {FinishChunk()}\r\n\r\n" +
            "data: [DONE]\r\n\r\n";

        var events = await CollectAsync(CreateProvider(Response(body)).StreamAsync(Request()));

        Assert.Equal(new[] { "crlf", "cr", "lf" },
            events.Where(e => e.Type == ChatCompletionStreamEventType.Token).Select(e => e.Content));
        Assert.True(events[^1].Type == ChatCompletionStreamEventType.Done,
            $"Unexpected terminal event: {events[^1].Type}/{events[^1].ErrorCode}");
    }

    [Fact]
    public async Task StreamAsync_JoinsMultipleDataFieldsAndIgnoresOtherFields()
    {
        var body =
            ": heartbeat\n" +
            "event: message\n" +
            "id: ignored\n" +
            "retry: 1000\n" +
            "data: {\"choices\":[\n" +
            "data: {\"index\":0,\"delta\":{\"content\":\"joined\"}}]}\n\n" +
            $"data: {FinishChunk()}\n\n" +
            "data: [DONE]\n\n";

        var events = await CollectAsync(CreateProvider(Response(body)).StreamAsync(Request()));

        var token = Assert.Single(events, e => e.Type == ChatCompletionStreamEventType.Token);
        Assert.Equal("joined", token.Content);
        Assert.True(events[^1].Type == ChatCompletionStreamEventType.Done,
            $"Unexpected terminal event: {events[^1].Type}/{events[^1].ErrorCode}");
    }

    [Fact]
    public async Task StreamAsync_WaitsForUsageAfterFinishReason()
    {
        var body =
            Sse(TokenChunk("answer")) +
            Sse("{\"choices\":[{\"index\":0,\"finish_reason\":\"stop\"}]}" ) +
            Sse("{\"choices\":[],\"usage\":{\"prompt_tokens\":12,\"completion_tokens\":4}}") +
            Sse("[DONE]");

        var events = await CollectAsync(CreateProvider(Response(body)).StreamAsync(Request()));

        Assert.Equal(
            new[]
            {
                ChatCompletionStreamEventType.Token,
                ChatCompletionStreamEventType.ChoiceFinished,
                ChatCompletionStreamEventType.Usage,
                ChatCompletionStreamEventType.Done
            },
            events.Select(e => e.Type));
        Assert.Equal("stop", events[1].FinishReason);
        Assert.Equal(12, events[2].Usage!.PromptTokens);
        Assert.Equal(4, events[2].Usage!.CompletionTokens);
    }

    [Fact]
    public async Task StreamAsync_RequestEnablesUsageAndUsesEventStreamAccept()
    {
        string? requestBody = null;
        MediaTypeWithQualityHeaderValue? accept = null;
        var handler = new DelegateHandler(async (request, _) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            accept = request.Headers.Accept.Single();
            return Response(Sse(FinishChunk()) + Sse("[DONE]"));
        });

        await CollectAsync(CreateProvider(handler).StreamAsync(Request()));

        Assert.Contains("\"stream\":true", requestBody, StringComparison.Ordinal);
        Assert.Contains("\"stream_options\":{\"include_usage\":true}", requestBody, StringComparison.Ordinal);
        Assert.Equal("text/event-stream", accept!.MediaType);
    }

    [Fact]
    public async Task StreamAsync_IncompleteEventAtEofIsDiscardedAndReportedTruncated()
    {
        var body = $"data: {TokenChunk("must-not-leak")}";

        var events = await CollectAsync(CreateProvider(Response(body)).StreamAsync(Request()));

        var error = Assert.Single(events);
        Assert.Equal(ChatCompletionStreamEventType.Error, error.Type);
        Assert.Equal("STREAM_TRUNCATED", error.ErrorCode);
    }

    [Fact]
    public async Task StreamAsync_DoneBeforeAnyFinishedChoiceIsReportedTruncated()
    {
        var events = await CollectAsync(CreateProvider(Response(Sse("[DONE]"))).StreamAsync(Request()));

        var error = Assert.Single(events);
        Assert.Equal(ChatCompletionStreamEventType.Error, error.Type);
        Assert.Equal("STREAM_TRUNCATED", error.ErrorCode);
    }

    [Fact]
    public async Task StreamAsync_EofAfterFinishedChoiceCompletesWithoutDoneMarker()
    {
        var body = Sse(TokenChunk("ok")) + Sse("{\"choices\":[{\"index\":0,\"finish_reason\":\"stop\"}]}");

        var events = await CollectAsync(CreateProvider(Response(body)).StreamAsync(Request()));

        Assert.Equal(ChatCompletionStreamEventType.Token, events[0].Type);
        Assert.Equal(ChatCompletionStreamEventType.ChoiceFinished, events[1].Type);
        Assert.Equal("stop", events[1].FinishReason);
        Assert.Equal(ChatCompletionStreamEventType.Done, events[2].Type);
    }

    [Fact]
    public async Task StreamAsync_MalformedJsonFailsClosed()
    {
        var events = await CollectAsync(CreateProvider(Response(Sse("not-json") + Sse("[DONE]"))).StreamAsync(Request()));

        var error = Assert.Single(events);
        Assert.Equal("STREAM_PROTOCOL_ERROR", error.ErrorCode);
    }

    [Fact]
    public async Task StreamAsync_InvalidUtf8FailsClosed()
    {
        var bytes = Encoding.ASCII.GetBytes("data: ").Concat(new byte[] { 0xff, (byte)'\n', (byte)'\n' }).ToArray();
        var events = await CollectAsync(CreateProvider(StreamResponse(new MemoryStream(bytes))).StreamAsync(Request()));

        var error = Assert.Single(events);
        Assert.Equal("STREAM_PROTOCOL_ERROR", error.ErrorCode);
    }

    [Fact]
    public async Task StreamAsync_RejectsWrongContentType()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Sse("[DONE]"), Encoding.UTF8, "application/json")
        };

        var error = Assert.Single(await CollectAsync(CreateProvider(response).StreamAsync(Request())));

        Assert.Equal("INVALID_CONTENT_TYPE", error.ErrorCode);
    }

    [Fact]
    public async Task StreamAsync_EnforcesLineAndEventLimits()
    {
        var oversizedLine = "data: " + new string('a', 64 * 1024) + "\n\n";
        var lineError = Assert.Single(await CollectAsync(CreateProvider(Response(oversizedLine)).StreamAsync(Request())));
        Assert.Equal("STREAM_PROTOCOL_ERROR", lineError.ErrorCode);

        var part = new string('a', 60_000);
        var oversizedEvent = string.Concat(Enumerable.Repeat($"data: {part}\n", 5)) + "\n";
        var eventError = Assert.Single(await CollectAsync(CreateProvider(Response(oversizedEvent)).StreamAsync(Request())));
        Assert.Equal("STREAM_PROTOCOL_ERROR", eventError.ErrorCode);
    }

    [Fact]
    public async Task StreamAsync_ReadStallUsesBodyTimeout()
    {
        var stream = new BlockingStream();
        var provider = CreateProvider(
            new DelegateHandler((_, _) => Task.FromResult(StreamResponse(stream))),
            timeout: TimeSpan.FromMilliseconds(50));

        var events = await CollectAsync(provider.StreamAsync(Request()));

        var error = Assert.Single(events);
        Assert.Equal("TIMEOUT", error.ErrorCode);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task StreamAsync_PreCancelledTokenDoesNotSend()
    {
        var handler = new DelegateHandler((_, _) => Task.FromResult(Response(Sse("[DONE]"))));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await CollectAsync(CreateProvider(handler).StreamAsync(Request(), cts.Token)));
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task StreamAsync_RetryAfterDeltaIsHonoredBeforeFirstEvent()
    {
        var retry = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        retry.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
        var handler = QueueHandler(retry, Response(Sse(TokenChunk("ok")) + SuccessfulTail()));
        var provider = CreateProvider(handler, maxRetries: 1);

        var events = await CollectAsync(provider.StreamAsync(Request()));

        Assert.Equal(2, handler.CallCount);
        Assert.Equal(TimeSpan.FromSeconds(7), Assert.Single(provider.Delays));
        Assert.Contains(events, e => e.Type == ChatCompletionStreamEventType.Token);
    }

    [Fact]
    public async Task StreamAsync_RetryAfterDateIsHonoredAndCapped()
    {
        var now = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var retry = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        retry.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddMinutes(2));
        var handler = QueueHandler(retry, Response(Sse(FinishChunk()) + Sse("[DONE]")));
        var provider = CreateProvider(handler, maxRetries: 1, now: now);

        await CollectAsync(provider.StreamAsync(Request()));

        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(provider.Delays));
    }

    [Fact]
    public async Task StreamAsync_TransportFailureBeforeFirstEventRetries()
    {
        var handler = QueueHandler(
            StreamResponse(new ThrowingAfterStream(Array.Empty<byte>())),
            Response(Sse(TokenChunk("after-retry")) + SuccessfulTail()));
        var provider = CreateProvider(handler, maxRetries: 1);

        var events = await CollectAsync(provider.StreamAsync(Request()));

        Assert.Equal(2, handler.CallCount);
        Assert.Equal(TimeSpan.FromMilliseconds(250), Assert.Single(provider.Delays));
        Assert.Equal("after-retry", Assert.Single(events, e => e.Type == ChatCompletionStreamEventType.Token).Content);
    }

    [Fact]
    public async Task StreamAsync_TransportFailureAfterFirstEventDoesNotRetry()
    {
        var stream = new ThrowingAfterStream(Utf8(Sse(TokenChunk("partial"))));
        var handler = QueueHandler(streamResponse: StreamResponse(stream), fallback: Response(Sse(TokenChunk("duplicate"))));
        var provider = CreateProvider(handler, maxRetries: 2);

        var events = await CollectAsync(provider.StreamAsync(Request()));

        Assert.Equal(1, handler.CallCount);
        Assert.Empty(provider.Delays);
        Assert.Equal("partial", Assert.Single(events, e => e.Type == ChatCompletionStreamEventType.Token).Content);
        Assert.Equal("STREAM_INTERRUPTED", events[^1].ErrorCode);
    }

    [Fact]
    public async Task StreamAsync_EmitsCorrelatedParallelToolCallsAndEnds()
    {
        var begin = "{\"choices\":[{\"index\":2,\"delta\":{\"tool_calls\":[" +
            "{\"id\":\"call_a\",\"index\":0,\"function\":{\"name\":\"search\",\"arguments\":\"\"}}," +
            "{\"id\":\"call_b\",\"index\":1,\"function\":{\"name\":\"read\",\"arguments\":\"\"}}]}}]}";
        var delta = "{\"choices\":[{\"index\":2,\"delta\":{\"tool_calls\":[" +
            "{\"index\":0,\"function\":{\"arguments\":\"{\\\"q\\\":1}\"}}," +
            "{\"index\":1,\"function\":{\"arguments\":\"{\\\"p\\\":2}\"}}]}}]}";
        var finish = "{\"choices\":[{\"index\":2,\"finish_reason\":\"tool_calls\"}]}";

        var events = await CollectAsync(CreateProvider(Response(Sse(begin) + Sse(delta) + Sse(finish) + Sse("[DONE]"))).StreamAsync(Request()));

        Assert.Equal(2, events.Count(e => e.Type == ChatCompletionStreamEventType.ToolCallBegin));
        Assert.Equal(2, events.Count(e => e.Type == ChatCompletionStreamEventType.ToolCallDelta));
        Assert.Equal(2, events.Count(e => e.Type == ChatCompletionStreamEventType.ToolCallEnd));
        Assert.All(events.Where(e => e.Type is ChatCompletionStreamEventType.ToolCallBegin or ChatCompletionStreamEventType.ToolCallDelta or ChatCompletionStreamEventType.ToolCallEnd),
            e => Assert.Equal(2, e.ChoiceIndex));
        Assert.Contains(events, e => e.Type == ChatCompletionStreamEventType.ToolCallDelta && e.ToolCallId == "call_a" && e.ToolCallIndex == 0);
        Assert.Contains(events, e => e.Type == ChatCompletionStreamEventType.ToolCallEnd && e.ToolCallId == "call_b" && e.ToolCallIndex == 1);
    }

    [Fact]
    public async Task StreamAsync_OrphanToolDeltaFailsClosed()
    {
        var chunk = "{\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{}\"}}]}}]}";

        var error = Assert.Single(await CollectAsync(CreateProvider(Response(Sse(chunk))).StreamAsync(Request())));

        Assert.Equal("STREAM_PROTOCOL_ERROR", error.ErrorCode);
    }

    [Fact]
    public async Task StreamAsync_ExcessToolCallsFailsClosedWithoutDone()
    {
        var calls = string.Join(',', Enumerable.Range(0, 129).Select(index =>
            $"{{\"id\":\"call_{index}\",\"index\":{index},\"function\":{{\"name\":\"fn{index}\",\"arguments\":\"\"}}}}"));
        var chunk = $"{{\"choices\":[{{\"index\":0,\"delta\":{{\"tool_calls\":[{calls}]}}}}]}}";

        var events = await CollectAsync(CreateProvider(Response(Sse(chunk))).StreamAsync(Request()));

        Assert.Equal("STREAM_PROTOCOL_ERROR", events[^1].ErrorCode);
        Assert.DoesNotContain(events, e => e.Type == ChatCompletionStreamEventType.Done);
    }

    [Fact]
    public async Task StreamAsync_VisibleDeltaAfterFinishFailsClosed()
    {
        var body =
            Sse("{\"choices\":[{\"index\":0,\"finish_reason\":\"stop\"}]}") +
            Sse(TokenChunk("late")) + Sse("[DONE]");

        var events = await CollectAsync(CreateProvider(Response(body)).StreamAsync(Request()));

        Assert.Equal("STREAM_PROTOCOL_ERROR", Assert.Single(events).ErrorCode);
    }

    [Fact]
    public async Task StreamAsync_CleanTerminalOrdersChoiceFinishedUsageThenDone()
    {
        var first = "{\"choices\":[" +
            "{\"index\":0,\"delta\":{\"content\":\"safe\"},\"finish_reason\":\"content_filter\"}," +
            "{\"index\":1,\"delta\":{\"tool_calls\":[{\"id\":\"call_1\",\"index\":0,\"function\":{\"name\":\"lookup\",\"arguments\":\"{}\"}}]},\"finish_reason\":\"tool_calls\"}]}";
        var usage = "{\"choices\":[],\"usage\":{\"prompt_tokens\":8,\"completion_tokens\":3,\"total_tokens\":11}}";

        var events = await CollectAsync(CreateProvider(Response(Sse(first) + Sse(usage) + Sse("[DONE]"))).StreamAsync(Request()));

        var terminal = events.TakeLast(4).ToArray();
        Assert.Equal(
            new[]
            {
                ChatCompletionStreamEventType.ChoiceFinished,
                ChatCompletionStreamEventType.ChoiceFinished,
                ChatCompletionStreamEventType.Usage,
                ChatCompletionStreamEventType.Done
            },
            terminal.Select(item => item.Type));
        Assert.Equal(0, terminal[0].ChoiceIndex);
        Assert.Equal("content_filter", terminal[0].FinishReason);
        Assert.False(terminal[0].HasToolCalls);
        Assert.False(terminal[0].ShouldExecuteTools);
        Assert.Equal(1, terminal[1].ChoiceIndex);
        Assert.Equal("tool_calls", terminal[1].FinishReason);
        Assert.True(terminal[1].HasToolCalls);
        Assert.True(terminal[1].ShouldExecuteTools);
        Assert.Equal(11, terminal[2].Usage!.TotalTokens);
    }

    [Theory]
    [InlineData("tool_calls", true)]
    [InlineData("stop", true)]
    [InlineData("content_filter", false)]
    [InlineData("refusal", false)]
    [InlineData("error", false)]
    [InlineData("STOP", false)]
    [InlineData(" TOOL_CALLS ", false)]
    public void ChoiceFinishedToolAuthorizationRequiresExactReason(string finishReason, bool expected)
    {
        var streamEvent = ChatCompletionStreamEvent.ChoiceFinished(finishReason, hasToolCalls: true);

        Assert.Equal(finishReason, streamEvent.FinishReason);
        Assert.True(streamEvent.HasToolCalls);
        Assert.Equal(expected, streamEvent.ShouldExecuteTools);
    }

    [Fact]
    public async Task StreamAsync_TransportErrorDoesNotReleasePendingChoiceFinishedOrUsage()
    {
        var usage = "{\"choices\":[],\"usage\":{\"prompt_tokens\":2,\"completion_tokens\":1,\"total_tokens\":3}}";
        var bytes = Utf8(Sse(TokenChunk("partial")) + Sse(FinishChunk()) + Sse(usage));
        var handler = QueueHandler(StreamResponse(new ThrowingAfterStream(bytes)));

        var events = await CollectAsync(CreateProvider(handler).StreamAsync(Request()));

        Assert.Equal("partial", Assert.Single(events, item => item.Type == ChatCompletionStreamEventType.Token).Content);
        Assert.Equal("STREAM_INTERRUPTED", events[^1].ErrorCode);
        Assert.DoesNotContain(events, item => item.Type == ChatCompletionStreamEventType.ChoiceFinished);
        Assert.DoesNotContain(events, item => item.Type == ChatCompletionStreamEventType.Usage);
        Assert.DoesNotContain(events, item => item.Type == ChatCompletionStreamEventType.Done);
    }

    [Fact]
    public async Task StreamAsync_RejectsUtf16CharsetBeforeReadingBody()
    {
        var response = StreamResponse(new MemoryStream(Utf8(SuccessfulTail())));
        response.Content.Headers.ContentType!.CharSet = "utf-16";

        var error = Assert.Single(await CollectAsync(CreateProvider(response).StreamAsync(Request())));

        Assert.Equal("INVALID_CONTENT_TYPE", error.ErrorCode);
    }

    [Fact]
    public async Task StreamAsync_RejectsUtf16BomButAcceptsUtf8Bom()
    {
        var text = Sse(TokenChunk("bom")) + SuccessfulTail();
        var utf16Bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray();
        var utf16Events = await CollectAsync(
            CreateProvider(StreamResponse(new MemoryStream(utf16Bytes))).StreamAsync(Request()));
        Assert.Equal("STREAM_PROTOCOL_ERROR", Assert.Single(utf16Events).ErrorCode);

        var utf8Bytes = Encoding.UTF8.GetPreamble().Concat(Utf8(text)).ToArray();
        var utf8Events = await CollectAsync(
            CreateProvider(StreamResponse(new MemoryStream(utf8Bytes))).StreamAsync(Request()));
        Assert.Equal("bom", Assert.Single(utf8Events, item => item.Type == ChatCompletionStreamEventType.Token).Content);
        Assert.Contains(utf8Events, item => item.Type == ChatCompletionStreamEventType.ChoiceFinished);
        Assert.Equal(ChatCompletionStreamEventType.Done, utf8Events[^1].Type);
    }

    [Fact]
    public async Task StreamAsync_EmptyDataEventFailsClosed()
    {
        var events = await CollectAsync(CreateProvider(Response("data:\n\n")).StreamAsync(Request()));

        var error = Assert.Single(events);
        Assert.Equal("STREAM_PROTOCOL_ERROR", error.ErrorCode);
    }

    [Theory]
    [InlineData("{\"choices\":[null]}")]
    [InlineData("{\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[null]}}]}")]
    public async Task StreamAsync_NullCollectionElementsFailClosed(string chunk)
    {
        var events = await CollectAsync(CreateProvider(Response(Sse(chunk))).StreamAsync(Request()));

        var error = Assert.Single(events);
        Assert.Equal("STREAM_PROTOCOL_ERROR", error.ErrorCode);
    }

    [Fact]
    public async Task StreamAsync_ReadTimeoutBeforeFirstEventRetries()
    {
        var handler = QueueHandler(
            StreamResponse(new BlockingStream()),
            Response(Sse(TokenChunk("retry-success")) + SuccessfulTail()));
        var provider = CreateProvider(handler, timeout: TimeSpan.FromMilliseconds(50), maxRetries: 1);

        var events = await CollectAsync(provider.StreamAsync(Request()));

        Assert.Equal(2, handler.CallCount);
        Assert.Equal(TimeSpan.FromMilliseconds(250), Assert.Single(provider.Delays));
        Assert.Equal("retry-success", Assert.Single(events, item => item.Type == ChatCompletionStreamEventType.Token).Content);
        Assert.Equal(ChatCompletionStreamEventType.Done, events[^1].Type);
    }

    [Fact]
    public async Task StreamAsync_ReadTimeoutAfterFirstEventDoesNotRetry()
    {
        var stream = new GatedStream(Utf8(Sse(TokenChunk("partial"))), Utf8(SuccessfulTail()));
        var handler = QueueHandler(
            StreamResponse(stream),
            Response(Sse(TokenChunk("duplicate")) + SuccessfulTail()));
        var provider = CreateProvider(handler, timeout: TimeSpan.FromMilliseconds(50), maxRetries: 2);

        var events = await CollectAsync(provider.StreamAsync(Request()));

        Assert.Equal(1, handler.CallCount);
        Assert.Empty(provider.Delays);
        Assert.Equal("partial", Assert.Single(events, item => item.Type == ChatCompletionStreamEventType.Token).Content);
        Assert.Equal("TIMEOUT", events[^1].ErrorCode);
        Assert.DoesNotContain(events, item => item.Type == ChatCompletionStreamEventType.ChoiceFinished);
    }

    [Fact]
    public async Task StreamAsync_ConsumerBreakDisposesStreamThatIgnoresCancellation()
    {
        var stream = new DisposeUnblocksStream(Utf8(Sse(TokenChunk("first"))));
        var provider = CreateProvider(
            new DelegateHandler((_, _) => Task.FromResult(StreamResponse(stream))),
            timeout: TimeSpan.FromSeconds(2));
        var enumerator = provider.StreamAsync(Request()).GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("first", enumerator.Current.Content);
        Assert.Same(stream.BlockedReadStarted.Task,
            await Task.WhenAny(stream.BlockedReadStarted.Task, Task.Delay(TimeSpan.FromSeconds(2))));

        var disposeTask = enumerator.DisposeAsync().AsTask();
        Assert.Same(disposeTask, await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(2))));
        await disposeTask;
        Assert.True(stream.Disposed);
    }

    private static TestProvider CreateProvider(
        HttpMessageHandler handler,
        TimeSpan? timeout = null,
        int maxRetries = 0,
        DateTimeOffset? now = null) =>
        new(new TestHttpClientFactory(new HttpClient(handler)), timeout ?? TimeSpan.FromSeconds(2), maxRetries, now);

    private static TestProvider CreateProvider(
        HttpResponseMessage response,
        TimeSpan? timeout = null,
        int maxRetries = 0) =>
        CreateProvider(new DelegateHandler((_, _) => Task.FromResult(response)), timeout, maxRetries);

    private static HttpResponseMessage Response(string body) =>
        StreamResponse(new MemoryStream(Utf8(body)));

    private static HttpResponseMessage StreamResponse(Stream stream)
    {
        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream") { CharSet = "utf-8" };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static DelegateHandler QueueHandler(params HttpResponseMessage[] responses)
    {
        var queue = new Queue<HttpResponseMessage>(responses);
        return new DelegateHandler((_, _) => Task.FromResult(queue.Dequeue()));
    }

    private static DelegateHandler QueueHandler(HttpResponseMessage streamResponse, HttpResponseMessage fallback) =>
        QueueHandler(new[] { streamResponse, fallback });

    private static ChatCompletionRequest Request() => new()
    {
        Model = "test-model",
        Messages = new[] { new ChatMessage { Role = "user", Content = "hello" } }
    };

    private static string TokenChunk(string value) =>
        $"{{\"choices\":[{{\"index\":0,\"delta\":{{\"content\":\"{value}\"}}}}]}}";

    private static string FinishChunk(int choiceIndex = 0) =>
        $"{{\"choices\":[{{\"index\":{choiceIndex},\"finish_reason\":\"stop\"}}]}}";

    private static string SuccessfulTail(int choiceIndex = 0) =>
        Sse(FinishChunk(choiceIndex)) + Sse("[DONE]");

    private static string Sse(string data) => $"data: {data}\n\n";
    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static async Task<List<ChatCompletionStreamEvent>> CollectAsync(
        IAsyncEnumerable<ChatCompletionStreamEvent> source)
    {
        var result = new List<ChatCompletionStreamEvent>();
        await foreach (var item in source)
            result.Add(item);
        return result;
    }

    private sealed class TestProvider : ProviderBase
    {
        private readonly DateTimeOffset? _now;

        public TestProvider(IHttpClientFactory factory, TimeSpan timeout, int maxRetries, DateTimeOffset? now)
            : base(factory, timeout, maxRetries) => _now = now;

        public List<TimeSpan> Delays { get; } = new();
        public override string ProviderName => "test";
        protected override string BaseUri => "http://localhost/v1/";
        protected override AuthenticationHeaderValue? AuthHeader => null;
        protected override Task DelayBeforeRetryAsync(TimeSpan delay, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Delays.Add(delay);
            return Task.CompletedTask;
        }

        protected override DateTimeOffset GetUtcNow() => _now ?? base.GetUtcNow();
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public TestHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        public DelegateHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return _send(request, cancellationToken);
        }
    }

    private sealed class GatedStream : Stream
    {
        private readonly byte[] _first;
        private readonly byte[] _second;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _read;

        public GatedStream(byte[] first, byte[] second)
        {
            _first = first;
            _second = second;
        }

        public TaskCompletionSource SecondReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public void Release() => _release.TrySetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_read == 0)
            {
                _read++;
                _first.CopyTo(buffer);
                return _first.Length;
            }

            if (_read == 1)
            {
                SecondReadStarted.TrySetResult();
                try
                {
                    await _release.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    CancellationObserved.TrySetResult();
                    throw;
                }

                _read++;
                _second.CopyTo(buffer);
                return _second.Length;
            }

            return 0;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            _release.TrySetResult();
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class BlockingStream : Stream
    {
        public bool Disposed { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class ThrowingAfterStream : Stream
    {
        private readonly byte[] _first;
        private int _read;
        public ThrowingAfterStream(byte[] first) => _first = first;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_read++ == 0 && _first.Length > 0)
            {
                _first.CopyTo(buffer);
                return ValueTask.FromResult(_first.Length);
            }

            return ValueTask.FromException<int>(new IOException("simulated transport interruption"));
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class DisposeUnblocksStream : Stream
    {
        private readonly byte[] _first;
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _read;

        public DisposeUnblocksStream(byte[] first) => _first = first;
        public TaskCompletionSource BlockedReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_read++ == 0)
            {
                _first.CopyTo(buffer);
                return _first.Length;
            }

            BlockedReadStarted.TrySetResult();
            await _disposed.Task.ConfigureAwait(false);
            return 0;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            _disposed.TrySetResult();
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
