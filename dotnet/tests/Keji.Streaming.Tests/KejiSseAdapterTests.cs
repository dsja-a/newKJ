using Keji.Providers;
using Keji.Streaming;

namespace Keji.Streaming.Tests;

public class KejiSseAdapterTests
{
    [Fact]
    public async Task ReasoningToken_MapsToThinkToken()
    {
        var providerEvents = AsyncEnumerable(new[] { ChatCompletionStreamEvent.ReasoningToken("think step 1") });

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Single(results);
        Assert.Equal(KejiSseEventType.ThinkToken, results[0].EventType);
        Assert.Equal(KejiSsePhase.Thinking, results[0].Phase);
        Assert.Equal("think step 1", results[0].Delta);
    }

    [Fact]
    public async Task Token_MapsToAnswer()
    {
        var providerEvents = AsyncEnumerable(new[] { ChatCompletionStreamEvent.Token("Hello") });

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal(KejiSseEventType.Answering, results[0].EventType);
        Assert.Equal(KejiSsePhase.Answering, results[0].Phase);
        Assert.Equal(KejiSseEventType.Answer, results[1].EventType);
        Assert.Equal("Hello", results[1].Delta);
    }

    [Fact]
    public async Task MultipleTokens_EmitSingleAnswering()
    {
        var providerEvents = AsyncEnumerable(new[]
        {
            ChatCompletionStreamEvent.Token("Hello"),
            ChatCompletionStreamEvent.Token(" World")
        });

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(3, results.Count);
        Assert.Equal(KejiSseEventType.Answering, results[0].EventType);
        Assert.Equal(KejiSseEventType.Answer, results[1].EventType);
        Assert.Equal(KejiSseEventType.Answer, results[2].EventType);
    }

    [Fact]
    public async Task ReasoningThenToken_TransitionsPhase()
    {
        var providerEvents = AsyncEnumerable(new[]
        {
            ChatCompletionStreamEvent.ReasoningToken("thinking"),
            ChatCompletionStreamEvent.Token("answer")
        });

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(3, results.Count);
        Assert.Equal(KejiSsePhase.Thinking, results[0].Phase);
        Assert.Equal(KejiSsePhase.Answering, results[1].Phase);
        Assert.Equal(KejiSsePhase.Answering, results[2].Phase);
    }

    [Fact]
    public async Task ToolCallBegin_MapsToToolCall_WithNameOnly()
    {
        var providerEvents = AsyncEnumerable(new[]
        {
            ChatCompletionStreamEvent.ToolCallBegin("call_1", "read_file")
        });

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(2, results.Count);
        Assert.Equal(KejiSseEventType.Answering, results[0].EventType);
        Assert.Equal(KejiSseEventType.ToolCall, results[1].EventType);
        Assert.Equal("read_file", results[1].ToolName);
        Assert.Null(results[1].Delta);
    }

