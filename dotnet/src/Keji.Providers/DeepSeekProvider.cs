using System.Net.Http.Headers;

namespace Keji.Providers;

public sealed class DeepSeekProvider : ProviderBase
{
    private readonly IKejiProviderSecretResolver _secretResolver;
    private readonly KejiProviderSecretReference _secretReference;
    private readonly Uri _endpointUri;
    private readonly string _defaultModel;
    private readonly int _maxTokens;

    public DeepSeekProvider(
        IHttpClientFactory httpClientFactory,
        ModelProviderConfig config,
        IKejiProviderSecretResolver? secretResolver = null)
        : base(
            httpClientFactory,
            (config ?? throw new ArgumentNullException(nameof(config))).Timeout,
            config.MaxRetries)
    {
        if (!string.Equals(config.ProviderType, "deepseek", StringComparison.Ordinal))
            throw new ArgumentException("DeepSeekProvider requires a deepseek configuration", nameof(config));
        _secretReference = config.SecretReference
            ?? throw new ArgumentException("DeepSeekProvider requires a secret reference", nameof(config));
        _secretResolver = secretResolver ?? new EnvironmentKejiProviderSecretResolver();
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
        new("Bearer", ResolveSecret());

    private string ResolveSecret()
    {
        string? secret;
        try
        {
            secret = _secretResolver.Resolve(_secretReference);
        }
        catch (InvalidOperationException)
        {
            throw new KejiProviderSecretResolutionException();
        }
        if (string.IsNullOrWhiteSpace(secret))
            throw new KejiProviderSecretResolutionException();
        return secret;
    }
}
