using Keji.Providers;

namespace Keji.Streaming;

public enum KejiSseEventType
{
    Thinking,
    ThinkToken,
    Answering,
    Answer,
    ToolCall,
    Usage,
    Error,
    Done,
}

public enum KejiSsePhase
{
    Thinking,
    Answering,
    Done,
    Error,
}

public sealed class KejiSseEvent
{
    public const string CurrentProtocolVersion = "1.0";

    public KejiSseEventType EventType { get; init; }
    public KejiSsePhase Phase { get; init; }
    public string? Delta { get; init; }
    public string? ToolName { get; init; }
    public string? ToolCallId { get; init; }
    public int? ToolCallIndex { get; init; }
    public int? ChoiceIndex { get; init; }
    public TokenUsage? Usage { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string ProtocolVersion => CurrentProtocolVersion;
    public long Sequence { get; init; }
    public string? EventId { get; init; }
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
}
