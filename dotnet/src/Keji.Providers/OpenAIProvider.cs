using System.Net.Http.Headers;

namespace Keji.Providers;

public sealed class OpenAIProvider : ProviderBase
{
    private readonly string _apiKey;

    public OpenAIProvider(IHttpClientFactory httpClientFactory, ModelProviderConfig config)
        : base(httpClientFactory, config.Timeout, config.MaxRetries)
    {
        _apiKey = config.ApiKey;
    }

    public override string ProviderName => "openai";
    protected override string BaseUri => "https://api.openai.com/v1/";
    protected override AuthenticationHeaderValue? AuthHeader =>
        string.IsNullOrEmpty(_apiKey) ? null : new("Bearer", _apiKey);
}
