using System.Runtime.CompilerServices;
using Keji.Providers;
using Keji.Streaming;

namespace Keji.Agent;

public sealed class KejiAgentSseAdapter
{
    public async IAsyncEnumerable<string> AdaptAsync(
        IAsyncEnumerable<KejiAgentEvent> events,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        long sequence = 0;
        await foreach (var item in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var mapped = Map(item, ++sequence);
            yield return KejiSseFormatter.FormatEvent(mapped);
        }
    }

    private static KejiSseEvent Map(KejiAgentEvent item, long sequence)
    {
        Validate(item);
        var (eventType, phase) = item.Type switch
        {
            KejiAgentEventType.RunStarted => (KejiSseEventType.SystemNotice, KejiSsePhase.Answering),
            KejiAgentEventType.ThinkingStarted => (KejiSseEventType.Thinking, KejiSsePhase.Thinking),
            KejiAgentEventType.ThinkingDelta => (KejiSseEventType.ThinkToken, KejiSsePhase.Thinking),
            KejiAgentEventType.AnsweringStarted => (KejiSseEventType.Answering, KejiSsePhase.Answering),
            KejiAgentEventType.AnswerDelta => (KejiSseEventType.Answer, KejiSsePhase.Answering),
            KejiAgentEventType.ToolStarted => (KejiSseEventType.ToolCall, KejiSsePhase.Answering),
            KejiAgentEventType.ToolCompleted => (KejiSseEventType.ToolResult, KejiSsePhase.Answering),
            KejiAgentEventType.Usage => (KejiSseEventType.Usage, KejiSsePhase.Done),
            KejiAgentEventType.Error => (KejiSseEventType.Error, KejiSsePhase.Error),
            KejiAgentEventType.RunCompleted => (KejiSseEventType.Done, KejiSsePhase.Done),
            _ => throw new ArgumentException("Agent event type is invalid.", nameof(item)),
        };
        return new KejiSseEvent
        {
            EventType = eventType,
            Phase = phase,
            Delta = item.Type == KejiAgentEventType.RunStarted ? "run_started" : item.ContentDelta,
            ToolName = item.ToolName,
            ToolCallId = item.ToolCallId,
            ToolCallIndex = item.ToolCallIndex,
            ChoiceIndex = item.ChoiceIndex,
            Usage = item.Usage is null ? null : new TokenUsage
            {
                PromptTokens = item.Usage.PromptTokens,
                CompletionTokens = item.Usage.CompletionTokens,
                CachedTokens = item.Usage.CachedTokens,
            },
            ErrorCode = MapError(item.ErrorCode),
            Sequence = sequence,
            EventId = Guid.NewGuid().ToString("N"),
            TimestampUtc = item.TimestampUtc.UtcDateTime,
        };
    }

    private static KejiProviderErrorCode MapError(KejiAgentErrorCode code) => code switch
    {
        KejiAgentErrorCode.ProviderTimeout or KejiAgentErrorCode.RunTimedOut => KejiProviderErrorCode.Timeout,
        KejiAgentErrorCode.ProviderRejected => KejiProviderErrorCode.InvalidRequest,
        KejiAgentErrorCode.ProviderUnavailable => KejiProviderErrorCode.ServiceUnavailable,
        KejiAgentErrorCode.ProviderProtocolError => KejiProviderErrorCode.StreamProtocolError,
        KejiAgentErrorCode.InvalidRequest => KejiProviderErrorCode.InvalidRequest,
        _ => KejiProviderErrorCode.ProviderError,
    };

    private static void Validate(KejiAgentEvent item)
    {
        if (item.Type == KejiAgentEventType.Invalid || item.Sequence < 1 || !IsRunId(item.RunId) ||
            item.TimestampUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Agent event is invalid.", nameof(item));
    }

    private static bool IsRunId(string value) => value.Length == 32 &&
        value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
