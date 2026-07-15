using System.Net.Http.Headers;

namespace Keji.Providers;

public sealed class OllamaProvider : ProviderBase
{
    private readonly string _endpoint;

    public OllamaProvider(IHttpClientFactory httpClientFactory, ModelProviderConfig config)
        : base(httpClientFactory, config.Timeout, config.MaxRetries)
    {
        _endpoint = config.Endpoint;
    }

    public override string ProviderName => "ollama";
    protected override string BaseUri => _endpoint;
    protected override AuthenticationHeaderValue? AuthHeader => null;
}
