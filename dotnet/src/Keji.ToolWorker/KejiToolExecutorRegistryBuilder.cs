namespace Keji.ToolWorker;

public sealed class KejiToolExecutorRegistryBuilder
{
    private readonly Dictionary<string, IKejiToolExecutor> _executors = new(StringComparer.Ordinal);
    private bool _frozen;

    public void Register(IKejiToolExecutor executor)
    {
        if (_frozen) throw new InvalidOperationException("Registry already frozen");
        _executors.Add(executor.ToolName, executor);
    }

    public KejiFrozenToolExecutorRegistry Freeze()
    {
        _frozen = true;
        return new KejiFrozenToolExecutorRegistry(_executors);
    }
}
