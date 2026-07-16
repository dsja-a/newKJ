namespace Keji.Agent;

public sealed class AgentRunRequest
{
    public string ConversationId { get; init; } = string.Empty;
    public string ProviderName { get; init; } = string.Empty;
    public string Model { get; init; } = string.Empty;
    public string UserMessage { get; init; } = string.Empty;
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }
}
