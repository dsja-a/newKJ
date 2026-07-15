namespace Keji.Providers;

public sealed class ModelProviderConfig
{
    public string ProviderType { get; }
    public string ApiKey { get; }
    public string Endpoint { get; }
    public string DefaultModel { get; }
    public TimeSpan Timeout { get; }
    public int MaxRetries { get; }
    public int MaxTokens { get; }

    private ModelProviderConfig(
        string providerType, string apiKey, string endpoint,
        string defaultModel, TimeSpan timeout, int maxRetries, int maxTokens)
    {
        ProviderType = providerType;
        ApiKey = apiKey;
        Endpoint = endpoint;
        DefaultModel = defaultModel;
        Timeout = timeout;
        MaxRetries = maxRetries;
        MaxTokens = maxTokens;
    }

    public static ModelProviderConfig Create(string providerType, string apiKey, string endpoint, string defaultModel)
    {
        if (string.IsNullOrWhiteSpace(providerType))
            throw new ArgumentException("Provider type is required", nameof(providerType));
        if (string.IsNullOrWhiteSpace(defaultModel))
            throw new ArgumentException("Default model is required", nameof(defaultModel));
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new ArgumentException("Endpoint is required", nameof(endpoint));

        return new ModelProviderConfig(providerType, apiKey, endpoint.TrimEnd('/') + "/", defaultModel,
            TimeSpan.FromSeconds(30), 2, 4096);
    }

    public ModelProviderConfig WithTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(120))
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be between 1ms and 120s");
        return new ModelProviderConfig(ProviderType, ApiKey, Endpoint, DefaultModel, timeout, MaxRetries, MaxTokens);
    }

    public ModelProviderConfig WithMaxRetries(int maxRetries)
    {
        if (maxRetries < 0 || maxRetries > 5)
            throw new ArgumentOutOfRangeException(nameof(maxRetries), "MaxRetries must be between 0 and 5");
        return new ModelProviderConfig(ProviderType, ApiKey, Endpoint, DefaultModel, Timeout, maxRetries, MaxTokens);
    }

    public ModelProviderConfig WithMaxTokens(int maxTokens)
    {
        if (maxTokens < 1 || maxTokens > 131072)
            throw new ArgumentOutOfRangeException(nameof(maxTokens), "MaxTokens must be between 1 and 131072");
        return new ModelProviderConfig(ProviderType, ApiKey, Endpoint, DefaultModel, Timeout, MaxRetries, maxTokens);
    }
}
