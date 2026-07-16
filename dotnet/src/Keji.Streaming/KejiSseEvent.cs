using Keji.Providers;

namespace Keji.Streaming;

public enum KejiSseEventType
{
    Invalid = 0,
    Thinking = 1,
    ThinkToken = 2,
    Answering = 3,
    Answer = 4,
    ToolCall = 5,
    ToolResult = 6,
    Usage = 7,
    Error = 8,
    Done = 9,
    SystemNotice = 10,
}

public enum KejiSsePhase
{
    Invalid = 0,
    Thinking = 1,
    Answering = 2,
    Done = 3,
    Error = 4,
}

public sealed class KejiSseEvent
{
    public const int CurrentProtocolVersion = 1;

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
    public int ProtocolVersion => CurrentProtocolVersion;
    public long Sequence { get; init; }
    public string EventId { get; init; } = "";
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}
