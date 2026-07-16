using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Keji.Providers;

namespace Keji.Streaming;

public static class KejiSseFormatter
{
    private const int MaxDeltaBytes = 256 * 1024;
    private const long MaxReportedTokens = 1_000_000_000;
    private static readonly Encoding StrictUtf8 =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string FormatEvent(KejiSseEvent streamEvent)
    {
        ArgumentNullException.ThrowIfNull(streamEvent);
        ValidateEnvelope(streamEvent);

        var eventName = GetEventName(streamEvent.EventType);
        var phaseName = GetPhaseName(streamEvent.Phase);
        ValidatePhase(streamEvent.EventType, streamEvent.Phase);

        var data = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["protocol_version"] = streamEvent.ProtocolVersion,
            ["sequence"] = streamEvent.Sequence,
            ["event_id"] = streamEvent.EventId,
            ["timestamp_utc"] = streamEvent.TimestampUtc.ToString("O"),
            ["phase"] = phaseName,
        };

        switch (streamEvent.EventType)
        {
            case KejiSseEventType.SystemNotice:
                ValidateChoiceIndex(streamEvent.ChoiceIndex);
                data["choice_index"] = streamEvent.ChoiceIndex!.Value;
                if (!string.IsNullOrEmpty(streamEvent.Delta))
                    data["delta"] = streamEvent.Delta;
                break;

            case KejiSseEventType.Thinking:
            case KejiSseEventType.Answering:
                ValidateChoiceIndex(streamEvent.ChoiceIndex);
                data["choice_index"] = streamEvent.ChoiceIndex!.Value;
                break;

            case KejiSseEventType.Done:
                break;

            case KejiSseEventType.ThinkToken:
            case KejiSseEventType.Answer:
                if (string.IsNullOrEmpty(streamEvent.Delta))
                    throw new ArgumentException("Token events require content and a choice index", nameof(streamEvent));
                ValidateChoiceIndex(streamEvent.ChoiceIndex);
                ValidateDelta(streamEvent.Delta);
                data["choice_index"] = streamEvent.ChoiceIndex!.Value;
                data["delta"] = streamEvent.Delta;
                break;

            case KejiSseEventType.ToolCall:
            case KejiSseEventType.ToolResult:
                ValidateToolEvent(streamEvent);
                data["choice_index"] = streamEvent.ChoiceIndex!.Value;
                data["tool_call_index"] = streamEvent.ToolCallIndex!.Value;
                data["tool_call_id"] = streamEvent.ToolCallId;
                data["tool"] = streamEvent.ToolName;
                break;

            case KejiSseEventType.Usage:
                ValidateUsage(streamEvent.Usage);
                var usageData = new Dictionary<string, long>(StringComparer.Ordinal)
                {
                    ["prompt_tokens"] = streamEvent.Usage!.PromptTokens,
                    ["completion_tokens"] = streamEvent.Usage.CompletionTokens,
                    ["total_tokens"] = streamEvent.Usage.TotalTokens,
                };
                if (streamEvent.Usage.CachedTokens > 0)
                    usageData["cached_tokens"] = streamEvent.Usage.CachedTokens;
                data["usage"] = usageData;
                break;

            case KejiSseEventType.Error:
                var sanitizedError = ProviderErrorMapper.Sanitize(streamEvent.ErrorCode);
                data["error_code"] = sanitizedError.Code.ToString();
                data["error"] = sanitizedError.Message;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(streamEvent), "Unknown SSE event type");
        }

        var json = JsonSerializer.Serialize(data, JsonOptions);
        var builder = new StringBuilder(json.Length + 64);
        builder.Append("id: ").Append(streamEvent.EventId).Append('\n');
        builder.Append("event: ").Append(eventName).Append('\n');
        builder.Append("data: ").Append(json).Append("\n\n");
        return builder.ToString();
    }

    public static string FormatHeartbeat() => ": keep-alive\n\n";

    private static string GetEventName(KejiSseEventType eventType) => eventType switch
    {
        KejiSseEventType.SystemNotice => "system_notice",
        KejiSseEventType.Thinking => "thinking",
        KejiSseEventType.ThinkToken => "think_token",
        KejiSseEventType.Answering => "answering",
        KejiSseEventType.Answer => "answer",
        KejiSseEventType.ToolCall => "tool_call",
        KejiSseEventType.ToolResult => "tool_result",
        KejiSseEventType.Usage => "usage",
        KejiSseEventType.Error => "error",
        KejiSseEventType.Done => "done",
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), "Unknown SSE event type"),
    };

    private static string GetPhaseName(KejiSsePhase phase) => phase switch
    {
        KejiSsePhase.Thinking => "thinking",
        KejiSsePhase.Answering => "answering",
        KejiSsePhase.Done => "done",
        KejiSsePhase.Error => "error",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), "Unknown SSE phase"),
    };

    private static void ValidateEnvelope(KejiSseEvent streamEvent)
    {
        if (streamEvent.Sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(streamEvent), "SSE sequence must be non-negative");
        if (string.IsNullOrEmpty(streamEvent.EventId) || streamEvent.EventId.Length != 32 ||
            streamEvent.EventId.Any(c => c is < '0' or > '9' and < 'a' or > 'f'))
            throw new ArgumentException("SSE event ID must be a 32-character lowercase hex GUID", nameof(streamEvent));
        if (streamEvent.TimestampUtc == default || streamEvent.TimestampUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("SSE timestamp must be a UTC timestamp", nameof(streamEvent));
        if (streamEvent.ProtocolVersion != 1)
            throw new ArgumentException("SSE protocol version must be 1", nameof(streamEvent));
    }

    private static void ValidatePhase(KejiSseEventType eventType, KejiSsePhase phase)
    {
        var expectedPhase = eventType switch
        {
            KejiSseEventType.SystemNotice => KejiSsePhase.Answering,
            KejiSseEventType.Thinking or KejiSseEventType.ThinkToken => KejiSsePhase.Thinking,
            KejiSseEventType.Answering or KejiSseEventType.Answer or KejiSseEventType.ToolCall or KejiSseEventType.ToolResult => KejiSsePhase.Answering,
            KejiSseEventType.Usage or KejiSseEventType.Done => KejiSsePhase.Done,
            KejiSseEventType.Error => KejiSsePhase.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(eventType)),
        };

        if (phase != expectedPhase)
            throw new ArgumentException("SSE event type and phase are inconsistent", nameof(phase));
    }

    private static void ValidateToolEvent(KejiSseEvent streamEvent)
    {
        if (streamEvent.ChoiceIndex is not >= 0 or > 1024 ||
            streamEvent.ToolCallIndex is not >= 0 or > 1024 ||
            string.IsNullOrWhiteSpace(streamEvent.ToolCallId) ||
            streamEvent.ToolCallId.Length > 256 || streamEvent.ToolCallId.Any(char.IsControl) ||
            !IsValidUnicode(streamEvent.ToolCallId) ||
            string.IsNullOrWhiteSpace(streamEvent.ToolName) || streamEvent.ToolName.Length > 128 ||
            !streamEvent.ToolName.All(static character =>
                character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-' or '.'))
        {
            throw new ArgumentException("Tool-call SSE event is invalid", nameof(streamEvent));
        }
    }

    private static void ValidateUsage(TokenUsage? usage)
    {
        if (usage is null || usage.PromptTokens is < 0 or > MaxReportedTokens ||
            usage.CompletionTokens is < 0 or > MaxReportedTokens ||
            usage.PromptTokens > MaxReportedTokens - usage.CompletionTokens)
            throw new ArgumentException("Usage SSE event is invalid", nameof(usage));

        try
        {
            _ = usage.TotalTokens;
        }
        catch (OverflowException exception)
        {
            throw new ArgumentException("Usage SSE event is invalid", nameof(usage), exception);
        }
    }

    private static void ValidateChoiceIndex(int? choiceIndex)
    {
        if (choiceIndex is not >= 0 or > 1024)
            throw new ArgumentException("SSE event choice index is invalid", nameof(choiceIndex));
    }

    private static void ValidateDelta(string delta)
    {
        if (delta.Length > MaxDeltaBytes)
            throw new ArgumentException("SSE event delta exceeds the size limit", nameof(delta));

        try
        {
            if (StrictUtf8.GetByteCount(delta) > MaxDeltaBytes)
                throw new ArgumentException("SSE event delta exceeds the size limit", nameof(delta));
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("SSE event delta is not valid Unicode", nameof(delta), exception);
        }
    }

    private static bool IsValidUnicode(string value)
    {
        try
        {
            _ = StrictUtf8.GetByteCount(value);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }
}
