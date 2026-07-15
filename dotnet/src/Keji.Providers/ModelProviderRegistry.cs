namespace Keji.Providers;

public interface IModelProviderRegistry
{
    IModelProvider? GetProvider(string providerName);
    IReadOnlyCollection<string> RegisteredProviders { get; }
}

public sealed class ModelProviderRegistry : IModelProviderRegistry
{
    private readonly Dictionary<string, IModelProvider> _providers;

    public ModelProviderRegistry(Dictionary<string, IModelProvider> providers)
    {
        _providers = providers;
    }

    public IModelProvider? GetProvider(string providerName) =>
        _providers.TryGetValue(providerName, out var provider) ? provider : null;

    public IReadOnlyCollection<string> RegisteredProviders => _providers.Keys;
}
