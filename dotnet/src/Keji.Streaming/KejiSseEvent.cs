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
    Done
}

public enum KejiSsePhase
{
    Thinking,
    Answering,
    Done,
    Error
}

public sealed class KejiSseEvent
{
    public KejiSseEventType EventType { get; init; }
    public KejiSsePhase Phase { get; init; }
    public string? Delta { get; init; }
    public string? ToolName { get; init; }
    public TokenUsage? Usage { get; init; }
    public string? ErrorMessage { get; init; }
}
