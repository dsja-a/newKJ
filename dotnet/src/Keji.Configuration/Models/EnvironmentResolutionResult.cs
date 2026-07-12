namespace Keji.Configuration.Models;

public class EnvironmentResolutionResult
{
    public ConfigNode Root { get; }
    public IReadOnlyList<EnvironmentResolutionDiagnostic> Diagnostics { get; }

    public EnvironmentResolutionResult(ConfigNode root, IReadOnlyList<EnvironmentResolutionDiagnostic> diagnostics)
    {
        Root = root;
        Diagnostics = diagnostics;
    }
}
