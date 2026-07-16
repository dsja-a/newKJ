using System.Runtime.CompilerServices;
using Keji.Providers;
using Keji.Streaming;

namespace Keji.Streaming.Tests;

public sealed class KejiSseAdapterTests
{
    [Fact]
    public async Task ReasoningAnswerUsageAndDone_EmitCompletePhaseSequence()
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ReasoningToken("plan", choiceIndex: 2),
            ChatCompletionStreamEvent.Token("answer", choiceIndex: 2),
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, hasToolCalls: false, choiceIndex: 2),
            ChatCompletionStreamEvent.UsageEvent(new TokenUsage { PromptTokens = 5, CompletionTokens = 3 }),
            ChatCompletionStreamEvent.Done());

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(
            [
                KejiSseEventType.Thinking,
                KejiSseEventType.ThinkToken,
                KejiSseEventType.Answering,
                KejiSseEventType.Answer,
                KejiSseEventType.Usage,
                KejiSseEventType.Done,
            ],
            results.Select(static item => item.EventType));
        Assert.Equal(
            [
                KejiSsePhase.Thinking,
                KejiSsePhase.Thinking,
                KejiSsePhase.Answering,
                KejiSsePhase.Answering,
                KejiSsePhase.Done,
                KejiSsePhase.Done,
            ],
            results.Select(static item => item.Phase));
        Assert.Equal(2, results[1].ChoiceIndex);
        Assert.Equal("plan", results[1].Delta);
        Assert.Equal(2, results[3].ChoiceIndex);
        Assert.Equal("answer", results[3].Delta);
        Assert.Equal(8, results[4].Usage!.TotalTokens);
    }

    [Fact]
    public async Task MultipleTokens_AnnounceEachPhaseOnlyOnce()
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ReasoningToken("one"),
            ChatCompletionStreamEvent.ReasoningToken("two"),
            ChatCompletionStreamEvent.Token("a"),
            ChatCompletionStreamEvent.Token("b"),
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, hasToolCalls: false),
            ChatCompletionStreamEvent.Done());

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Single(results, static item => item.EventType == KejiSseEventType.Thinking);
        Assert.Single(results, static item => item.EventType == KejiSseEventType.Answering);
        Assert.Equal(2, results.Count(static item => item.EventType == KejiSseEventType.ThinkToken));
        Assert.Equal(2, results.Count(static item => item.EventType == KejiSseEventType.Answer));
        Assert.Equal(KejiSseEventType.Done, results[^1].EventType);
    }

    [Fact]
    public async Task ParallelToolCalls_AreCorrelatedByChoiceAndToolIndex()
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ToolCallBegin("call-a", "search", toolCallIndex: 0, choiceIndex: 0),
            ChatCompletionStreamEvent.ToolCallBegin("call-b", "fetch", toolCallIndex: 0, choiceIndex: 1),
            ChatCompletionStreamEvent.ToolCallDelta("{\"secret\":\"alpha\"}", 0, 1, "call-b"),
            ChatCompletionStreamEvent.ToolCallDelta("{\"secret\":\"beta\"}", 0, 0, "call-a"),
            ChatCompletionStreamEvent.ToolCallEnd("call-a", 0, 0),
            ChatCompletionStreamEvent.ToolCallEnd("call-b", 0, 1),
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.ToolCalls, hasToolCalls: true, choiceIndex: 1),
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.ToolCalls, hasToolCalls: true, choiceIndex: 0),
            ChatCompletionStreamEvent.Done());

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(
            [
                KejiSseEventType.Answering,
                KejiSseEventType.ToolCall,
                KejiSseEventType.Answering,
                KejiSseEventType.ToolCall,
                KejiSseEventType.Done,
            ],
            results.Select(static item => item.EventType));
        Assert.Collection(
            results.Where(static item => item.EventType == KejiSseEventType.ToolCall),
            first =>
            {
                Assert.Equal(0, first.ChoiceIndex);
                Assert.Equal(0, first.ToolCallIndex);
                Assert.Equal("call-a", first.ToolCallId);
                Assert.Equal("search", first.ToolName);
                Assert.Null(first.Delta);
            },
            second =>
            {
                Assert.Equal(1, second.ChoiceIndex);
                Assert.Equal(0, second.ToolCallIndex);
                Assert.Equal("call-b", second.ToolCallId);
                Assert.Equal("fetch", second.ToolName);
                Assert.Null(second.Delta);
            });
        Assert.DoesNotContain(results, static item => item.Delta?.Contains("secret", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task SequentialToolCalls_MayReuseIndexAfterEnd()
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ToolCallBegin("call-a", "first", toolCallIndex: 3, choiceIndex: 1),
            ChatCompletionStreamEvent.ToolCallDelta("{}", toolCallIndex: 3, choiceIndex: 1, toolCallId: "call-a"),
            ChatCompletionStreamEvent.ToolCallEnd("call-a", toolCallIndex: 3, choiceIndex: 1),
            ChatCompletionStreamEvent.ToolCallBegin("call-b", "second", toolCallIndex: 3, choiceIndex: 1),
            ChatCompletionStreamEvent.ToolCallDelta("{}", toolCallIndex: 3, choiceIndex: 1, toolCallId: "call-b"),
            ChatCompletionStreamEvent.ToolCallEnd("call-b", toolCallIndex: 3, choiceIndex: 1),
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.ToolCalls, hasToolCalls: true, choiceIndex: 1),
            ChatCompletionStreamEvent.Done());

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(
            [
                KejiSseEventType.Answering,
                KejiSseEventType.ToolCall,
                KejiSseEventType.ToolCall,
                KejiSseEventType.Done,
            ],
            results.Select(static item => item.EventType));
        Assert.Equal(["call-a", "call-b"], results.Where(static item => item.EventType == KejiSseEventType.ToolCall).Select(static item => item.ToolCallId));
    }

    [Fact]
    public async Task ReasoningMayResumeOnlyAfterAllToolCallsEnd()
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ToolCallBegin("call-a", "first", toolCallIndex: 0),
            ChatCompletionStreamEvent.ToolCallDelta("{}", toolCallIndex: 0, toolCallId: "call-a"),
            ChatCompletionStreamEvent.ToolCallEnd("call-a", toolCallIndex: 0),
            ChatCompletionStreamEvent.ReasoningToken("inspect result"),
            ChatCompletionStreamEvent.Token("final"),
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.ToolCalls, hasToolCalls: true),
            ChatCompletionStreamEvent.Done());

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(
            [
                KejiSseEventType.Thinking,
                KejiSseEventType.ThinkToken,
                KejiSseEventType.Answering,
                KejiSseEventType.Answer,
                KejiSseEventType.ToolCall,
                KejiSseEventType.Done,
            ],
            results.Select(static item => item.EventType));
    }

    [Fact]
    public async Task ReasoningWhileToolCallIsOpen_IsProtocolError()
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ToolCallBegin("call-a", "first", toolCallIndex: 0),
            ChatCompletionStreamEvent.ReasoningToken("invalid"),
            ChatCompletionStreamEvent.Done());

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(KejiSseEventType.Error, results[^1].EventType);
        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, results[^1].ErrorCode);
        Assert.DoesNotContain(results, static item => item.EventType == KejiSseEventType.Done);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OrphanOrMismatchedToolEvent_IsProtocolError(bool useEnd)
    {
        var invalid = useEnd
            ? ChatCompletionStreamEvent.ToolCallEnd("unknown", toolCallIndex: 7, choiceIndex: 2)
            : ChatCompletionStreamEvent.ToolCallDelta("{}", toolCallIndex: 7, choiceIndex: 2, toolCallId: "unknown");

        var results = await KejiSseAdapter.ToSseEvents(AsyncEnumerable(invalid)).ToListAsync();

        var error = Assert.Single(results);
        Assert.Equal(KejiSseEventType.Error, error.EventType);
        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, error.ErrorCode);
        Assert.Equal(KejiSsePhase.Error, error.Phase);
    }

    [Fact]
    public async Task MismatchedToolId_IsProtocolError()
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ToolCallBegin("call-a", "search", 0, 0),
            ChatCompletionStreamEvent.ToolCallDelta("{}", 0, 0, "call-b"));

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, results[^1].ErrorCode);
        Assert.Equal(KejiSseEventType.Error, results[^1].EventType);
    }

    [Fact]
    public async Task DoneWithOpenToolCall_IsProtocolError()
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ToolCallBegin("call-a", "search"),
            ChatCompletionStreamEvent.Done());

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, results[^1].ErrorCode);
        Assert.DoesNotContain(results, static item => item.EventType == KejiSseEventType.Done);
    }

    [Fact]
    public async Task UsageWithOpenToolCall_IsProtocolError()
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ToolCallBegin("call-a", "search"),
            ChatCompletionStreamEvent.UsageEvent(new TokenUsage { PromptTokens = 1, CompletionTokens = 1 }));

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, results[^1].ErrorCode);
        Assert.DoesNotContain(results, static item => item.EventType == KejiSseEventType.Usage);
    }

    [Fact]
    public async Task ProviderEventAfterUsage_IsProtocolErrorAndNeverEmitted()
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, hasToolCalls: false),
            ChatCompletionStreamEvent.UsageEvent(new TokenUsage { PromptTokens = 1, CompletionTokens = 2 }),
            ChatCompletionStreamEvent.Token("must not escape"),
            ChatCompletionStreamEvent.Done());

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal([KejiSseEventType.Error], results.Select(static item => item.EventType));
        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, results[^1].ErrorCode);
        Assert.DoesNotContain(results, static item => item.Delta == "must not escape");
    }

    [Fact]
    public async Task ReasoningAfterAnswerWithoutToolBoundary_IsProtocolError()
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.Token("answer"),
            ChatCompletionStreamEvent.ReasoningToken("too late"));

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, results[^1].ErrorCode);
        Assert.DoesNotContain(results, static item => item.Delta == "too late");
    }

    [Fact]
    public async Task ProviderError_IsSanitizedAndTerminal()
    {
        var probe = new EnumerationProbe();
        var providerEvents = TerminalThenExtra(
            ChatCompletionStreamEvent.Error(KejiProviderErrorCode.AuthFailed, "sk-secret\r\nStackTrace"),
            probe);

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        var error = Assert.Single(results, static e => e.EventType == KejiSseEventType.Error);
        Assert.Equal(KejiProviderErrorCode.AuthFailed, error.ErrorCode);
        Assert.Equal("Provider authentication failed", error.ErrorMessage);
        Assert.DoesNotContain("secret", error.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(probe.ReadPastTerminal);
        Assert.True(probe.Disposed);
    }

    [Fact]
    public async Task Done_IsTerminalAndDisposesProviderEnumerator()
    {
        var probe = new EnumerationProbe();

        var results = await KejiSseAdapter
            .ToSseEvents(TerminalThenExtra(ChatCompletionStreamEvent.Done(), probe))
            .ToListAsync();

        Assert.Single(results);
        Assert.Equal(KejiSseEventType.Done, results[0].EventType);
        Assert.False(probe.ReadPastTerminal);
        Assert.True(probe.Disposed);
    }

    [Fact]
    public async Task UpstreamException_IsSanitizedAndDisposesProviderEnumerator()
    {
        var probe = new EnumerationProbe();

        var results = await KejiSseAdapter.ToSseEvents(ThrowingSource(probe)).ToListAsync();

        var error = Assert.Single(results);
        Assert.Equal(KejiSseEventType.Error, error.EventType);
        Assert.Equal(KejiProviderErrorCode.ProviderError, error.ErrorCode);
        Assert.Equal("Model provider request failed", error.ErrorMessage);
        Assert.True(probe.Disposed);
    }

    [Fact]
    public async Task SourceEndingWithoutTerminal_IsReportedAsTruncated()
    {
        var results = await KejiSseAdapter
            .ToSseEvents(AsyncEnumerable(ChatCompletionStreamEvent.Token("partial")))
            .ToListAsync();

        Assert.Equal(
            [KejiSseEventType.Answering, KejiSseEventType.Answer, KejiSseEventType.Error, KejiSseEventType.Done],
            results.Select(static item => item.EventType));
        Assert.Equal(KejiProviderErrorCode.StreamTruncated, results[^2].ErrorCode);
        Assert.Equal("Provider stream ended unexpectedly", results[^2].ErrorMessage);
    }

    [Fact]
    public async Task EmptySource_ProducesNoSyntheticEvent()
    {
        var results = await KejiSseAdapter.ToSseEvents(AsyncEnumerable()).ToListAsync();

        Assert.Empty(results);
    }

    [Fact]
    public async Task PreCancelledToken_DoesNotEnumerateSource()
    {
        var probe = new EnumerationProbe();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var results = await KejiSseAdapter.ToSseEvents(ProbeSource(probe), cts.Token).ToListAsync();

        Assert.Empty(results);
        Assert.False(probe.Started);
    }

    [Fact]
    public async Task CancellationDuringEnumeration_PropagatesAndDisposesSource()
    {
        var probe = new EnumerationProbe();
        using var cts = new CancellationTokenSource();
        await using var enumerator = KejiSseAdapter
            .ToSseEvents(TrackedSource(probe), cts.Token)
            .GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(KejiSseEventType.Answering, enumerator.Current.EventType);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await enumerator.MoveNextAsync().AsTask());
        await enumerator.DisposeAsync();
        Assert.True(probe.Disposed);
    }

    [Fact]
    public async Task EventsHaveContiguousSequenceMatchingIdsAndMonotonicUtcTimestamps()
    {
        var first = new DateTimeOffset(2026, 7, 15, 8, 0, 2, TimeSpan.Zero);
        var second = first.AddSeconds(-1);
        var third = first.AddSeconds(3);
        var fourth = first.AddSeconds(2);
        var clock = new SequenceTimeProvider(first, second, third, fourth);
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.Token("one"),
            ChatCompletionStreamEvent.Token("two"),
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, hasToolCalls: false),
            ChatCompletionStreamEvent.Done());

        var results = await KejiSseAdapter.ToSseEvents(providerEvents, timeProvider: clock).ToListAsync();

        Assert.Equal(4, results.Count);
        for (var index = 0; index < results.Count; index++)
        {
            Assert.Equal(index + 1, results[index].Sequence);
            Assert.Equal(32, results[index].EventId.Length);
            Assert.Equal(DateTimeKind.Utc, results[index].TimestampUtc.Kind);
            Assert.Equal(KejiSseEvent.CurrentProtocolVersion, results[index].ProtocolVersion);
            if (index > 0)
                Assert.True(results[index].TimestampUtc >= results[index - 1].TimestampUtc);
        }

        Assert.Equal(first.UtcDateTime, results[0].TimestampUtc);
        Assert.Equal(first.UtcDateTime, results[1].TimestampUtc);
        Assert.Equal(third.UtcDateTime, results[2].TimestampUtc);
        Assert.Equal(third.UtcDateTime, results[3].TimestampUtc);
    }

    [Theory]
    [InlineData(KejiProviderStreamEventKind.Token)]
    [InlineData(KejiProviderStreamEventKind.ReasoningToken)]
    public async Task MissingTokenContent_IsProtocolError(KejiProviderStreamEventKind type)
    {
        var invalid = new ChatCompletionStreamEvent { Type = type, Content = null, ChoiceIndex = 0 };

        var results = await KejiSseAdapter.ToSseEvents(AsyncEnumerable(invalid)).ToListAsync();

        var error = Assert.Single(results);
        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, error.ErrorCode);
    }

    [Fact]
    public async Task UnknownProviderEventType_IsProtocolError()
    {
        var invalid = new ChatCompletionStreamEvent { Type = (KejiProviderStreamEventKind)999 };

        var results = await KejiSseAdapter.ToSseEvents(AsyncEnumerable(invalid)).ToListAsync();

        var error = Assert.Single(results);
        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, error.ErrorCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChoiceFinished_IsRequiredBeforeUsageOrDone(bool sendUsage)
    {
        var events = new List<ChatCompletionStreamEvent>
        {
            ChatCompletionStreamEvent.Token("partial"),
        };
        events.Add(sendUsage
            ? ChatCompletionStreamEvent.UsageEvent(new TokenUsage { PromptTokens = 1, CompletionTokens = 1 })
            : ChatCompletionStreamEvent.Done());

        var results = await KejiSseAdapter.ToSseEvents(AsyncEnumerable([.. events])).ToListAsync();

        Assert.Equal(KejiSseEventType.Error, results[^1].EventType);
        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, results[^1].ErrorCode);
        Assert.DoesNotContain(results, static item => item.EventType is KejiSseEventType.Usage or KejiSseEventType.Done);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EveryObservedChoiceMustFinishBeforeUsageOrDone(bool sendUsage)
    {
        var events = new List<ChatCompletionStreamEvent>
        {
            ChatCompletionStreamEvent.Token("choice zero", choiceIndex: 0),
            ChatCompletionStreamEvent.Token("choice one", choiceIndex: 1),
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, hasToolCalls: false, choiceIndex: 0),
        };
        events.Add(sendUsage
            ? ChatCompletionStreamEvent.UsageEvent(new TokenUsage { PromptTokens = 1, CompletionTokens = 1 })
            : ChatCompletionStreamEvent.Done());

        var results = await KejiSseAdapter.ToSseEvents(AsyncEnumerable([.. events])).ToListAsync();

        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, results[^1].ErrorCode);
        Assert.DoesNotContain(results, static item => item.EventType is KejiSseEventType.Usage or KejiSseEventType.Done);
    }

    [Theory]
    [InlineData(KejiProviderStreamEventKind.Token)]
    [InlineData(KejiProviderStreamEventKind.ReasoningToken)]
    [InlineData(KejiProviderStreamEventKind.ToolCallBegin)]
    [InlineData(KejiProviderStreamEventKind.ToolCallDelta)]
    [InlineData(KejiProviderStreamEventKind.ToolCallEnd)]
    [InlineData(KejiProviderStreamEventKind.ChoiceFinished)]
    public async Task DuplicateFinishOrChoiceScopedEventAfterFinish_IsProtocolError(
        KejiProviderStreamEventKind eventType)
    {
        var postFinishEvent = eventType switch
        {
            KejiProviderStreamEventKind.Token => ChatCompletionStreamEvent.Token("late"),
            KejiProviderStreamEventKind.ReasoningToken => ChatCompletionStreamEvent.ReasoningToken("late"),
            KejiProviderStreamEventKind.ToolCallBegin => ChatCompletionStreamEvent.ToolCallBegin("late", "tool"),
            KejiProviderStreamEventKind.ToolCallDelta => ChatCompletionStreamEvent.ToolCallDelta("{}"),
            KejiProviderStreamEventKind.ToolCallEnd => ChatCompletionStreamEvent.ToolCallEnd(),
            KejiProviderStreamEventKind.ChoiceFinished =>
                ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, hasToolCalls: false),
            _ => throw new ArgumentOutOfRangeException(nameof(eventType)),
        };
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, hasToolCalls: false),
            postFinishEvent);

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        var error = Assert.Single(results);
        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, error.ErrorCode);
    }

    [Theory]
    [InlineData(KejiFinishReason.Stop, true, KejiSseEventType.Done)]
    [InlineData(KejiFinishReason.ToolCalls, true, KejiSseEventType.Done)]
    [InlineData(KejiFinishReason.ContentFilter, false, KejiSseEventType.Done)]
    [InlineData(KejiFinishReason.Invalid, false, KejiSseEventType.Error)]
    [InlineData(KejiFinishReason.Length, false, KejiSseEventType.Done)]
    [InlineData(KejiFinishReason.Error, false, KejiSseEventType.Done)]
    [InlineData((KejiFinishReason)99, false, KejiSseEventType.Done)]
    public async Task PendingToolCalls_AreReleasedOnlyForExactAllowedFinishReasons(
        KejiFinishReason finishReason,
        bool expectToolCall,
        KejiSseEventType expectedTerminal)
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ToolCallBegin("call_1", "search"),
            ChatCompletionStreamEvent.ToolCallDelta("{\"q\":\"private\"}", toolCallId: "call_1"),
            ChatCompletionStreamEvent.ToolCallEnd("call_1"),
            ChatCompletionStreamEvent.ChoiceFinished(finishReason, hasToolCalls: true),
            ChatCompletionStreamEvent.Done());

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(expectToolCall, results.Any(static item => item.EventType == KejiSseEventType.ToolCall));
        Assert.Equal(expectedTerminal, results[^1].EventType);
        Assert.DoesNotContain(results, static item => item.Delta?.Contains("private", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData(false, KejiProviderErrorCode.StreamTruncated)]
    [InlineData(true, KejiProviderErrorCode.AuthFailed)]
    public async Task PendingToolCalls_AreNotReleasedOnTruncationOrProviderError(
        bool sendError,
        KejiProviderErrorCode expectedErrorCode)
    {
        var events = new List<ChatCompletionStreamEvent>
        {
            ChatCompletionStreamEvent.ToolCallBegin("call_1", "search"),
            ChatCompletionStreamEvent.ToolCallDelta("{\"password\":\"secret\"}", toolCallId: "call_1"),
            ChatCompletionStreamEvent.ToolCallEnd("call_1"),
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.ToolCalls, hasToolCalls: true),
        };
        if (sendError)
            events.Add(ChatCompletionStreamEvent.Error(KejiProviderErrorCode.AuthFailed, "sk-secret"));

        var results = await KejiSseAdapter.ToSseEvents(AsyncEnumerable([.. events])).ToListAsync();

        var error = Assert.Single(results, static e => e.EventType == KejiSseEventType.Error);
        Assert.Equal(KejiSseEventType.Error, error.EventType);
        Assert.Equal(expectedErrorCode, error.ErrorCode);
        Assert.DoesNotContain(results, static item => item.EventType == KejiSseEventType.ToolCall);
    }

    [Theory]
    [InlineData(false, KejiProviderErrorCode.StreamTruncated)]
    [InlineData(true, KejiProviderErrorCode.AuthFailed)]
    public async Task PendingUsage_IsNotReleasedWithoutSuccessfulGlobalDone(
        bool sendError,
        KejiProviderErrorCode expectedErrorCode)
    {
        var events = new List<ChatCompletionStreamEvent>
        {
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, hasToolCalls: false),
            ChatCompletionStreamEvent.UsageEvent(new TokenUsage { PromptTokens = 3, CompletionTokens = 4 }),
        };
        if (sendError)
            events.Add(ChatCompletionStreamEvent.Error(KejiProviderErrorCode.AuthFailed, "raw secret"));

        var results = await KejiSseAdapter.ToSseEvents(AsyncEnumerable([.. events])).ToListAsync();

        var error = Assert.Single(results, static e => e.EventType == KejiSseEventType.Error);
        Assert.Equal(expectedErrorCode, error.ErrorCode);
        Assert.DoesNotContain(results, static item => item.EventType == KejiSseEventType.Usage);
    }

    [Fact]
    public async Task InterleavedChoices_MaintainIndependentPhaseMarkersWithChoiceIndex()
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ReasoningToken("reason one", choiceIndex: 1),
            ChatCompletionStreamEvent.Token("answer zero", choiceIndex: 0),
            ChatCompletionStreamEvent.Token("answer one", choiceIndex: 1),
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, hasToolCalls: false, choiceIndex: 0),
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, hasToolCalls: false, choiceIndex: 1),
            ChatCompletionStreamEvent.Done());

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(
            [
                KejiSseEventType.Thinking,
                KejiSseEventType.ThinkToken,
                KejiSseEventType.Answering,
                KejiSseEventType.Answer,
                KejiSseEventType.Answering,
                KejiSseEventType.Answer,
                KejiSseEventType.Done,
            ],
            results.Select(static item => item.EventType));
        Assert.Equal([1, 1, 0, 0, 1, 1, null], results.Select(static item => item.ChoiceIndex));
    }

    [Fact]
    public async Task NullProviderEvent_IsProtocolError()
    {
        ChatCompletionStreamEvent[] events = [null!];

        var results = await KejiSseAdapter.ToSseEvents(AsyncEnumerable(events)).ToListAsync();

        var error = Assert.Single(results);
        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, error.ErrorCode);
    }

    [Fact]
    public async Task GetAsyncEnumeratorFailure_IsSanitized()
    {
        var providerEvents = new BoundaryEnumerable(
            [],
            acquisitionError: "Authorization: Bearer sk-acquire-secret");

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();
        var wire = KejiSseFormatter.FormatEvent(Assert.Single(results));

        Assert.Equal(KejiProviderErrorCode.ProviderError, results[0].ErrorCode);
        Assert.DoesNotContain("sk-acquire-secret", wire, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NullEnumerator_IsSanitized()
    {
        var providerEvents = new BoundaryEnumerable([], returnNullEnumerator: true);

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        var error = Assert.Single(results);
        Assert.Equal(KejiProviderErrorCode.ProviderError, error.ErrorCode);
    }

    [Fact]
    public async Task DisposeAsyncFailure_CannotReplaceOrLeakPastSuccessfulDone()
    {
        var providerEvents = new BoundaryEnumerable(
            [
                ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, hasToolCalls: false),
                ChatCompletionStreamEvent.Done(),
            ],
            disposalError: "Authorization: Bearer sk-dispose-secret");

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();
        var wire = string.Concat(results.Select(KejiSseFormatter.FormatEvent));

        var done = Assert.Single(results);
        Assert.Equal(KejiSseEventType.Done, done.EventType);
        Assert.DoesNotContain("sk-dispose-secret", wire, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(16, KejiSseEventType.Done)]
    [InlineData(17, KejiSseEventType.Error)]
    public async Task ChoiceStateCount_IsBounded(int choiceCount, KejiSseEventType expectedTerminal)
    {
        var events = Enumerable.Range(0, choiceCount)
            .Select(static index => ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, false, index))
            .Append(ChatCompletionStreamEvent.Done())
            .ToArray();

        var results = await KejiSseAdapter.ToSseEvents(AsyncEnumerable(events)).ToListAsync();

        var terminal = Assert.Single(results);
        Assert.Equal(expectedTerminal, terminal.EventType);
        if (expectedTerminal == KejiSseEventType.Error)
            Assert.Equal(KejiProviderErrorCode.StreamProtocolError, terminal.ErrorCode);
    }

    [Theory]
    [InlineData(1024, KejiSseEventType.Done)]
    [InlineData(1025, KejiSseEventType.Error)]
    public async Task ChoiceIndex_IsBounded(int choiceIndex, KejiSseEventType expectedTerminal)
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, hasToolCalls: false, choiceIndex),
            ChatCompletionStreamEvent.Done());

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        var terminal = Assert.Single(results);
        Assert.Equal(expectedTerminal, terminal.EventType);
    }

    [Theory]
    [InlineData(128, KejiSseEventType.Done, 128)]
    [InlineData(129, KejiSseEventType.Error, 0)]
    public async Task ToolCallCountPerChoice_IsBounded(
        int toolCallCount,
        KejiSseEventType expectedTerminal,
        int expectedReleasedCalls)
    {
        var events = new List<ChatCompletionStreamEvent>();
        for (var index = 0; index < toolCallCount; index++)
        {
            events.Add(ChatCompletionStreamEvent.ToolCallBegin($"call_{index}", "tool", index));
            events.Add(ChatCompletionStreamEvent.ToolCallDelta("{}", index, toolCallId: $"call_{index}"));
            events.Add(ChatCompletionStreamEvent.ToolCallEnd($"call_{index}", index));
        }
        events.Add(ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.ToolCalls, hasToolCalls: true));
        events.Add(ChatCompletionStreamEvent.Done());

        var results = await KejiSseAdapter.ToSseEvents(AsyncEnumerable([.. events])).ToListAsync();

        Assert.Equal(expectedTerminal, results[^1].EventType);
        Assert.Equal(expectedReleasedCalls, results.Count(static item => item.EventType == KejiSseEventType.ToolCall));
    }

    [Theory]
    [InlineData(1024, KejiSseEventType.Done)]
    [InlineData(1025, KejiSseEventType.Error)]
    public async Task ToolCallIndex_IsBounded(int toolCallIndex, KejiSseEventType expectedTerminal)
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ToolCallBegin("call_1", "tool", toolCallIndex),
            ChatCompletionStreamEvent.ToolCallDelta("{}", toolCallIndex, toolCallId: "call_1"),
            ChatCompletionStreamEvent.ToolCallEnd("call_1", toolCallIndex),
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.ToolCalls, hasToolCalls: true),
            ChatCompletionStreamEvent.Done());

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(expectedTerminal, results[^1].EventType);
        Assert.Equal(expectedTerminal == KejiSseEventType.Done, results.Any(static item => item.EventType == KejiSseEventType.ToolCall));
    }

    [Fact]
    public async Task ToolArgumentDelta_EnforcesStrictUtf8ByteBoundary()
    {
        var exact = "{\"x\":\"" + new string('a', 256 * 1024 - 8) + "\"}";
        var tooLarge = "{\"x\":\"" + new string('a', 256 * 1024 - 7) + "\"}";
        var accepted = await KejiSseAdapter.ToSseEvents(AsyncEnumerable(
            ChatCompletionStreamEvent.ToolCallBegin("call_1", "tool"),
            ChatCompletionStreamEvent.ToolCallDelta(exact, toolCallId: "call_1"),
            ChatCompletionStreamEvent.ToolCallEnd("call_1"),
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.ToolCalls, hasToolCalls: true),
            ChatCompletionStreamEvent.Done())).ToListAsync();
        var rejected = await KejiSseAdapter.ToSseEvents(AsyncEnumerable(
            ChatCompletionStreamEvent.ToolCallBegin("call_1", "tool"),
            ChatCompletionStreamEvent.ToolCallDelta(tooLarge, toolCallId: "call_1"))).ToListAsync();
        var invalidUnicode = await KejiSseAdapter.ToSseEvents(AsyncEnumerable(
            ChatCompletionStreamEvent.ToolCallBegin("call_1", "tool"),
            ChatCompletionStreamEvent.ToolCallDelta("{\"x\":\"\uD800\"}", toolCallId: "call_1"))).ToListAsync();

        Assert.Equal(KejiSseEventType.Done, accepted[^1].EventType);
        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, Assert.Single(rejected).ErrorCode);
        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, Assert.Single(invalidUnicode).ErrorCode);
    }

    [Fact]
    public async Task ToolArgumentAggregatePerChoice_IsBounded()
    {
        var events = new List<ChatCompletionStreamEvent>();
        var quarterLimit = "{\"x\":\"" + new string('a', 256 * 1024 - 8) + "\"}";
        for (var index = 0; index < 4; index++)
        {
            events.Add(ChatCompletionStreamEvent.ToolCallBegin($"call_{index}", "tool", index));
            events.Add(ChatCompletionStreamEvent.ToolCallDelta(quarterLimit, index, toolCallId: $"call_{index}"));
            events.Add(ChatCompletionStreamEvent.ToolCallEnd($"call_{index}", index));
        }
        events.Add(ChatCompletionStreamEvent.ToolCallBegin("call_4", "tool", 4));
        events.Add(ChatCompletionStreamEvent.ToolCallDelta("{}", 4, toolCallId: "call_4"));

        var results = await KejiSseAdapter.ToSseEvents(AsyncEnumerable([.. events])).ToListAsync();

        var error = Assert.Single(results);
        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, error.ErrorCode);
    }

    [Theory]
    [InlineData("", KejiSseEventType.Error)]
    [InlineData("[]", KejiSseEventType.Error)]
    [InlineData("{\"key\":", KejiSseEventType.Error)]
    [InlineData("{\"key\":1}", KejiSseEventType.Done)]
    public async Task ToolCallEnd_RequiresCompleteJsonObjectArguments(
        string arguments,
        KejiSseEventType expectedTerminal)
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ToolCallBegin("call_1", "tool"),
            ChatCompletionStreamEvent.ToolCallDelta(arguments, toolCallId: "call_1"),
            ChatCompletionStreamEvent.ToolCallEnd("call_1"),
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.ToolCalls, hasToolCalls: true),
            ChatCompletionStreamEvent.Done());

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(expectedTerminal, results[^1].EventType);
        Assert.Equal(expectedTerminal == KejiSseEventType.Done, results.Any(static item => item.EventType == KejiSseEventType.ToolCall));
    }

    [Fact]
    public async Task FragmentedToolArguments_AreValidatedAsOneJsonObjectAtEnd()
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ToolCallBegin("call_1", "tool"),
            ChatCompletionStreamEvent.ToolCallDelta("{\"path\":", toolCallId: "call_1"),
            ChatCompletionStreamEvent.ToolCallDelta("\"private.txt\"}", toolCallId: "call_1"),
            ChatCompletionStreamEvent.ToolCallEnd("call_1"),
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.ToolCalls, hasToolCalls: true),
            ChatCompletionStreamEvent.Done());

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(KejiSseEventType.Done, results[^1].EventType);
        Assert.Single(results, static item => item.EventType == KejiSseEventType.ToolCall);
        Assert.DoesNotContain(results, static item => item.Delta?.Contains("private.txt", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task DuplicateToolCallIdAcrossIndices_IsProtocolErrorWithoutRelease()
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ToolCallBegin("call_1", "tool", toolCallIndex: 0),
            ChatCompletionStreamEvent.ToolCallDelta("{}", toolCallIndex: 0, toolCallId: "call_1"),
            ChatCompletionStreamEvent.ToolCallEnd("call_1", toolCallIndex: 0),
            ChatCompletionStreamEvent.ToolCallBegin("call_1", "tool", toolCallIndex: 1));

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        var error = Assert.Single(results);
        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, error.ErrorCode);
    }

    [Fact]
    public async Task ChoiceFinishHasToolCallsFlag_MustMatchObservedToolState()
    {
        var missingFlag = await KejiSseAdapter.ToSseEvents(AsyncEnumerable(
            ChatCompletionStreamEvent.ToolCallBegin("call_1", "tool"),
            ChatCompletionStreamEvent.ToolCallDelta("{}", toolCallId: "call_1"),
            ChatCompletionStreamEvent.ToolCallEnd("call_1"),
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.ToolCalls, hasToolCalls: false))).ToListAsync();
        var unexpectedFlag = await KejiSseAdapter.ToSseEvents(AsyncEnumerable(
            ChatCompletionStreamEvent.Token("answer"),
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, hasToolCalls: true))).ToListAsync();

        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, Assert.Single(missingFlag).ErrorCode);
        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, unexpectedFlag[^1].ErrorCode);
        Assert.DoesNotContain(missingFlag, static item => item.EventType == KejiSseEventType.ToolCall);
    }

    [Fact]
    public async Task InvalidFinishReason_IsProtocolError()
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Invalid, hasToolCalls: false));

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, Assert.Single(results).ErrorCode);
    }

    [Fact]
    public async Task FinishReasonLengthAndUnicode_AreResourceBounded()
    {
        foreach (var reason in new[] { KejiFinishReason.Stop, KejiFinishReason.ToolCalls, KejiFinishReason.ContentFilter, KejiFinishReason.Error, KejiFinishReason.Length })
        {
            var providerEvents = AsyncEnumerable(
                ChatCompletionStreamEvent.ChoiceFinished(reason, hasToolCalls: false),
                ChatCompletionStreamEvent.Done());

            var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

            Assert.NotEmpty(results);
            Assert.Equal(KejiSseEventType.Done, results[^1].EventType);
        }
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1_000_000_001, 0)]
    [InlineData(600_000_000, 500_000_000)]
    [InlineData(1, -1)]
    public async Task UsageCounts_AreResourceBounded(long promptTokens, long completionTokens)
    {
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, hasToolCalls: false),
            ChatCompletionStreamEvent.UsageEvent(new TokenUsage
            {
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
            }));

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        var error = Assert.Single(results);
        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, error.ErrorCode);
    }

    [Fact]
    public async Task TokenDelta_EnforcesStrictUtf8ByteBoundary()
    {
        var overLimit = new string('a', 256 * 1024 + 1);

        var tooLarge = await KejiSseAdapter.ToSseEvents(
            AsyncEnumerable(ChatCompletionStreamEvent.Token(overLimit))).ToListAsync();
        var invalidUnicode = await KejiSseAdapter.ToSseEvents(
            AsyncEnumerable(ChatCompletionStreamEvent.Token("\uD800"))).ToListAsync();

        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, Assert.Single(tooLarge).ErrorCode);
        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, Assert.Single(invalidUnicode).ErrorCode);
    }

    [Theory]
    [InlineData(KejiProviderStreamEventKind.Token)]
    [InlineData(KejiProviderStreamEventKind.ReasoningToken)]
    public async Task ChoiceTextAggregate_IsBoundedAt4MiB(KejiProviderStreamEventKind eventType)
    {
        var maximumDelta = new string('a', 256 * 1024);
        var acceptedEvents = Enumerable.Range(0, 16)
            .Select(_ => eventType == KejiProviderStreamEventKind.Token
                ? ChatCompletionStreamEvent.Token(maximumDelta)
                : ChatCompletionStreamEvent.ReasoningToken(maximumDelta))
            .Append(ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, hasToolCalls: false))
            .Append(ChatCompletionStreamEvent.Done())
            .ToArray();
        var rejectedEvents = Enumerable.Range(0, 17)
            .Select(_ => eventType == KejiProviderStreamEventKind.Token
                ? ChatCompletionStreamEvent.Token(maximumDelta)
                : ChatCompletionStreamEvent.ReasoningToken(maximumDelta))
            .ToArray();

        var accepted = await KejiSseAdapter.ToSseEvents(AsyncEnumerable(acceptedEvents)).ToListAsync();
        var rejected = await KejiSseAdapter.ToSseEvents(AsyncEnumerable(rejectedEvents)).ToListAsync();

        Assert.Equal(KejiSseEventType.Done, accepted[^1].EventType);
        Assert.Equal(KejiProviderErrorCode.StreamProtocolError, rejected[^1].ErrorCode);
    }

    [Fact]
    public async Task OversizedUnknownErrorCode_IsReducedToGenericBoundedError()
    {
        var results = await KejiSseAdapter.ToSseEvents(
            AsyncEnumerable(ChatCompletionStreamEvent.Error(KejiProviderErrorCode.ProviderError, "sk-secret"))).ToListAsync();
        var error = Assert.Single(results, static e => e.EventType == KejiSseEventType.Error);
        var wire = KejiSseFormatter.FormatEvent(error);

        Assert.Equal(KejiProviderErrorCode.ProviderError, error.ErrorCode);
        Assert.Equal("Model provider request failed", error.ErrorMessage);
        Assert.DoesNotContain("sk-secret", wire, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationDuringGlobalDoneCommit_StopsBeforeToolCallRelease()
    {
        using var cts = new CancellationTokenSource();
        var providerEvents = AsyncEnumerable(
            ChatCompletionStreamEvent.ToolCallBegin("call_1", "tool"),
            ChatCompletionStreamEvent.ToolCallDelta("{}", toolCallId: "call_1"),
            ChatCompletionStreamEvent.ToolCallEnd("call_1"),
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.ToolCalls, hasToolCalls: true),
            ChatCompletionStreamEvent.Done());
        await using var enumerator = KejiSseAdapter
            .ToSseEvents(providerEvents, cts.Token)
            .GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(KejiSseEventType.Answering, enumerator.Current.EventType);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await enumerator.MoveNextAsync().AsTask());
    }

    private static async IAsyncEnumerable<ChatCompletionStreamEvent> AsyncEnumerable(
        params ChatCompletionStreamEvent[] events)
    {
        foreach (var streamEvent in events)
        {
            await Task.Yield();
            yield return streamEvent;
        }
    }

    private static async IAsyncEnumerable<ChatCompletionStreamEvent> TerminalThenExtra(
        ChatCompletionStreamEvent terminal,
        EnumerationProbe probe,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        probe.Started = true;
        try
        {
            yield return ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, hasToolCalls: false);
            yield return terminal;
            cancellationToken.ThrowIfCancellationRequested();
            probe.ReadPastTerminal = true;
            yield return ChatCompletionStreamEvent.Token("must not be read");
        }
        finally
        {
            probe.Disposed = true;
        }
    }

    private static async IAsyncEnumerable<ChatCompletionStreamEvent> ThrowingSource(
        EnumerationProbe probe,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        probe.Started = true;
        try
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Authorization: Bearer sk-secret\r\n at StackTrace");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
        finally
        {
            probe.Disposed = true;
        }
    }

    private static async IAsyncEnumerable<ChatCompletionStreamEvent> ProbeSource(
        EnumerationProbe probe,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        probe.Started = true;
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        yield return ChatCompletionStreamEvent.Done();
    }

    private static async IAsyncEnumerable<ChatCompletionStreamEvent> TrackedSource(
        EnumerationProbe probe,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        probe.Started = true;
        try
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return ChatCompletionStreamEvent.Token("one");
            cancellationToken.ThrowIfCancellationRequested();
            yield return ChatCompletionStreamEvent.Done();
        }
        finally
        {
            probe.Disposed = true;
        }
    }

    private sealed class EnumerationProbe
    {
        public bool Started { get; set; }
        public bool ReadPastTerminal { get; set; }
        public bool Disposed { get; set; }
    }

    private sealed class SequenceTimeProvider(params DateTimeOffset[] values) : TimeProvider
    {
        private readonly Queue<DateTimeOffset> _values = new(values);

        public override DateTimeOffset GetUtcNow()
        {
            Assert.NotEmpty(_values);
            return _values.Dequeue();
        }
    }

    private sealed class BoundaryEnumerable(
        IReadOnlyList<ChatCompletionStreamEvent> events,
        string? acquisitionError = null,
        string? disposalError = null,
        bool returnNullEnumerator = false) : IAsyncEnumerable<ChatCompletionStreamEvent>
    {
        public IAsyncEnumerator<ChatCompletionStreamEvent> GetAsyncEnumerator(
            CancellationToken cancellationToken = default)
        {
            if (acquisitionError is not null)
                throw new InvalidOperationException(acquisitionError);
            if (returnNullEnumerator)
                return null!;
            return new BoundaryEnumerator(events, disposalError, cancellationToken);
        }

        private sealed class BoundaryEnumerator(
            IReadOnlyList<ChatCompletionStreamEvent> events,
            string? disposalError,
            CancellationToken cancellationToken) : IAsyncEnumerator<ChatCompletionStreamEvent>
        {
            private int _index = -1;

            public ChatCompletionStreamEvent Current => events[_index];

            public ValueTask<bool> MoveNextAsync()
            {
                cancellationToken.ThrowIfCancellationRequested();
                _index++;
                return ValueTask.FromResult(_index < events.Count);
            }

            public ValueTask DisposeAsync()
            {
                if (disposalError is not null)
                    throw new InvalidOperationException(disposalError);
                return ValueTask.CompletedTask;
            }
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
