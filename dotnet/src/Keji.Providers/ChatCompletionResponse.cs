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

    public static ChatCompletionResponse Succeeded(string content, TokenUsage? usage = null, string? reasoningContent = null, IReadOnlyList<ChatToolCall>? toolCalls = null, string model = "") =>
        new() { Success = true, Content = content, ReasoningContent = reasoningContent, ToolCalls = toolCalls, Usage = usage, Model = model };

    public static ChatCompletionResponse Failed(string errorCode, string errorMessage) =>
        new() { Success = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
}
