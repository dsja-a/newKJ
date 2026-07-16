using System.Text.Json;

namespace Keji.Providers;

public sealed class ChatCompletionStreamEvent
{
    public KejiProviderStreamEventKind Type { get; init; }
    public string? Content { get; init; }
    public string? ToolCallId { get; init; }
    public string? ToolName { get; init; }
    public string? ToolArguments { get; init; }
    public KejiFinishReason FinishReason { get; init; }
    public bool HasToolCalls { get; init; }
    public int ChoiceIndex { get; init; }
    public int ToolCallIndex { get; init; }
    public TokenUsage? Usage { get; init; }
    public KejiProviderErrorCode ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool ShouldExecuteTools =>
        Type == KejiProviderStreamEventKind.ChoiceFinished &&
        HasToolCalls && FinishReason is KejiFinishReason.ToolCalls or KejiFinishReason.Stop;

    public static ChatCompletionStreamEvent ReasoningToken(string content, int choiceIndex = 0) =>
        new() { Type = KejiProviderStreamEventKind.ReasoningToken, Content = content, ChoiceIndex = choiceIndex };

    public static ChatCompletionStreamEvent Token(string content, int choiceIndex = 0) =>
        new() { Type = KejiProviderStreamEventKind.Token, Content = content, ChoiceIndex = choiceIndex };

    public static ChatCompletionStreamEvent ToolCallBegin(
        string id,
        string name,
        int toolCallIndex = 0,
        int choiceIndex = 0) =>
        new()
        {
            Type = KejiProviderStreamEventKind.ToolCallBegin,
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
            Type = KejiProviderStreamEventKind.ToolCallDelta,
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
            Type = KejiProviderStreamEventKind.ToolCallEnd,
            ToolCallId = toolCallId,
            ToolCallIndex = toolCallIndex,
            ChoiceIndex = choiceIndex,
        };

    public static ChatCompletionStreamEvent ChoiceFinished(
        KejiFinishReason finishReason,
        bool hasToolCalls,
        int choiceIndex = 0) =>
        new()
        {
            Type = KejiProviderStreamEventKind.ChoiceFinished,
            FinishReason = finishReason,
            HasToolCalls = hasToolCalls,
            ChoiceIndex = choiceIndex,
        };

    public static ChatCompletionStreamEvent UsageEvent(TokenUsage usage) =>
        new() { Type = KejiProviderStreamEventKind.Usage, Usage = usage };

    public static ChatCompletionStreamEvent Error(KejiProviderErrorCode code, string message) =>
        new() { Type = KejiProviderStreamEventKind.Error, ErrorCode = code, ErrorMessage = message };

    public static ChatCompletionStreamEvent Done() =>
        new() { Type = KejiProviderStreamEventKind.Done };
}
