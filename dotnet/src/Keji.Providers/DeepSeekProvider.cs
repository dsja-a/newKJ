using System.Net.Http.Headers;

namespace Keji.Providers;

public sealed class DeepSeekProvider : ProviderBase
{
    private readonly string _apiKey;

    public DeepSeekProvider(IHttpClientFactory httpClientFactory, ModelProviderConfig config)
        : base(httpClientFactory, config.Timeout, config.MaxRetries)
    {
        _apiKey = config.ApiKey;
    }

    public override string ProviderName => "deepseek";
    protected override string BaseUri => "https://api.deepseek.com/v1/";
    protected override AuthenticationHeaderValue? AuthHeader =>
        string.IsNullOrEmpty(_apiKey) ? null : new("Bearer", _apiKey);
}
