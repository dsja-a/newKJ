using System.Text.RegularExpressions;

namespace Keji.Configuration.Secrets;

public static partial class ProviderSecretName
{
    private static readonly HashSet<string> KnownProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "deepseek", "openai",
    };

    public static string GetEnvironmentVariableName(string provider)
    {
        if (provider is null)
            throw new ArgumentNullException(nameof(provider));

        if (provider.Length == 0)
            throw new ArgumentException("Provider name must not be empty.", nameof(provider));

        if (provider.Any(c => c is ' ' or '\n' or '\r' or '\t' or '=' or '/' or '\\' or '.' or ':'))
            throw new ArgumentException($"Provider name '{provider}' contains invalid characters.", nameof(provider));

        if (!ValidProviderPattern().IsMatch(provider))
            throw new ArgumentException($"Provider name '{provider}' does not match allowed pattern.", nameof(provider));

        if (KnownProviders.TryGetValue(provider, out var known))
        {
            return known switch
            {
                "deepseek" => "DEEPSEEK_API_KEY",
                "openai" => "OPENAI_API_KEY",
                _ => ToEnvName(provider),
            };
        }

        return ToEnvName(provider);
    }

    private static string ToEnvName(string provider)
    {
        return provider.Replace("-", "_").ToUpperInvariant() + "_API_KEY";
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]+$", RegexOptions.Compiled)]
    private static partial Regex ValidProviderPattern();
}
