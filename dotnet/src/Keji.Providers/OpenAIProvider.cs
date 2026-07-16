using System.Net.Http.Headers;

namespace Keji.Providers;

public sealed class OpenAIProvider : ProviderBase
{
    private readonly string _apiKey;
    private readonly string _endpoint;
    private readonly string _defaultModel;
    private readonly int _maxTokens;

    public OpenAIProvider(IHttpClientFactory httpClientFactory, ModelProviderConfig config)
        : base(
            httpClientFactory,
            (config ?? throw new ArgumentNullException(nameof(config))).Timeout,
            config.MaxRetries)
    {
        if (!string.Equals(config.ProviderType, "openai", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("OpenAIProvider requires an openai configuration", nameof(config));
        _apiKey = config.ApiKey;
        _endpoint = config.Endpoint;
        _defaultModel = config.DefaultModel;
        _maxTokens = config.MaxTokens;
    }

    public override string ProviderName => "openai";
    protected override string BaseUri => _endpoint;
    protected override string DefaultModel => _defaultModel;
    protected override int DefaultMaxTokens => _maxTokens;
    protected override AuthenticationHeaderValue? AuthHeader =>
        string.IsNullOrEmpty(_apiKey) ? null : new("Bearer", _apiKey);
}
