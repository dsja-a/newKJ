namespace Keji.Providers;

public enum ChatCompletionStreamEventType
{
    ReasoningToken,
    Token,
    ToolCallBegin,
    ToolCallDelta,
    ToolCallEnd,
    Usage,
    Error,
    Done
}

public sealed class ChatCompletionStreamEvent
{
    public ChatCompletionStreamEventType Type { get; init; }
    public string? Content { get; init; }
    public string? ToolCallId { get; init; }
    public string? ToolName { get; init; }
    public string? ToolArguments { get; init; }
    public TokenUsage? Usage { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static ChatCompletionStreamEvent ReasoningToken(string content) =>
        new() { Type = ChatCompletionStreamEventType.ReasoningToken, Content = content };

    public static ChatCompletionStreamEvent Token(string content) =>
        new() { Type = ChatCompletionStreamEventType.Token, Content = content };

    public static ChatCompletionStreamEvent ToolCallBegin(string id, string name) =>
        new() { Type = ChatCompletionStreamEventType.ToolCallBegin, ToolCallId = id, ToolName = name };

    public static ChatCompletionStreamEvent ToolCallDelta(string arguments) =>
        new() { Type = ChatCompletionStreamEventType.ToolCallDelta, ToolArguments = arguments };

    public static ChatCompletionStreamEvent ToolCallEnd() =>
        new() { Type = ChatCompletionStreamEventType.ToolCallEnd };

    public static ChatCompletionStreamEvent UsageEvent(TokenUsage usage) =>
        new() { Type = ChatCompletionStreamEventType.Usage, Usage = usage };

    public static ChatCompletionStreamEvent Error(string code, string message) =>
        new() { Type = ChatCompletionStreamEventType.Error, ErrorCode = code, ErrorMessage = message };

    public static ChatCompletionStreamEvent Done() =>
        new() { Type = ChatCompletionStreamEventType.Done };
}
