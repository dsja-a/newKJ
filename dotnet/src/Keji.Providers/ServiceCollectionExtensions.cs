using Keji.Providers;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class KejiProvidersServiceCollectionExtensions
{
    public static IServiceCollection AddKejiProviders(this IServiceCollection services,
        Action<IModelProviderRegistryBuilder> configure)
    {
        services.AddHttpClient("KejiProvider", client =>
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
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
    private readonly Dictionary<string, ModelProviderConfig> _configs = new();

    public ModelProviderRegistryBuilder(IServiceCollection services)
    {
        _services = services;
    }

    public IModelProviderRegistryBuilder AddOpenAI(ModelProviderConfig config)
    {
        _configs["openai"] = config ?? throw new ArgumentNullException(nameof(config));
        return this;
    }

    public IModelProviderRegistryBuilder AddDeepSeek(ModelProviderConfig config)
    {
        _configs["deepseek"] = config ?? throw new ArgumentNullException(nameof(config));
        return this;
    }

    public IModelProviderRegistryBuilder AddOllama(ModelProviderConfig config)
    {
        _configs["ollama"] = config ?? throw new ArgumentNullException(nameof(config));
        return this;
    }

    public void FinalizeRegistration()
    {
        _services.AddSingleton<IModelProviderRegistry>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var providers = new Dictionary<string, IModelProvider>();

            foreach (var (name, config) in _configs)
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
}
