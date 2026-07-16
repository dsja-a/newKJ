using System.Collections.Immutable;

namespace Keji.Providers;

public sealed class ChatCompletionResponse
{
    public bool Success { get; init; }
    public string? Content { get; init; }
    public string? ReasoningContent { get; init; }
    public ImmutableArray<ChatToolCall> ToolCalls { get; init; }
    public TokenUsage? Usage { get; init; }
    public KejiProviderErrorCode ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string Model { get; init; } = "";
    public KejiFinishReason FinishReason { get; init; }
    public bool HasToolCalls => !ToolCalls.IsDefaultOrEmpty;
    public bool ShouldExecuteTools =>
        HasToolCalls && FinishReason is KejiFinishReason.ToolCalls or KejiFinishReason.Stop;

    public static ChatCompletionResponse Succeeded(
        string content,
        TokenUsage? usage = null,
        string? reasoningContent = null,
        ImmutableArray<ChatToolCall> toolCalls = default,
        string model = "",
        KejiFinishReason finishReason = default) =>
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

    public static ChatCompletionResponse Failed(KejiProviderErrorCode errorCode, string errorMessage) =>
        new() { Success = false, ErrorCode = errorCode, ErrorMessage = errorMessage, FinishReason = KejiFinishReason.Error };
}
