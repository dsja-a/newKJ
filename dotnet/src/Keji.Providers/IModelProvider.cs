namespace Keji.Providers;

public interface IModelProvider
{
    string ProviderName { get; }
    Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default);
    IAsyncEnumerable<ChatCompletionStreamEvent> StreamAsync(ChatCompletionRequest request, CancellationToken ct = default);
}
