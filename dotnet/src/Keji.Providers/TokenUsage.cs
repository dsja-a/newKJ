namespace Keji.Providers;

public sealed class TokenUsage
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens => PromptTokens + CompletionTokens;
}
