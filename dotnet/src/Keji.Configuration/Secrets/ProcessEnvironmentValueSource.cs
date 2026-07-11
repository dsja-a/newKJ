namespace Keji.Configuration.Secrets;

public class ProcessEnvironmentValueSource : IEnvironmentValueSource
{
    public string? GetValue(string variableName)
    {
        return Environment.GetEnvironmentVariable(variableName);
    }
}
