namespace Keji.Tools.Execution;

public sealed class ToolExecutionPipeline : IToolExecutionPipeline
{
    private readonly IToolExecutionCoordinator _coordinator;

    public ToolExecutionPipeline(IToolExecutionCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
    }

    public Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? inputs,
        CancellationToken cancellationToken = default)
    {
        return _coordinator.ExecuteAsync(toolName, inputs, cancellationToken);
    }
}
