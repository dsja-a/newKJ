using System.Collections.Immutable;

namespace Keji.Agent;

public enum KejiAgentEventType
{
    Invalid = 0,
    RunStarted = 1,
    ThinkingStarted = 2,
    ThinkingDelta = 3,
    AnsweringStarted = 4,
    AnswerDelta = 5,
    ToolStarted = 6,
    ToolCompleted = 7,
    Usage = 8,
    Error = 9,
    RunCompleted = 10,
}

public enum KejiAgentStopReason
{
    Invalid = 0,
    Completed = 1,
    Length = 2,
    ContentFiltered = 3,
    IterationLimit = 4,
    ToolCallLimit = 5,
    ContextLimit = 6,
    Cancelled = 7,
    TimedOut = 8,
    Failed = 9,
    SessionBusy = 10,
    EmptyResponse = 11,
}

public enum KejiAgentErrorCode
{
    Invalid = 0,
    InvalidRequest = 1,
    Unauthenticated = 2,
    ConversationNotFound = 3,
    SessionBusy = 4,
    ProviderNotFound = 5,
    ProviderTimeout = 6,
    ProviderRejected = 7,
    ProviderUnavailable = 8,
    ProviderProtocolError = 9,
    ToolRejected = 10,
    ToolFailed = 11,
    ContextLimit = 12,
    ToolCallLimit = 13,
    IterationLimit = 14,
    RunTimedOut = 15,
    PersistenceFailed = 16,
    InternalFailure = 17,
}

public sealed record KejiAgentUsage
{
    public const long MaxTokenCount = 1_000_000_000;
    public long PromptTokens { get; }
    public long CompletionTokens { get; }
    public long CachedTokens { get; }
    public long TotalTokens => checked(PromptTokens + CompletionTokens);

    public KejiAgentUsage(long promptTokens = 0, long completionTokens = 0, long cachedTokens = 0)
    {
        if (promptTokens is < 0 or > MaxTokenCount || completionTokens is < 0 or > MaxTokenCount ||
            cachedTokens is < 0 or > MaxTokenCount || cachedTokens > promptTokens)
            throw new ArgumentOutOfRangeException(nameof(promptTokens), "Agent usage is invalid.");
        _ = checked(promptTokens + completionTokens);
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
        CachedTokens = cachedTokens;
    }

    public KejiAgentUsage Add(long promptTokens, long completionTokens, long cachedTokens) =>
        new(checked(PromptTokens + promptTokens), checked(CompletionTokens + completionTokens),
            checked(CachedTokens + cachedTokens));
}

public enum KejiAgentTranscriptRole
{
    Invalid = 0,
    System = 1,
    User = 2,
    Assistant = 3,
    Tool = 4,
}

public sealed record KejiAgentTranscriptMessage(
    KejiAgentTranscriptRole Role,
    string? Content = null,
    string? ToolCallId = null,
    string? ToolName = null);

public sealed record KejiAgentTranscript(
    string RunId,
    string ConversationId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    KejiAgentStopReason StopReason,
    int Iterations,
    int ToolCallCount,
    ImmutableArray<string> ToolsUsed,
    string FinalContent,
    KejiAgentUsage Usage,
    ImmutableArray<KejiAgentTranscriptMessage> Messages)
{
    public int ToolCalls => ToolCallCount;
    public string AssistantContent => FinalContent;
}

public sealed record KejiAgentEvent
{
    public required string RunId { get; init; }
    public required long Sequence { get; init; }
    public required KejiAgentEventType Type { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public int Iteration { get; init; }
    public int ChoiceIndex { get; init; }
    public int? ToolCallIndex { get; init; }
    public long? ToolDurationMs { get; init; }
    public string? ContentDelta { get; init; }
    public string? ToolCallId { get; init; }
    public string? ToolName { get; init; }
    public bool? ToolSucceeded { get; init; }
    public string? ToolErrorCode { get; init; }
    public string? SafeErrorMessage { get; init; }
    public KejiAgentUsage? Usage { get; init; }
    public KejiAgentStopReason StopReason { get; init; }
    public KejiAgentErrorCode ErrorCode { get; init; }
    public KejiAgentTranscript? Transcript { get; init; }
}

public sealed class KejiAgentRunRequest
{
    public string RunId { get; init; } = string.Empty;
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
