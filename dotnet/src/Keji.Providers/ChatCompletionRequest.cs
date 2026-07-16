using System.Collections.Immutable;
using System.Text.Json;

namespace Keji.Providers;

public sealed class ChatCompletionRequest
{
    public string Model { get; init; } = "";
    public ImmutableArray<ChatMessage> Messages { get; init; }
    public ImmutableArray<ChatTool> Tools { get; init; }
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }

    public bool HasTools => !Tools.IsDefaultOrEmpty;
}

public sealed class ChatMessage
{
    public KejiChatRole Role { get; init; }
    public string? Content { get; init; } = "";
    public string? Name { get; init; }
    public string? ToolCallId { get; init; }
    public string? ReasoningContent { get; init; }
    public ImmutableArray<ChatToolCall> ToolCalls { get; init; }
}

public sealed class ChatTool
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string? InputSchemaJson { get; init; }
}

public sealed class ChatToolCall
{
    public string Id { get; init; } = "";
    public string Type { get; init; } = "function";
    public string FunctionName { get; init; } = "";
    public JsonElement FunctionArguments { get; init; }
}
