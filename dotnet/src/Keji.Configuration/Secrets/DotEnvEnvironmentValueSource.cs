namespace Keji.Configuration.Secrets;

public class DotEnvEnvironmentValueSource : IEnvironmentValueSource
{
    private readonly IDotEnvStore _store;

    public DotEnvEnvironmentValueSource(IDotEnvStore store)
    {
        _store = store;
    }

    public string? GetValue(string variableName)
    {
        return _store.GetValue(variableName);
    }
}
