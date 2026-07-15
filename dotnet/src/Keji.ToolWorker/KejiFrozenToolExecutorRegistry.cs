using System.Collections.Frozen;

namespace Keji.ToolWorker;

public sealed class KejiFrozenToolExecutorRegistry
{
    private readonly FrozenDictionary<string, IKejiToolExecutor> _executors;

    public KejiFrozenToolExecutorRegistry(Dictionary<string, IKejiToolExecutor> executors)
    {
        _executors = executors.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public IKejiToolExecutor? Get(string toolName)
    {
        _executors.TryGetValue(toolName, out var executor);
        return executor;
    }

    public IReadOnlyCollection<string> RegisteredTools => _executors.Keys;
}
