namespace Keji.Configuration.Models;

public class KejiConfigurationLoadResult
{
    public KejiConfigurationDocument Document { get; }
    public IReadOnlyList<EnvironmentResolutionDiagnostic> Diagnostics { get; }

    public KejiConfigurationLoadResult(
        KejiConfigurationDocument document,
        IReadOnlyList<EnvironmentResolutionDiagnostic> diagnostics)
    {
        Document = document;
        Diagnostics = diagnostics;
    }
}
