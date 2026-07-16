namespace Keji.Providers;

public interface IKejiProviderSecretResolver
{
    string? Resolve(KejiProviderSecretReference secretReference);
}

public sealed class KejiProviderSecretReference
{
    public string EnvironmentVariableName { get; }

    public KejiProviderSecretReference(string environmentVariableName)
    {
        if (string.IsNullOrWhiteSpace(environmentVariableName))
            throw new ArgumentException("Environment variable name is required", nameof(environmentVariableName));
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
    public string? Resolve(KejiProviderSecretReference secretReference)
    {
        ArgumentNullException.ThrowIfNull(secretReference);
        return Environment.GetEnvironmentVariable(secretReference.EnvironmentVariableName);
    }
}
