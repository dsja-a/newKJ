using System.Runtime.CompilerServices;
using Keji.Providers;
using Keji.Streaming;

namespace Keji.Streaming.Tests;

public sealed class KejiSseSecurityTests
{
    private static readonly DateTime FixedTimestamp = new(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ProviderError_RawMessageAndHeaderValueNeverReachEventOrWire()
    {
        const string secret = "sk-live-super-secret";
        var providerEvents = Source(
            ChatCompletionStreamEvent.Error(
                "AUTH_FAILED",
                $"Authorization: Bearer {secret}\r\nInjected: true\r\nStackTrace"));

        var result = Assert.Single(await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync());
        var wire = KejiSseFormatter.FormatEvent(result);

        Assert.Equal("AUTH_FAILED", result.ErrorCode);
        Assert.Equal("Provider authentication failed", result.ErrorMessage);
        Assert.DoesNotContain(secret, wire, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization", wire, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Injected", wire, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", wire, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownErrorCodeWithControlCharacters_BecomesGenericAllowlistedError()
    {
        var providerEvents = Source(
            ChatCompletionStreamEvent.Error("BOOM\r\nevent: answer", "database password=hunter2"));

        var result = Assert.Single(await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync());
        var wire = KejiSseFormatter.FormatEvent(result);

        Assert.Equal("PROVIDER_ERROR", result.ErrorCode);
        Assert.Equal("Model provider request failed", result.ErrorMessage);
        Assert.Contains("\"error_code\":\"PROVIDER_ERROR\"", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("event: answer", wire, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpstreamException_TextNeverReachesEventOrWire()
    {
        const string secret = "token-value-from-exception";

        var result = Assert.Single(await KejiSseAdapter.ToSseEvents(ThrowWithSecret(secret)).ToListAsync());
        var wire = KejiSseFormatter.FormatEvent(result);

        Assert.Equal("PROVIDER_ERROR", result.ErrorCode);
        Assert.Equal("Model provider request failed", result.ErrorMessage);
        Assert.DoesNotContain(secret, wire, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", wire, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToolArguments_AreAbsentFromAdapterEventsAndEveryWireFrame()
    {
        const string secretArguments = "{\"command\":\"rm -rf /\",\"password\":\"hunter2\"}";
        var providerEvents = Source(
            ChatCompletionStreamEvent.ToolCallBegin("call_1", "execute", 0, 0),
            ChatCompletionStreamEvent.ToolCallDelta(secretArguments, 0, 0, "call_1"),
            ChatCompletionStreamEvent.ToolCallEnd("call_1", 0, 0),
            ChatCompletionStreamEvent.ChoiceFinished("tool_calls", hasToolCalls: true),
            ChatCompletionStreamEvent.Done());

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();
        var wire = string.Concat(results.Select(KejiSseFormatter.FormatEvent));

        var toolCall = Assert.Single(results, static item => item.EventType == KejiSseEventType.ToolCall);
        Assert.Equal("execute", toolCall.ToolName);
        Assert.Equal("call_1", toolCall.ToolCallId);
        Assert.Null(toolCall.Delta);
        Assert.DoesNotContain("hunter2", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("rm -rf", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("arguments", wire, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarkerEvent_RejectsFieldSmugglingBySerializationWhitelist()
    {
        var streamEvent = new KejiSseEvent
        {
            EventType = KejiSseEventType.Done,
            Phase = KejiSsePhase.Done,
            Delta = "sk-secret-delta",
            ToolName = "secret_tool",
            ToolCallId = "secret_call",
            ToolCallIndex = 99,
            ChoiceIndex = 88,
            Usage = new TokenUsage { PromptTokens = 1, CompletionTokens = 2 },
            ErrorCode = "AUTH_FAILED",
            ErrorMessage = "password=hunter2",
            Sequence = 1,
            EventId = "evt_1",
            TimestampUtc = FixedTimestamp,
        };

        var wire = KejiSseFormatter.FormatEvent(streamEvent);

        Assert.DoesNotContain("sk-secret-delta", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("secret_tool", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("secret_call", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("AUTH_FAILED", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("usage", wire, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("evt_0\r\nevent: answer")]
    [InlineData("evt_0\ndata: {\"admin\":true}")]
    [InlineData("evt_0\0suffix")]
    public void EventIdInjection_IsRejectedBeforeWireFormatting(string eventId)
    {
        var streamEvent = new KejiSseEvent
        {
            EventType = KejiSseEventType.Done,
            Phase = KejiSsePhase.Done,
            Sequence = 0,
            EventId = eventId,
            TimestampUtc = FixedTimestamp,
        };

        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(streamEvent));
    }

    [Theory]
    [InlineData("tool\r\nevent: done", "call_1")]
    [InlineData("safe_tool", "call_1\0suffix")]
    public void ToolMetadataControlCharacters_AreRejected(string toolName, string toolCallId)
    {
        var streamEvent = new KejiSseEvent
        {
            EventType = KejiSseEventType.ToolCall,
            Phase = KejiSsePhase.Answering,
            ToolName = toolName,
            ToolCallId = toolCallId,
            ToolCallIndex = 0,
            ChoiceIndex = 0,
            Sequence = 0,
            EventId = "evt_0",
            TimestampUtc = FixedTimestamp,
        };

        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(streamEvent));
    }

    private static async IAsyncEnumerable<ChatCompletionStreamEvent> Source(
        params ChatCompletionStreamEvent[] events)
    {
        foreach (var streamEvent in events)
        {
            await Task.Yield();
            yield return streamEvent;
        }
    }

    private static async IAsyncEnumerable<ChatCompletionStreamEvent> ThrowWithSecret(
        string secret,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException($"InvalidOperationException Authorization: Bearer {secret}");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
