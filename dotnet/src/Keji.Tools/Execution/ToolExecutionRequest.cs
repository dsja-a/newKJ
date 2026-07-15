using System.Collections.Immutable;

namespace Keji.Tools.Execution;

public sealed class ToolExecutionRequest
{
    public string ToolName { get; }
    public IReadOnlyDictionary<string, object?> Inputs { get; }

    public ToolExecutionRequest(string toolName, IReadOnlyDictionary<string, object?> inputs)
    {
        ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
        Inputs = inputs ?? new Dictionary<string, object?>();
    }
}
