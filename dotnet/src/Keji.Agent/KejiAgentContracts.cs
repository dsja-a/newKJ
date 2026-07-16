using System.Collections.Immutable;

namespace Keji.Agent;

public enum KejiAgentEventType
{
    RunStarted = 1,
    IterationStarted = 2,
    AssistantDelta = 3,
    ToolStarted = 4,
    ToolCompleted = 5,
    Usage = 6,
    RunCompleted = 7,
    Error = 8,
}

public enum KejiAgentStopReason
{
    None = 0,
    Completed = 1,
    Length = 2,
    ContentFiltered = 3,
    IterationLimit = 4,
    ToolCallLimit = 5,
    ContextLimit = 6,
    Cancelled = 7,
    TimedOut = 8,
    Failed = 9,
}

public enum KejiAgentErrorCode
{
    None = 0,
    InvalidRequest = 1,
    Unauthenticated = 2,
    ConversationNotFound = 3,
    ProviderNotFound = 4,
    ProviderTimeout = 5,
    ProviderRejected = 6,
    ProviderUnavailable = 7,
    ProviderProtocolError = 8,
    ToolRejected = 9,
    ToolFailed = 10,
    LimitExceeded = 11,
    SessionBusy = 12,
    PersistenceFailed = 13,
    AuditFailed = 14,
    InternalFailure = 15,
    ContextLimit = 16,
    ToolCallLimit = 17,
    RunTimedOut = 18,
}

public sealed record KejiAgentUsage(
    long PromptTokens = 0,
    long CompletionTokens = 0,
    long CachedTokens = 0)
{
    public long TotalTokens => checked(PromptTokens + CompletionTokens);

    public KejiAgentUsage Add(long promptTokens, long completionTokens, long cachedTokens) =>
        new(checked(PromptTokens + promptTokens), checked(CompletionTokens + completionTokens),
            checked(CachedTokens + cachedTokens));
}

public sealed record KejiAgentTranscript(
    string RunId,
    string ConversationId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    KejiAgentStopReason StopReason,
    int Iterations,
    int ToolCalls,
    string AssistantContent,
    KejiAgentUsage Usage);

public sealed record KejiAgentEvent
{
    public required string RunId { get; init; }
    public required long Sequence { get; init; }
    public required KejiAgentEventType Type { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public int Iteration { get; init; }
    public string? ContentDelta { get; init; }
    public string? ToolCallId { get; init; }
    public string? ToolName { get; init; }
    public bool? ToolSucceeded { get; init; }
    public string? ToolErrorCode { get; init; }
    public KejiAgentUsage? Usage { get; init; }
    public KejiAgentStopReason StopReason { get; init; }
    public KejiAgentErrorCode ErrorCode { get; init; }
    public KejiAgentTranscript? Transcript { get; init; }
}

public sealed class KejiAgentRunRequest
{
    public string ConversationId { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string UserMessage { get; init; } = string.Empty;
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }
}

public interface IKejiAgentLoop
{
    IAsyncEnumerable<KejiAgentEvent> RunStreamAsync(
        KejiAgentRunRequest request,
        CancellationToken cancellationToken = default);
}
