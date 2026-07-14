using System.Collections.Immutable;
using System.Globalization;

namespace Keji.Auditing.Services;

public static class KejiAuditMetadataSanitizer
{
    private const int MaxKeyCount = 32;
    private const int MaxKeyLength = 64;
    private const int MaxValueLength = 512;
    private const int MaxTotalChars = 4096;

    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "passwd",
        "pwd",
        "token",
        "access_token",
        "refresh_token",
        "api_key",
        "apikey",
        "secret",
        "client_secret",
        "authorization",
        "cookie",
        "set-cookie",
        "private_key",
        "connection_string",
    };

    public static IReadOnlyDictionary<string, string> Sanitize(IReadOnlyDictionary<string, string>? raw)
    {
        if (raw is null || raw.Count == 0)
            return ImmutableDictionary<string, string>.Empty;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var totalChars = 0;

        foreach (var kvp in raw)
        {
            if (result.Count >= MaxKeyCount)
                break;

            var key = kvp.Key;
            if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength)
                continue;

            if (SensitiveKeys.Contains(key))
                continue;

            var value = kvp.Value ?? string.Empty;
            value = SanitizeValue(value);

            if (value.Length > MaxValueLength)
                value = value[..MaxValueLength];

            if (totalChars + key.Length + value.Length > MaxTotalChars)
                break;

            result[key] = value;
            totalChars += key.Length + value.Length;
        }

        return result.ToImmutableDictionary(StringComparer.Ordinal);
    }

    public static bool IsSensitiveKey(string key)
    {
        return !string.IsNullOrEmpty(key) && SensitiveKeys.Contains(key);
    }

    private static string SanitizeValue(string value)
    {
        var span = value.AsSpan();
        var cleaned = new char[span.Length];
        var written = 0;

        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];

            if (char.IsControl(c) && c != '\t' && c != '\n' && c != '\r')
                continue;

            if (char.IsSurrogate(c))
            {
                if (i + 1 < span.Length && char.IsSurrogatePair(span[i], span[i + 1]))
                {
                    cleaned[written++] = c;
                    cleaned[written++] = span[i + 1];
                    i++;
                }
                continue;
            }

            cleaned[written++] = c;
        }

        return new string(cleaned, 0, written);
    }
}
