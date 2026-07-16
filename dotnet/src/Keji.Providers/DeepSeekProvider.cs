using System.Net.Http.Headers;

namespace Keji.Providers;

public sealed class DeepSeekProvider : ProviderBase
{
    private readonly string _apiKey;
    private readonly Uri _endpointUri;
    private readonly string _defaultModel;
    private readonly int _maxTokens;

    public DeepSeekProvider(IHttpClientFactory httpClientFactory, ModelProviderConfig config)
        : base(
            httpClientFactory,
            (config ?? throw new ArgumentNullException(nameof(config))).Timeout,
            config.MaxRetries)
    {
        if (!string.Equals(config.ProviderType, "deepseek", StringComparison.Ordinal))
            throw new ArgumentException("DeepSeekProvider requires a deepseek configuration", nameof(config));
        _apiKey = config.ApiKey;
        _endpointUri = config.EndpointUri;
        _defaultModel = config.DefaultModel;
        _maxTokens = config.MaxTokens;
    }

    public override string ProviderName => "deepseek";
    protected override string BaseUri => _endpointUri.AbsoluteUri;
    protected override string DefaultModel => _defaultModel;
    protected override int DefaultMaxTokens => _maxTokens;
    protected override bool BackfillAssistantReasoningContent => true;
    protected override AuthenticationHeaderValue? AuthHeader =>
        string.IsNullOrEmpty(_apiKey) ? null : new("Bearer", _apiKey);
}
