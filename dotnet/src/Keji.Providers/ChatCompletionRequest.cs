namespace Keji.Providers;

public sealed class ChatCompletionRequest
{
    public string Model { get; init; } = "";
    public IReadOnlyList<ChatMessage> Messages { get; init; } = Array.Empty<ChatMessage>();
    public IReadOnlyList<ChatTool>? Tools { get; init; }
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }

    public bool HasTools => Tools is { Count: > 0 };
}

public sealed class ChatMessage
{
    public string Role { get; init; } = "";
    public string? Content { get; init; } = "";
    public string? Name { get; init; }
    public string? ToolCallId { get; init; }
    public string? ReasoningContent { get; init; }
    public IReadOnlyList<ChatToolCall>? ToolCalls { get; init; }
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
    public string FunctionArguments { get; init; } = "";
}
