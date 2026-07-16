using System.Collections.Frozen;

namespace Keji.Providers;

public interface IModelProviderRegistry
{
    IModelProvider? GetProvider(string? providerName);
    IReadOnlyCollection<string> RegisteredProviders { get; }
}

public sealed class ModelProviderRegistry : IModelProviderRegistry
{
    private readonly FrozenDictionary<string, IModelProvider> _providers;
    private readonly IReadOnlyCollection<string> _registeredProviders;

    public ModelProviderRegistry(IEnumerable<KeyValuePair<string, IModelProvider>> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        var snapshot = new Dictionary<string, IModelProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, provider) in providers)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Provider registry keys must not be empty", nameof(providers));
            ArgumentNullException.ThrowIfNull(provider);

            var normalizedName = name.Trim().ToLowerInvariant();
            if (normalizedName.Length > 64 || normalizedName.Any(static ch =>
                    !(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_' or '.')))
                throw new ArgumentException("Provider registry keys contain invalid characters", nameof(providers));
            if (!string.Equals(normalizedName, provider.ProviderName, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Provider registry key must match ProviderName", nameof(providers));
            if (!snapshot.TryAdd(normalizedName, provider))
                throw new ArgumentException("Provider registry contains a duplicate name", nameof(providers));
        }

        _providers = snapshot.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _registeredProviders = Array.AsReadOnly(
            _providers.Keys.OrderBy(static name => name, StringComparer.Ordinal).ToArray());
    }

    public IModelProvider? GetProvider(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return null;

        return _providers.TryGetValue(providerName.Trim(), out var provider) ? provider : null;
    }

    public IReadOnlyCollection<string> RegisteredProviders => _registeredProviders;

    public IReadOnlyDictionary<string, IModelProvider> Providers => _providers;
}
