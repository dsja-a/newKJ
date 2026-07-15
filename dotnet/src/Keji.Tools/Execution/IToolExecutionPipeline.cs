namespace Keji.Tools.Execution;

public interface IToolExecutionPipeline
{
    Task<ToolExecutionResult> ExecuteAsync(string toolName, IReadOnlyDictionary<string, object?>? inputs, CancellationToken cancellationToken = default);
}
