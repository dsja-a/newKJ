using Keji.Tools.Execution;

namespace Keji.Api.Services;

public sealed class KejiApiToolExecutionPipeline : IToolExecutionPipeline
{
    public Task<ToolExecutionResult> ExecuteAsync(
        string toolName,IReadOnlyDictionary<string,object?>? inputs,
        CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ToolExecutionResult.Failed(
            "Tool execution is unavailable.","TOOL_EXECUTION_DEFERRED"));
    }
}
