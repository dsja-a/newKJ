using System.Net.Http.Headers;

namespace Keji.Providers;

public sealed class OllamaProvider : ProviderBase
{
    private readonly Uri _endpointUri;
    private readonly string _defaultModel;
    private readonly int _maxTokens;

    public OllamaProvider(IHttpClientFactory httpClientFactory, ModelProviderConfig config)
        : base(
            httpClientFactory,
            (config ?? throw new ArgumentNullException(nameof(config))).Timeout,
            config.MaxRetries)
    {
        if (!string.Equals(config.ProviderType, "ollama", StringComparison.Ordinal))
            throw new ArgumentException("OllamaProvider requires an ollama configuration", nameof(config));
        _endpointUri = config.EndpointUri;
        _defaultModel = config.DefaultModel;
        _maxTokens = config.MaxTokens;
    }

    public override string ProviderName => "ollama";
    protected override string BaseUri => NormalizeOpenAiCompatibleEndpoint(_endpointUri);
    protected override string DefaultModel => _defaultModel;
    protected override int DefaultMaxTokens => _maxTokens;
    protected override AuthenticationHeaderValue? AuthHeader => null;

    private static string NormalizeOpenAiCompatibleEndpoint(Uri endpoint)
    {
        if (endpoint.AbsolutePath is "/" or "")
            return new Uri(endpoint, "v1/").AbsoluteUri;

        return endpoint.AbsoluteUri;
    }
}