    [Fact]
    public async Task ToolCallDeltas_AreStrippedFromSSE()
    {
        var providerEvents = AsyncEnumerable(new[]
        {
            ChatCompletionStreamEvent.ToolCallBegin("call_1", "read_file"),
            ChatCompletionStreamEvent.ToolCallDelta("{\"path\":\"/tmp\"}"),
            ChatCompletionStreamEvent.ToolCallEnd()
        });

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        // Only Answering + ToolCall emitted; deltas and end are stripped
        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, r => r.EventType == KejiSseEventType.Answer);
        Assert.DoesNotContain(results, r => r.Delta is not null);
    }

    [Fact]
    public async Task Usage_MapsToUsage()
    {
        var usage = new TokenUsage { PromptTokens = 10, CompletionTokens = 5 };
        var providerEvents = AsyncEnumerable(new[] { ChatCompletionStreamEvent.UsageEvent(usage) });

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Single(results);
        Assert.Equal(KejiSseEventType.Usage, results[0].EventType);
        Assert.Equal(KejiSsePhase.Done, results[0].Phase);
        Assert.Equal(10, results[0].Usage!.PromptTokens);
    }

    [Fact]
    public async Task Error_MapsToError()
    {
        var providerEvents = AsyncEnumerable(new[] { ChatCompletionStreamEvent.Error("ERR", "Something failed") });

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Single(results);
        Assert.Equal(KejiSseEventType.Error, results[0].EventType);
        Assert.Equal(KejiSsePhase.Error, results[0].Phase);
        Assert.Equal("Something failed", results[0].ErrorMessage);
    }

    [Fact]
    public async Task Error_WithNullMessage_UsesDefault()
    {
        var providerEvents = AsyncEnumerable(new[] { ChatCompletionStreamEvent.Error("ERR", null!) });

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal("An error occurred", results[0].ErrorMessage);
    }

    [Fact]
    public async Task Done_MapsToDone()
    {
        var providerEvents = AsyncEnumerable(new[] { ChatCompletionStreamEvent.Done() });

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Single(results);
        Assert.Equal(KejiSseEventType.Done, results[0].EventType);
        Assert.Equal(KejiSsePhase.Done, results[0].Phase);
    }

    [Fact]
    public async Task MultipleEvents_FullSequence()
    {
        var providerEvents = AsyncEnumerable(new[]
        {
            ChatCompletionStreamEvent.ReasoningToken("think"),
            ChatCompletionStreamEvent.ReasoningToken(" more"),
            ChatCompletionStreamEvent.Token("Hello"),
            ChatCompletionStreamEvent.ToolCallBegin("call_1", "search"),
            ChatCompletionStreamEvent.ToolCallDelta("{\"q\":"),
            ChatCompletionStreamEvent.ToolCallDelta("\"test\"}"),
            ChatCompletionStreamEvent.ToolCallEnd(),
            ChatCompletionStreamEvent.Token(" World"),
            ChatCompletionStreamEvent.UsageEvent(new TokenUsage { PromptTokens = 5, CompletionTokens = 3 }),
            ChatCompletionStreamEvent.Done()
        });

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(9, results.Count);
        Assert.Equal(KejiSseEventType.ThinkToken, results[0].EventType);
        Assert.Equal(KejiSseEventType.ThinkToken, results[1].EventType);
        Assert.Equal(KejiSseEventType.Answering, results[2].EventType);
        Assert.Equal(KejiSseEventType.Answer, results[3].EventType);
        Assert.Equal(KejiSseEventType.ToolCall, results[4].EventType);
        Assert.Equal(KejiSseEventType.Answering, results[5].EventType);
        Assert.Equal(KejiSseEventType.Answer, results[6].EventType);
        Assert.Equal(KejiSseEventType.Usage, results[7].EventType);
        Assert.Equal(KejiSseEventType.Done, results[8].EventType);
    }

    [Fact]
    public async Task CancellationToken_StopsEnumeration()
    {
        var providerEvents = AsyncEnumerable(new[]
        {
            ChatCompletionStreamEvent.Token("Hello"),
            ChatCompletionStreamEvent.Token(" World")
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var results = await KejiSseAdapter.ToSseEvents(providerEvents, cts.Token).ToListAsync();
        Assert.Empty(results);
    }

    [Fact]
    public async Task EmptyStream_ReturnsNothing()
    {
        var providerEvents = AsyncEnumerable(Array.Empty<ChatCompletionStreamEvent>());
        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();
        Assert.Empty(results);
    }

    [Fact]
    public async Task ToolCallWithoutBegin_NoEmission()
    {
        var providerEvents = AsyncEnumerable(new[]
        {
            ChatCompletionStreamEvent.ToolCallDelta("{}"),
            ChatCompletionStreamEvent.ToolCallEnd()
        });

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();
        Assert.Empty(results);
    }

    private static async IAsyncEnumerable<ChatCompletionStreamEvent> AsyncEnumerable(ChatCompletionStreamEvent[] events)
    {
        foreach (var e in events)
        {
            await Task.Yield();
            yield return e;
        }
    }
}

internal static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var results = new List<T>();
        await foreach (var item in source)
            results.Add(item);
        return results;
    }
}
