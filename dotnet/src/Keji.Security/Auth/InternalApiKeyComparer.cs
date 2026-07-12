using System.Security.Cryptography;

namespace Keji.Security.Auth;

internal static class InternalApiKeyComparer
{
    private const int MaxApiKeyLength = 4096;

    public static bool IsValid(string? providedKey, string? configuredKey)
    {
        if (string.IsNullOrEmpty(providedKey) || string.IsNullOrEmpty(configuredKey))
            return false;

        if (providedKey.Length > MaxApiKeyLength)
            return false;

        var providedBytes = System.Text.Encoding.UTF8.GetBytes(providedKey);
        var configuredBytes = System.Text.Encoding.UTF8.GetBytes(configuredKey);

        var providedHash = SHA256.HashData(providedBytes);
        var configuredHash = SHA256.HashData(configuredBytes);

        return CryptographicOperations.FixedTimeEquals(providedHash, configuredHash);
    }
}
