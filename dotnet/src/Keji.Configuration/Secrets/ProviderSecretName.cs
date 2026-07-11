using System.Collections.Frozen;

namespace Keji.Configuration.Secrets;

public static class ProviderSecretName
{
    private static readonly FrozenDictionary<string, string> ProviderMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "deepseek", "DEEPSEEK_API_KEY" },
        { "openai", "OPENAI_API_KEY" },
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static string? GetSecretEnvironmentVariable(string providerName)
    {
        return ProviderMap.TryGetValue(providerName, out var envVar) ? envVar : null;
    }

    public static bool TryGetSecretEnvironmentVariable(string providerName, out string? envVarName)
    {
        return ProviderMap.TryGetValue(providerName, out envVarName);
    }
}
