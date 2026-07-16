namespace Keji.Providers;

public sealed class TokenUsage
{
    public long PromptTokens { get; init; }
    public long CompletionTokens { get; init; }
    public long TotalTokens => checked(PromptTokens + CompletionTokens);
}
