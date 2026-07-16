using System.Text.Json;
using Keji.Providers;
using Keji.Streaming;

namespace Keji.Streaming.Tests;

public sealed class KejiSseFormatterTests
{
    private static readonly DateTime FixedTimestamp = new(2026, 7, 15, 8, 30, 45, DateTimeKind.Utc);

    [Theory]
    [InlineData(KejiSseEventType.Thinking, "thinking")]
    [InlineData(KejiSseEventType.ThinkToken, "think_token")]
    [InlineData(KejiSseEventType.Answering, "answering")]
    [InlineData(KejiSseEventType.Answer, "answer")]
    [InlineData(KejiSseEventType.ToolCall, "tool_call")]
    [InlineData(KejiSseEventType.Usage, "usage")]
    [InlineData(KejiSseEventType.Error, "error")]
    [InlineData(KejiSseEventType.Done, "done")]
    public void EveryEventType_HasStableWireName(KejiSseEventType eventType, string expectedName)
    {
        var frame = ParseFrame(KejiSseFormatter.FormatEvent(CreateValidEvent(eventType)));

        Assert.Equal(expectedName, frame.EventName);
    }

    [Fact]
    public void WireFrame_UsesEventIdDataAndLfOnly()
    {
        var frame = KejiSseFormatter.FormatEvent(CreateValidEvent(KejiSseEventType.Done));

        Assert.Equal(
            "id: 00000000000000000000000000000007\nevent: done\ndata: {\"protocol_version\":1,\"sequence\":7,\"event_id\":\"00000000000000000000000000000007\",\"timestamp_utc\":\"2026-07-15T08:30:45.0000000Z\",\"phase\":\"done\"}\n\n",
            frame);
        Assert.DoesNotContain('\r', frame);
        Assert.EndsWith("\n\n", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void Envelope_ContainsOnlyRequiredMetadataForMarkerEvents()
    {
        using var document = ParseFrame(KejiSseFormatter.FormatEvent(CreateValidEvent(KejiSseEventType.Thinking))).Document;

        Assert.Equal(
            ["protocol_version", "sequence", "event_id", "timestamp_utc", "phase", "choice_index"],
            document.RootElement.EnumerateObject().Select(static property => property.Name));
        Assert.Equal(1, document.RootElement.GetProperty("protocol_version").GetInt32());
        Assert.Equal(7, document.RootElement.GetProperty("sequence").GetInt64());
        Assert.Equal("00000000000000000000000000000007", document.RootElement.GetProperty("event_id").GetString());
        Assert.Equal("thinking", document.RootElement.GetProperty("phase").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("choice_index").GetInt32());
        Assert.Equal(FixedTimestamp, document.RootElement.GetProperty("timestamp_utc").GetDateTime());
    }

    [Theory]
    [InlineData(KejiSseEventType.ThinkToken, "thinking")]
    [InlineData(KejiSseEventType.Answer, "answering")]
    public void TokenPayload_HasOnlyChoiceAndDelta(KejiSseEventType eventType, string expectedPhase)
    {
        var streamEvent = CreateValidEvent(eventType) with
        {
            ToolName = "must_not_leak",
            ToolCallId = "must_not_leak",
            ToolCallIndex = 99,
            Usage = new TokenUsage { PromptTokens = 100, CompletionTokens = 200 },
            ErrorCode = KejiProviderErrorCode.AuthFailed,
            ErrorMessage = "sk-must-not-leak",
        };

        var frame = KejiSseFormatter.FormatEvent(streamEvent);
        using var document = ParseFrame(frame).Document;

        Assert.Equal(
            ["protocol_version", "sequence", "event_id", "timestamp_utc", "phase", "choice_index", "delta"],
            document.RootElement.EnumerateObject().Select(static property => property.Name));
        Assert.Equal(expectedPhase, document.RootElement.GetProperty("phase").GetString());
        Assert.Equal(4, document.RootElement.GetProperty("choice_index").GetInt32());
        Assert.Equal("public delta", document.RootElement.GetProperty("delta").GetString());
        Assert.DoesNotContain("must_not_leak", frame, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-must-not-leak", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolPayload_IsCorrelatedAndExcludesArgumentsOrSmuggledFields()
    {
        var streamEvent = CreateValidEvent(KejiSseEventType.ToolCall) with
        {
            Delta = "{\"password\":\"secret\"}",
            Usage = new TokenUsage { PromptTokens = 1, CompletionTokens = 2 },
            ErrorMessage = "StackTrace sk-secret",
        };

        var frame = KejiSseFormatter.FormatEvent(streamEvent);
        using var document = ParseFrame(frame).Document;

        Assert.Equal(
            [
                "protocol_version",
                "sequence",
                "event_id",
                "timestamp_utc",
                "phase",
                "choice_index",
                "tool_call_index",
                "tool_call_id",
                "tool",
            ],
            document.RootElement.EnumerateObject().Select(static property => property.Name));
        Assert.Equal(2, document.RootElement.GetProperty("choice_index").GetInt32());
        Assert.Equal(3, document.RootElement.GetProperty("tool_call_index").GetInt32());
        Assert.Equal("call_123", document.RootElement.GetProperty("tool_call_id").GetString());
        Assert.Equal("read_file", document.RootElement.GetProperty("tool").GetString());
        Assert.DoesNotContain("password", frame, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", frame, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("arguments", frame, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsagePayload_UsesSnakeCaseAndCalculatedTotal()
    {
        using var document = ParseFrame(
            KejiSseFormatter.FormatEvent(CreateValidEvent(KejiSseEventType.Usage))).Document;

        var usage = document.RootElement.GetProperty("usage");
        Assert.Equal(["prompt_tokens", "completion_tokens", "total_tokens"], usage.EnumerateObject().Select(static property => property.Name));
        Assert.Equal(10, usage.GetProperty("prompt_tokens").GetInt64());
        Assert.Equal(6, usage.GetProperty("completion_tokens").GetInt64());
        Assert.Equal(16, usage.GetProperty("total_tokens").GetInt64());
    }

    [Theory]
    [InlineData(KejiProviderErrorCode.RateLimited, "RateLimited", "Provider rate limit exceeded")]
    [InlineData(KejiProviderErrorCode.Invalid, "ProviderError", "Model provider request failed")]
    [InlineData(KejiProviderErrorCode.ProviderError, "ProviderError", "Model provider request failed")]
    public void ErrorPayload_IsDerivedOnlyFromAllowlistedCode(
        KejiProviderErrorCode inputCode,
        string expectedCode,
        string expectedMessage)
    {
        var streamEvent = CreateValidEvent(KejiSseEventType.Error) with
        {
            ErrorCode = inputCode,
            ErrorMessage = "Authorization: Bearer sk-secret\r\nStackTrace",
            Delta = "secret-delta",
            ToolName = "secret-tool",
        };

        var frame = KejiSseFormatter.FormatEvent(streamEvent);
        using var document = ParseFrame(frame).Document;

        Assert.Equal(
            ["protocol_version", "sequence", "event_id", "timestamp_utc", "phase", "error_code", "error"],
            document.RootElement.EnumerateObject().Select(static property => property.Name));
        Assert.Equal(expectedCode, document.RootElement.GetProperty("error_code").GetString());
        Assert.Equal(expectedMessage, document.RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain("sk-secret", frame, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", frame, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-delta", frame, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-tool", frame, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("00000000000000000000000000000007\r\nevent: injected")]
    [InlineData("00000000000000000000000000000007\nid: injected")]
    [InlineData("00000000000000000000000000000007\0suffix")]
    public void EventId_ControlCharacterInjection_IsRejected(string eventId)
    {
        var streamEvent = CreateValidEvent(KejiSseEventType.Done) with { EventId = eventId };

        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(streamEvent));
    }

    [Fact]
    public void EventId_MustBe32CharacterHexGuid()
    {
        var streamEvent = CreateValidEvent(KejiSseEventType.Done) with { EventId = "not-a-valid-hex-guid" };

        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(streamEvent));
    }

    [Fact]
    public void NullEventId_IsRejected()
    {
        var streamEvent = CreateValidEvent(KejiSseEventType.Done) with { EventId = null };

        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(streamEvent));
    }

    [Theory]
    [InlineData(KejiSseEventType.Thinking, KejiSsePhase.Answering)]
    [InlineData(KejiSseEventType.ThinkToken, KejiSsePhase.Done)]
    [InlineData(KejiSseEventType.Answering, KejiSsePhase.Thinking)]
    [InlineData(KejiSseEventType.Answer, KejiSsePhase.Error)]
    [InlineData(KejiSseEventType.ToolCall, KejiSsePhase.Done)]
    [InlineData(KejiSseEventType.Usage, KejiSsePhase.Answering)]
    [InlineData(KejiSseEventType.Error, KejiSsePhase.Done)]
    [InlineData(KejiSseEventType.Done, KejiSsePhase.Error)]
    public void EventTypeAndPhaseMismatch_IsRejected(KejiSseEventType eventType, KejiSsePhase phase)
    {
        var streamEvent = CreateValidEvent(eventType) with { Phase = phase };

        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(streamEvent));
    }

    [Fact]
    public void UnknownEventTypeOrPhase_IsRejected()
    {
        var invalidType = CreateValidEvent(KejiSseEventType.Done) with { EventType = (KejiSseEventType)999 };
        var invalidPhase = CreateValidEvent(KejiSseEventType.Done) with { Phase = (KejiSsePhase)999 };

        Assert.Throws<ArgumentOutOfRangeException>(() => KejiSseFormatter.FormatEvent(invalidType));
        Assert.Throws<ArgumentOutOfRangeException>(() => KejiSseFormatter.FormatEvent(invalidPhase));
    }

    [Theory]
    [InlineData(KejiSseEventType.ThinkToken)]
    [InlineData(KejiSseEventType.Answer)]
    public void TokenRequiresNonNullContentAndNonNegativeChoice(KejiSseEventType eventType)
    {
        var missingContent = CreateValidEvent(eventType) with { Delta = null };
        var invalidChoice = CreateValidEvent(eventType) with { ChoiceIndex = -1 };
        var choiceTooLarge = CreateValidEvent(eventType) with { ChoiceIndex = 1025 };

        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(missingContent));
        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(invalidChoice));
        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(choiceTooLarge));
    }

    [Theory]
    [InlineData(KejiSseEventType.Thinking)]
    [InlineData(KejiSseEventType.Answering)]
    public void PhaseMarkersRequireChoiceIndex(KejiSseEventType eventType)
    {
        var missing = CreateValidEvent(eventType) with { ChoiceIndex = null };
        var negative = CreateValidEvent(eventType) with { ChoiceIndex = -1 };
        var tooLarge = CreateValidEvent(eventType) with { ChoiceIndex = 1025 };

        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(missing));
        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(negative));
        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(tooLarge));
    }

    [Theory]
    [InlineData(KejiSseEventType.Thinking)]
    [InlineData(KejiSseEventType.ThinkToken)]
    [InlineData(KejiSseEventType.Answering)]
    [InlineData(KejiSseEventType.Answer)]
    public void ChoiceIndex1024_IsAcceptedForChoiceScopedEvents(KejiSseEventType eventType)
    {
        var streamEvent = CreateValidEvent(eventType) with { ChoiceIndex = 1024 };

        using var document = ParseFrame(KejiSseFormatter.FormatEvent(streamEvent)).Document;

        Assert.Equal(1024, document.RootElement.GetProperty("choice_index").GetInt32());
    }

    [Theory]
    [InlineData(KejiSseEventType.ThinkToken)]
    [InlineData(KejiSseEventType.Answer)]
    public void TokenDelta_Enforces256KiBStrictUtf8Boundary(KejiSseEventType eventType)
    {
        var exactAscii = CreateValidEvent(eventType) with { Delta = new string('a', 256 * 1024) };
        var tooLargeAscii = CreateValidEvent(eventType) with { Delta = new string('a', 256 * 1024 + 1) };
        var exactMultibyte = CreateValidEvent(eventType) with { Delta = new string('界', 87_381) };
        var tooLargeMultibyte = CreateValidEvent(eventType) with { Delta = new string('界', 87_382) };
        var invalidUnicode = CreateValidEvent(eventType) with { Delta = "\uD800" };

        Assert.NotEmpty(KejiSseFormatter.FormatEvent(exactAscii));
        Assert.NotEmpty(KejiSseFormatter.FormatEvent(exactMultibyte));
        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(tooLargeAscii));
        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(tooLargeMultibyte));
        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(invalidUnicode));
    }

    [Theory]
    [InlineData("bad tool")]
    [InlineData("bad\r\nevent")]
    [InlineData("工具")]
    public void ToolNameOutsideAllowlist_IsRejected(string toolName)
    {
        var streamEvent = CreateValidEvent(KejiSseEventType.ToolCall) with { ToolName = toolName };

        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(streamEvent));
    }

    [Theory]
    [InlineData("")]
    [InlineData("call\r\nid: injected")]
    [InlineData("call\0suffix")]
    public void InvalidToolCallId_IsRejected(string toolCallId)
    {
        var streamEvent = CreateValidEvent(KejiSseEventType.ToolCall) with { ToolCallId = toolCallId };

        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(streamEvent));
    }

    [Fact]
    public void ToolChoiceAndCallIndices_AreBoundedAt1024()
    {
        var atLimit = CreateValidEvent(KejiSseEventType.ToolCall) with
        {
            ChoiceIndex = 1024,
            ToolCallIndex = 1024,
        };
        var choiceTooLarge = CreateValidEvent(KejiSseEventType.ToolCall) with { ChoiceIndex = 1025 };
        var toolTooLarge = CreateValidEvent(KejiSseEventType.ToolCall) with { ToolCallIndex = 1025 };
        var toolNegative = CreateValidEvent(KejiSseEventType.ToolCall) with { ToolCallIndex = -1 };

        Assert.NotEmpty(KejiSseFormatter.FormatEvent(atLimit));
        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(choiceTooLarge));
        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(toolTooLarge));
        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(toolNegative));
    }

    [Fact]
    public void UsageRejectsNegativeOrOverflowingCounts()
    {
        var negative = CreateValidEvent(KejiSseEventType.Usage) with
        {
            Usage = new TokenUsage { PromptTokens = -1, CompletionTokens = 2 },
        };
        var overflow = CreateValidEvent(KejiSseEventType.Usage) with
        {
            Usage = new TokenUsage { PromptTokens = long.MaxValue, CompletionTokens = 1 },
        };

        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(negative));
        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(overflow));
    }

    [Fact]
    public void UsageEnforcesReportedTokenCap()
    {
        var atLimit = CreateValidEvent(KejiSseEventType.Usage) with
        {
            Usage = new TokenUsage { PromptTokens = 600_000_000, CompletionTokens = 400_000_000 },
        };
        var promptTooLarge = CreateValidEvent(KejiSseEventType.Usage) with
        {
            Usage = new TokenUsage { PromptTokens = 1_000_000_001, CompletionTokens = 0 },
        };
        var totalTooLarge = CreateValidEvent(KejiSseEventType.Usage) with
        {
            Usage = new TokenUsage { PromptTokens = 600_000_000, CompletionTokens = 400_000_001 },
        };

        Assert.NotEmpty(KejiSseFormatter.FormatEvent(atLimit));
        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(promptTooLarge));
        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(totalTooLarge));
    }

    [Fact]
    public void SequenceAndTimestampMustBeValid()
    {
        var negativeSequence = CreateValidEvent(KejiSseEventType.Done) with { Sequence = -1, EventId = null };
        var defaultTimestamp = CreateValidEvent(KejiSseEventType.Done) with { TimestampUtc = default };
        var localTimestamp = CreateValidEvent(KejiSseEventType.Done) with
        {
            TimestampUtc = DateTime.SpecifyKind(FixedTimestamp, DateTimeKind.Local),
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => KejiSseFormatter.FormatEvent(negativeSequence));
        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(defaultTimestamp));
        Assert.Throws<ArgumentException>(() => KejiSseFormatter.FormatEvent(localTimestamp));
    }

    [Fact]
    public void ThinkingPhaseHelper_UsesProvidedClockAndSequence()
    {
        var expectedTime = new DateTimeOffset(2026, 7, 15, 1, 2, 3, TimeSpan.Zero);
        var streamEvent = new KejiSseEvent
        {
            EventType = KejiSseEventType.Thinking,
            Phase = KejiSsePhase.Thinking,
            Sequence = 12,
            ChoiceIndex = 9,
            TimestampUtc = expectedTime.UtcDateTime,
            EventId = "0000000000000000000000000000000c"
        };

        var frame = ParseFrame(KejiSseFormatter.FormatEvent(streamEvent));
        using var document = frame.Document;

        Assert.Equal("thinking", frame.EventName);
        Assert.Equal("0000000000000000000000000000000c", frame.Id);
        Assert.Equal(12, document.RootElement.GetProperty("sequence").GetInt64());
        Assert.Equal(9, document.RootElement.GetProperty("choice_index").GetInt32());
        Assert.Equal(expectedTime.UtcDateTime, document.RootElement.GetProperty("timestamp_utc").GetDateTime());
    }

    private static EventSpec CreateValidEvent(KejiSseEventType eventType)
    {
        var phase = eventType switch
        {
            KejiSseEventType.Thinking or KejiSseEventType.ThinkToken => KejiSsePhase.Thinking,
            KejiSseEventType.Answering or KejiSseEventType.Answer or KejiSseEventType.ToolCall => KejiSsePhase.Answering,
            KejiSseEventType.Usage or KejiSseEventType.Done => KejiSsePhase.Done,
            KejiSseEventType.Error => KejiSsePhase.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(eventType)),
        };

        return new EventSpec
        {
            EventType = eventType,
            Phase = phase,
            Delta = eventType is KejiSseEventType.ThinkToken or KejiSseEventType.Answer ? "public delta" : null,
            ChoiceIndex = eventType switch
            {
                KejiSseEventType.ThinkToken or KejiSseEventType.Answer => 4,
                KejiSseEventType.Thinking or KejiSseEventType.Answering or KejiSseEventType.ToolCall => 2,
                _ => null,
            },
            ToolCallIndex = eventType == KejiSseEventType.ToolCall ? 3 : null,
            ToolCallId = eventType == KejiSseEventType.ToolCall ? "call_123" : null,
            ToolName = eventType == KejiSseEventType.ToolCall ? "read_file" : null,
            Usage = eventType == KejiSseEventType.Usage
                ? new TokenUsage { PromptTokens = 10, CompletionTokens = 6 }
                : null,
            ErrorCode = eventType == KejiSseEventType.Error ? KejiProviderErrorCode.RateLimited : KejiProviderErrorCode.Invalid,
            ErrorMessage = eventType == KejiSseEventType.Error ? "must be ignored" : null,
            Sequence = 7,
            EventId = "00000000000000000000000000000007",
            TimestampUtc = FixedTimestamp,
        };
    }

    private static ParsedFrame ParseFrame(string value)
    {
        Assert.DoesNotContain('\r', value);
        var lines = value.Split('\n');
        Assert.Equal(5, lines.Length);
        Assert.StartsWith("id: ", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("event: ", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("data: ", lines[2], StringComparison.Ordinal);
        Assert.Equal(string.Empty, lines[3]);
        Assert.Equal(string.Empty, lines[4]);

        return new ParsedFrame(
            lines[1]["event: ".Length..],
            lines[0]["id: ".Length..],
            JsonDocument.Parse(lines[2]["data: ".Length..]));
    }

    private sealed record ParsedFrame(string EventName, string Id, JsonDocument Document);

    private sealed record EventSpec
    {
        public KejiSseEventType EventType { get; init; }
        public KejiSsePhase Phase { get; init; }
        public string? Delta { get; init; }
        public string? ToolName { get; init; }
        public string? ToolCallId { get; init; }
        public int? ToolCallIndex { get; init; }
        public int? ChoiceIndex { get; init; }
        public TokenUsage? Usage { get; init; }
        public KejiProviderErrorCode ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
        public long Sequence { get; init; }
        public string? EventId { get; init; }
        public DateTime TimestampUtc { get; init; }

        public static implicit operator KejiSseEvent(EventSpec value) => new()
        {
            EventType = value.EventType,
            Phase = value.Phase,
            Delta = value.Delta,
            ToolName = value.ToolName,
            ToolCallId = value.ToolCallId,
            ToolCallIndex = value.ToolCallIndex,
            ChoiceIndex = value.ChoiceIndex,
            Usage = value.Usage,
            ErrorCode = value.ErrorCode,
            ErrorMessage = value.ErrorMessage,
            Sequence = value.Sequence,
            EventId = value.EventId ?? "",
            TimestampUtc = value.TimestampUtc,
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}
