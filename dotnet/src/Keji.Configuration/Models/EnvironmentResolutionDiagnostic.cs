namespace Keji.Configuration.Models;

public class EnvironmentResolutionDiagnostic
{
    public string ConfigPath { get; }
    public string EnvironmentVariableName { get; }
    public string DiagnosticCode { get; }

    public EnvironmentResolutionDiagnostic(string configPath, string environmentVariableName, string diagnosticCode)
    {
        ConfigPath = configPath;
        EnvironmentVariableName = environmentVariableName;
        DiagnosticCode = diagnosticCode;
    }
}
