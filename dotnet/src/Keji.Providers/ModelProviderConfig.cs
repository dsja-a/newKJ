using System.Net;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Keji.Configuration.Models;

namespace Keji.Providers;

public sealed partial class ModelProviderConfig
{
    private const int MaxEndpointLength = 2048;
    private const int MaxModelLength = 256;
    private const int MaxSecretLength = 16 * 1024;

    public string ProviderType { get; }
    [JsonIgnore]
    public string ApiKey { get; }
    public Uri EndpointUri { get; }
    public string Endpoint => EndpointUri.AbsoluteUri;
    public string DefaultModel { get; }
    public TimeSpan Timeout { get; }
    public int MaxRetries { get; }
    public int MaxTokens { get; }

    private ModelProviderConfig(
        string providerType,
        string apiKey,
        Uri endpointUri,
        string defaultModel,
        TimeSpan timeout,
        int maxRetries,
        int maxTokens)
    {
        ProviderType = providerType;
        ApiKey = apiKey;
        EndpointUri = endpointUri;
        DefaultModel = defaultModel;
        Timeout = timeout;
        MaxRetries = maxRetries;
        MaxTokens = maxTokens;
    }

    public static ModelProviderConfig Create(
        string providerType,
        string? apiKey,
        string endpoint,
        string defaultModel)
    {
        var normalizedProvider = ValidateProviderType(providerType);
        var validatedSecret = ValidateSecret(apiKey ?? string.Empty, normalizedProvider);
        var validatedEndpoint = ValidateEndpoint(endpoint);
        var validatedModel = ValidateModel(defaultModel);

        return new ModelProviderConfig(
            normalizedProvider,
            validatedSecret,
            validatedEndpoint,
            validatedModel,
            TimeSpan.FromSeconds(30),
            maxRetries: 2,
            maxTokens: 4096);
    }

    public static ModelProviderConfig FromConfiguration(
        KejiConfigurationDocument document,
        string? providerType = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var selectedProvider = providerType ?? document.GetRequiredString("models.default");
        var normalizedProvider = ValidateProviderType(selectedProvider);
        var prefix = $"models.{normalizedProvider}";

        var config = Create(
            normalizedProvider,
            document.GetOptionalString($"{prefix}.api_key"),
            document.GetRequiredString($"{prefix}.base_url"),
            document.GetRequiredString($"{prefix}.model"));

        var timeoutSeconds = document.GetInt32($"{prefix}.timeout", 30);
        var maxRetries = document.GetInt32($"{prefix}.max_retries", 2);
        var maxTokens = document.GetInt32($"{prefix}.max_tokens", 4096);

        return config
            .WithTimeout(TimeSpan.FromSeconds(timeoutSeconds))
            .WithMaxRetries(maxRetries)
            .WithMaxTokens(maxTokens);
    }

    public ModelProviderConfig WithTimeout(TimeSpan timeout)
    {
        if (timeout < TimeSpan.FromMilliseconds(1) || timeout > TimeSpan.FromSeconds(120))
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be between 1ms and 120s");

        return Copy(timeout: timeout);
    }

    public ModelProviderConfig WithMaxRetries(int maxRetries)
    {
        if (maxRetries is < 0 or > 5)
            throw new ArgumentOutOfRangeException(nameof(maxRetries), "MaxRetries must be between 0 and 5");

        return Copy(maxRetries: maxRetries);
    }

    public ModelProviderConfig WithMaxTokens(int maxTokens)
    {
        if (maxTokens is < 1 or > 131072)
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "MaxTokens must be between 1 and 131072");

        return Copy(maxTokens: maxTokens);
    }

    public override string ToString() =>
        $"ModelProviderConfig {{ ProviderType = {ProviderType}, Endpoint = {Endpoint}, " +
        $"DefaultModel = {DefaultModel}, ApiKeyConfigured = {!string.IsNullOrEmpty(ApiKey)}, " +
        $"Timeout = {Timeout}, MaxRetries = {MaxRetries}, MaxTokens = {MaxTokens} }}";

    private ModelProviderConfig Copy(
        TimeSpan? timeout = null,
        int? maxRetries = null,
        int? maxTokens = null) =>
        new(
            ProviderType,
            ApiKey,
            EndpointUri,
            DefaultModel,
            timeout ?? Timeout,
            maxRetries ?? MaxRetries,
            maxTokens ?? MaxTokens);

    private static string ValidateProviderType(string providerType)
    {
        if (string.IsNullOrWhiteSpace(providerType))
            throw new ArgumentException("Provider type is required", nameof(providerType));

        var normalized = providerType.Trim().ToLowerInvariant();
        if (!ProviderTypePattern().IsMatch(normalized))
            throw new ArgumentException("Provider type contains invalid characters", nameof(providerType));

        return normalized;
    }

    private static string ValidateSecret(string apiKey, string providerType)
    {
        if (apiKey.Length > MaxSecretLength)
            throw new ArgumentException("Provider API key exceeds the maximum length", nameof(apiKey));

        if (apiKey.Any(char.IsControl))
            throw new ArgumentException("Provider API key contains control characters", nameof(apiKey));

        if (UnresolvedEnvironmentReferencePattern().IsMatch(apiKey))
            throw new ArgumentException("Provider API key must be resolved by the configuration foundation", nameof(apiKey));

        if (providerType is "openai" or "deepseek" && string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Provider API key is required", nameof(apiKey));

        if (providerType is "openai" or "deepseek" && !BearerTokenPattern().IsMatch(apiKey))
            throw new ArgumentException("Provider API key is not a valid bearer token", nameof(apiKey));

        return apiKey;
    }

    private static Uri ValidateEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint is required", nameof(endpoint));

        if (endpoint.Length > MaxEndpointLength)
            throw new ArgumentException("Endpoint exceeds the maximum length", nameof(endpoint));

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("Endpoint must be a valid absolute URI", nameof(endpoint));

        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Endpoint must not contain credentials, a query, or a fragment", nameof(endpoint));

        var isHttps = string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var isLoopbackHttp = string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) && IsLoopback(uri);
        if (!isHttps && !isLoopbackHttp)
            throw new ArgumentException("Endpoint must use HTTPS unless targeting loopback", nameof(endpoint));

        if (uri.AbsolutePath.Contains('\\', StringComparison.Ordinal))
            throw new ArgumentException("Endpoint path contains an invalid separator", nameof(endpoint));

        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/') + "/",
            Query = string.Empty,
            Fragment = string.Empty,
        };

        return builder.Uri;
    }

    private static string ValidateModel(string defaultModel)
    {
        if (string.IsNullOrWhiteSpace(defaultModel))
            throw new ArgumentException("Default model is required", nameof(defaultModel));

        if (defaultModel.Length > MaxModelLength || defaultModel.Any(char.IsControl))
            throw new ArgumentException("Default model is invalid", nameof(defaultModel));

        return defaultModel.Trim();
    }

    private static bool IsLoopback(Uri uri)
    {
        if (uri.IsLoopback)
            return true;

        return IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderTypePattern();

    [GeneratedRegex("^\\$\\{[A-Za-z_][A-Za-z0-9_]*\\}$", RegexOptions.CultureInvariant)]
    private static partial Regex UnresolvedEnvironmentReferencePattern();

    [GeneratedRegex("^[A-Za-z0-9._~+/\\-]+={0,}$", RegexOptions.CultureInvariant)]
    private static partial Regex BearerTokenPattern();
}
