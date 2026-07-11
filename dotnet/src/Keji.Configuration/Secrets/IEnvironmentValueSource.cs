namespace Keji.Configuration.Secrets;

public interface IEnvironmentValueSource
{
    string? GetValue(string variableName);
}
