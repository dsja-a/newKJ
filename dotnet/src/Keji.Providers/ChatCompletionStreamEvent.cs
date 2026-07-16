namespace Keji.Providers;

public enum ChatCompletionStreamEventType
{
    ReasoningToken,
    Token,
    ToolCallBegin,
    ToolCallDelta,
    ToolCallEnd,
    ChoiceFinished,
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
    public string? FinishReason { get; init; }
    public bool HasToolCalls { get; init; }
    public int ChoiceIndex { get; init; }
    public int ToolCallIndex { get; init; }
    public TokenUsage? Usage { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool ShouldExecuteTools =>
        Type == ChatCompletionStreamEventType.ChoiceFinished &&
        HasToolCalls && FinishReason is "tool_calls" or "stop";

    public static ChatCompletionStreamEvent ReasoningToken(string content, int choiceIndex = 0) =>
        new() { Type = ChatCompletionStreamEventType.ReasoningToken, Content = content, ChoiceIndex = choiceIndex };

    public static ChatCompletionStreamEvent Token(string content, int choiceIndex = 0) =>
        new() { Type = ChatCompletionStreamEventType.Token, Content = content, ChoiceIndex = choiceIndex };

    public static ChatCompletionStreamEvent ToolCallBegin(
        string id,
        string name,
        int toolCallIndex = 0,
        int choiceIndex = 0) =>
        new()
        {
            Type = ChatCompletionStreamEventType.ToolCallBegin,
            ToolCallId = id,
            ToolName = name,
            ToolCallIndex = toolCallIndex,
            ChoiceIndex = choiceIndex,
        };

    public static ChatCompletionStreamEvent ToolCallDelta(
        string arguments,
        int toolCallIndex = 0,
        int choiceIndex = 0,
        string? toolCallId = null) =>
        new()
        {
            Type = ChatCompletionStreamEventType.ToolCallDelta,
            ToolArguments = arguments,
            ToolCallIndex = toolCallIndex,
            ChoiceIndex = choiceIndex,
            ToolCallId = toolCallId,
        };

    public static ChatCompletionStreamEvent ToolCallEnd(
        string? toolCallId = null,
        int toolCallIndex = 0,
        int choiceIndex = 0) =>
        new()
        {
            Type = ChatCompletionStreamEventType.ToolCallEnd,
            ToolCallId = toolCallId,
            ToolCallIndex = toolCallIndex,
            ChoiceIndex = choiceIndex,
        };

    public static ChatCompletionStreamEvent ChoiceFinished(
        string finishReason,
        bool hasToolCalls,
        int choiceIndex = 0) =>
        new()
        {
            Type = ChatCompletionStreamEventType.ChoiceFinished,
            FinishReason = finishReason,
            HasToolCalls = hasToolCalls,
            ChoiceIndex = choiceIndex,
        };

    public static ChatCompletionStreamEvent UsageEvent(TokenUsage usage) =>
        new() { Type = ChatCompletionStreamEventType.Usage, Usage = usage };

    public static ChatCompletionStreamEvent Error(string code, string message) =>
        new() { Type = ChatCompletionStreamEventType.Error, ErrorCode = code, ErrorMessage = message };

    public static ChatCompletionStreamEvent Done() =>
        new() { Type = ChatCompletionStreamEventType.Done };
}
