namespace Keji.Configuration.Secrets;

public class CompositeEnvironmentValueSource : IEnvironmentValueSource
{
    private readonly IReadOnlyList<IEnvironmentValueSource> _sources;

    public CompositeEnvironmentValueSource(params IEnvironmentValueSource[] sources)
    {
        _sources = sources;
    }

    public CompositeEnvironmentValueSource(IEnumerable<IEnvironmentValueSource> sources)
    {
        _sources = new List<IEnvironmentValueSource>(sources);
    }

    public string? GetValue(string variableName)
    {
        foreach (var source in _sources)
        {
            var value = source.GetValue(variableName);
            if (value is not null)
                return value;
        }

        return null;
    }
}
