namespace Keji.Providers;

public interface IKejiProviderSecretResolver
{
    string? Resolve(KejiProviderSecretReference secretReference);
}

internal sealed class KejiProviderSecretResolutionException : Exception
{
    public KejiProviderSecretResolutionException() : base("Provider secret is unavailable") { }
}

public sealed class KejiProviderSecretReference
{
    private const int MaxEnvironmentVariableNameLength = 128;

    public string EnvironmentVariableName { get; }

    public KejiProviderSecretReference(string environmentVariableName)
    {
        if (string.IsNullOrWhiteSpace(environmentVariableName))
            throw new ArgumentException("Environment variable name is required", nameof(environmentVariableName));
        if (environmentVariableName.Length > MaxEnvironmentVariableNameLength)
            throw new ArgumentException("Environment variable name exceeds the maximum length", nameof(environmentVariableName));
        if (!EnvironmentVariableNamePattern.IsMatch(environmentVariableName))
            throw new ArgumentException("Invalid environment variable name", nameof(environmentVariableName));
        EnvironmentVariableName = environmentVariableName;
    }

    private static readonly System.Text.RegularExpressions.Regex EnvironmentVariableNamePattern =
        new("^[A-Z][A-Z0-9_]{0,127}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public override string ToString() => $"env:{EnvironmentVariableName}";
}

public sealed class EnvironmentKejiProviderSecretResolver : IKejiProviderSecretResolver
{
    private const int MaxSecretValueLength = 16 * 1024;

    public string? Resolve(KejiProviderSecretReference secretReference)
    {
        ArgumentNullException.ThrowIfNull(secretReference);

        var value = Environment.GetEnvironmentVariable(secretReference.EnvironmentVariableName);

        if (value is null)
            return null;

        if (value.Length > MaxSecretValueLength)
            throw new InvalidOperationException("Resolved secret exceeds the maximum length");

        if (value.Any(char.IsControl))
            throw new InvalidOperationException("Resolved secret contains control characters");

        return value;
    }
}
