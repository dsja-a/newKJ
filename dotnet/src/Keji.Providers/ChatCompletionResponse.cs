namespace Keji.Providers;

public sealed class ChatCompletionResponse
{
    public bool Success { get; init; }
    public string? Content { get; init; }
    public string? ReasoningContent { get; init; }
    public IReadOnlyList<ChatToolCall>? ToolCalls { get; init; }
    public TokenUsage? Usage { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string Model { get; init; } = "";
    public string FinishReason { get; init; } = "unknown";
    public bool HasToolCalls => ToolCalls is { Count: > 0 };
    public bool ShouldExecuteTools =>
        HasToolCalls && FinishReason is "tool_calls" or "stop";

    public static ChatCompletionResponse Succeeded(
        string content,
        TokenUsage? usage = null,
        string? reasoningContent = null,
        IReadOnlyList<ChatToolCall>? toolCalls = null,
        string model = "",
        string finishReason = "unknown") =>
        new()
        {
            Success = true,
            Content = content,
            ReasoningContent = reasoningContent,
            ToolCalls = toolCalls,
            Usage = usage,
            Model = model,
            FinishReason = finishReason,
        };

    public static ChatCompletionResponse Failed(string errorCode, string errorMessage) =>
        new() { Success = false, ErrorCode = errorCode, ErrorMessage = errorMessage, FinishReason = "error" };
}
