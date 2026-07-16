using Keji.Providers;

namespace Microsoft.Extensions.DependencyInjection;

public static class KejiProvidersServiceCollectionExtensions
{
    public static IServiceCollection AddKejiProviders(this IServiceCollection services,
        Action<IModelProviderRegistryBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddHttpClient("KejiProvider", client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.HttpClientHandler
        {
            AllowAutoRedirect = false,
            CheckCertificateRevocationList = true
        });

        var builder = new ModelProviderRegistryBuilder(services);
        configure(builder);
        builder.FinalizeRegistration();

        return services;
    }
}

public interface IModelProviderRegistryBuilder
{
    IModelProviderRegistryBuilder AddOpenAI(ModelProviderConfig config);
    IModelProviderRegistryBuilder AddDeepSeek(ModelProviderConfig config);
    IModelProviderRegistryBuilder AddOllama(ModelProviderConfig config);
}

internal sealed class ModelProviderRegistryBuilder : IModelProviderRegistryBuilder
{
    private readonly IServiceCollection _services;
    private readonly Dictionary<string, ModelProviderConfig> _configs = new(StringComparer.Ordinal);
    private bool _finalized;

    public ModelProviderRegistryBuilder(IServiceCollection services)
    {
        _services = services;
    }

    public IModelProviderRegistryBuilder AddOpenAI(ModelProviderConfig config)
    {
        return Add("openai", config);
    }

    public IModelProviderRegistryBuilder AddDeepSeek(ModelProviderConfig config)
    {
        return Add("deepseek", config);
    }

    public IModelProviderRegistryBuilder AddOllama(ModelProviderConfig config)
    {
        return Add("ollama", config);
    }

    public void FinalizeRegistration()
    {
        if (_finalized)
            throw new InvalidOperationException("Provider registration has already been finalized");

        _finalized = true;
        var frozenConfigs = _configs
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToArray();

        _services.AddSingleton<IModelProviderRegistry>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var providers = new Dictionary<string, IModelProvider>();

            foreach (var (name, config) in frozenConfigs)
            {
                providers[name] = name switch
                {
                    "openai" => new OpenAIProvider(factory, config),
                    "deepseek" => new DeepSeekProvider(factory, config),
                    "ollama" => new OllamaProvider(factory, config),
                    _ => throw new InvalidOperationException($"Unknown provider type: {name}")
                };
            }

            return new ModelProviderRegistry(providers);
        });
    }

    private IModelProviderRegistryBuilder Add(string providerName, ModelProviderConfig config)
    {
        if (_finalized)
            throw new InvalidOperationException("Provider registration is frozen");
        ArgumentNullException.ThrowIfNull(config);
        if (!string.Equals(config.ProviderType, providerName, StringComparison.Ordinal))
            throw new ArgumentException("Provider configuration type does not match the registration", nameof(config));
        if (!_configs.TryAdd(providerName, config))
            throw new InvalidOperationException($"Provider '{providerName}' is already registered");

        return this;
    }
}
