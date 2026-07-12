using Keji.Configuration.Models;
using Keji.Security.Exceptions;

namespace Keji.Security.Options;

public class KejiSecurityOptions
{
    public bool Enabled { get; set; } = true;
    public KejiAuthMode AuthMode { get; set; } = KejiAuthMode.Both;
    public string? ApiKey { get; set; }
    public string? JwtSecret { get; set; }
    public int JwtExpireHours { get; set; } = 72;
    public int JwtClockSkewSeconds { get; set; } = 0;
    public bool AllowLocalhostWithoutAuth { get; set; } = false;
    public bool AllowApiKeyInQuery { get; set; } = false;
    public IReadOnlyList<string> PublicPaths { get; set; } = Array.Empty<string>();
    public BootstrapAdminOptions BootstrapAdmin { get; set; } = new();

    public static KejiSecurityOptions FromConfiguration(KejiConfigurationDocument document)
    {
        var opts = new KejiSecurityOptions();

        opts.Enabled = document.GetBoolean("security.enabled", true);
        opts.AllowLocalhostWithoutAuth = document.GetBoolean("security.allow_localhost_without_auth", false);
        opts.AllowApiKeyInQuery = document.GetBoolean("security.allow_api_key_in_query", false);

        opts.JwtExpireHours = document.GetInt32("security.jwt_expire_hours", 72);
        if (opts.JwtExpireHours < 1 || opts.JwtExpireHours > 720)
            throw new KejiSecurityConfigurationException("jwt_expire_hours must be between 1 and 720.");

        opts.JwtClockSkewSeconds = document.GetInt32("security.jwt_clock_skew_seconds", 0);
        if (opts.JwtClockSkewSeconds < 0 || opts.JwtClockSkewSeconds > 300)
            throw new KejiSecurityConfigurationException("jwt_clock_skew_seconds must be between 0 and 300.");

        var authModeStr = document.GetOptionalString("security.auth_mode") ?? "both";
        opts.AuthMode = authModeStr.ToLowerInvariant() switch
        {
            "both" => KejiAuthMode.Both,
            "user_only" => KejiAuthMode.UserOnly,
            "api_key_only" => KejiAuthMode.ApiKeyOnly,
            _ => throw new KejiSecurityConfigurationException($"Invalid auth_mode '{authModeStr}'. Must be 'both', 'user_only', or 'api_key_only'.")
        };

        opts.ApiKey = document.GetOptionalString("security.api_key");
        opts.JwtSecret = document.GetOptionalString("security.jwt_secret");

        if (opts.Enabled)
        {
            ValidateSecrets(opts);
        }

        var rawPaths = document.GetStringList("security.public_paths");
        var deduped = new List<string>(rawPaths.Count);
        foreach (var p in rawPaths)
        {
            if (string.IsNullOrEmpty(p)) continue;
            if (!p.StartsWith('/'))
                throw new KejiSecurityConfigurationException($"Public path '{p}' must start with '/'.");
            if (p.Contains('?') || p.Contains('#') || p.Contains('\0'))
                throw new KejiSecurityConfigurationException($"Public path '{p}' contains invalid character.");
            if (!deduped.Contains(p, StringComparer.Ordinal))
                deduped.Add(p);
        }
        opts.PublicPaths = deduped.AsReadOnly();

        opts.BootstrapAdmin = new BootstrapAdminOptions
        {
            Username = document.GetOptionalString("security.bootstrap_admin.username")?.Trim() ?? "admin",
            Password = document.GetOptionalString("security.bootstrap_admin.password"),
            DisplayName = document.GetOptionalString("security.bootstrap_admin.display_name") ?? "系统管理员"
        };

        return opts;
    }

    private static void ValidateSecrets(KejiSecurityOptions opts)
    {
        if (opts.AuthMode == KejiAuthMode.UserOnly || opts.AuthMode == KejiAuthMode.Both)
        {
            if (string.IsNullOrEmpty(opts.JwtSecret))
                throw new KejiSecurityConfigurationException("JWT Secret is required for UserOnly or Both auth mode.");
            var secretBytes = System.Text.Encoding.UTF8.GetByteCount(opts.JwtSecret);
            if (secretBytes < 32)
                throw new KejiSecurityConfigurationException($"JWT Secret must be at least 32 bytes (UTF-8), got {secretBytes}.");
        }

        if (opts.AuthMode == KejiAuthMode.ApiKeyOnly || opts.AuthMode == KejiAuthMode.Both)
        {
            if (string.IsNullOrEmpty(opts.ApiKey))
                throw new KejiSecurityConfigurationException("API Key is required for ApiKeyOnly or Both auth mode.");
            var keyBytes = System.Text.Encoding.UTF8.GetByteCount(opts.ApiKey);
            if (keyBytes < 32)
                throw new KejiSecurityConfigurationException($"API Key must be at least 32 bytes (UTF-8), got {keyBytes}.");
        }
    }
}
