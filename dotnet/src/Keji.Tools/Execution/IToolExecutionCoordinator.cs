namespace Keji.Tools.Execution;

public interface IToolExecutionCoordinator
{
    Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        IReadOnlyDictionary<string, object?>? inputs,
        CancellationToken ct = default);
}