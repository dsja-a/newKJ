using Keji.Tools.Definitions;

namespace Keji.Tools.Execution;

public sealed class ToolWorkerLauncher
{
    private readonly IToolExecutionCoordinator _coordinator;

    public ToolWorkerLauncher(IToolExecutionCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public Task<ToolExecutionResult> ExecuteAsync(
        KejiToolDefinition definition,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken cancellationToken = default)
    {
        return _coordinator.ExecuteAsync(definition.Name.Value, inputs, cancellationToken);
    }
}
