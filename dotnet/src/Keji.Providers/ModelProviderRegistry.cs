using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Keji.Providers;

public interface IModelProviderRegistry
{
    IModelProvider? GetProvider(string? providerName);
    IReadOnlyCollection<string> RegisteredProviders { get; }
}

public sealed partial class ModelProviderRegistry : IModelProviderRegistry
{
    private readonly FrozenDictionary<string, IModelProvider> _providers;
    private readonly IReadOnlyCollection<string> _registeredProviders;

    public ModelProviderRegistry(IEnumerable<KeyValuePair<string, IModelProvider>> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var snapshot = new Dictionary<string, IModelProvider>(StringComparer.Ordinal);
        foreach (var (name, provider) in providers)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Provider registry keys must not be empty", nameof(providers));
            ArgumentNullException.ThrowIfNull(provider);

            if (!ProviderNamePattern().IsMatch(name))
                throw new ArgumentException("Provider registry keys contain invalid characters", nameof(providers));
            if (!string.Equals(name, provider.ProviderName, StringComparison.Ordinal))
                throw new ArgumentException("Provider registry key must match ProviderName exactly", nameof(providers));
            if (!snapshot.TryAdd(name, provider))
                throw new ArgumentException("Provider registry contains a duplicate name", nameof(providers));
        }

        _providers = snapshot.ToFrozenDictionary(StringComparer.Ordinal);
        _registeredProviders = Array.AsReadOnly(
            _providers.Keys.OrderBy(static name => name).ToArray());
    }

    public IModelProvider? GetProvider(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return null;

        return _providers.TryGetValue(providerName, out var provider) ? provider : null;
    }

    public IReadOnlyCollection<string> RegisteredProviders => _registeredProviders;

    public IReadOnlyDictionary<string, IModelProvider> Providers => _providers;

    [GeneratedRegex("^[a-z][a-z0-9_]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderNamePattern();
}
