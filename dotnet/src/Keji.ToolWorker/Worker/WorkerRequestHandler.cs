using Keji.Tools.Definitions;
using Keji.Tools.Execution;
using Keji.Tools.Names;
using Keji.Tools.Registry;
using Keji.Tools.Validation;
using Keji.ToolWorker.Execution;

namespace Keji.ToolWorker.Worker;

public sealed class WorkerRequestHandler
{
    private readonly IKejiToolRegistry _registry;

    public WorkerRequestHandler(IKejiToolRegistry registry)
    {
        _registry = registry;
    }

    public WorkerProtocolMessage Handle(WorkerProtocolMessage request)
    {
        var requestId = request.RequestId;
        var toolName = request.Tool;

        if (string.IsNullOrEmpty(toolName))
            return Error(requestId, "Tool name is required.", "INVALID_REQUEST");

        if (!KejiToolName.TryCreate(toolName, out var name))
            return Error(requestId, "Invalid tool name format.", "INVALID_TOOL_NAME");

        var resolution = _registry.Resolve(name);
        if (resolution.Status != KejiToolResolutionStatus.Found || resolution.Definition is null)
            return Error(requestId, "Tool not found in worker registry.", "NOT_FOUND");

        var def = resolution.Definition;

        if (def.Availability != KejiToolAvailability.Executable)
            return Error(requestId, "Tool is not executable.", "NOT_EXECUTABLE");

        if (def.ExecutionTarget != KejiToolExecutionTarget.ToolWorker)
            return Error(requestId, "Tool is not targeted for ToolWorker execution.", "TARGET_MISMATCH");

        if (def.ContractVersion != request.ContractVersion)
            return Error(requestId, "Contract version mismatch.", "VERSION_MISMATCH");

        var inputs = request.Args as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>();
        var validation = KejiToolInputValidator.Validate(_registry, toolName, inputs);
        if (!validation.IsValid)
            return Error(requestId, validation.ErrorMessage ?? "Input validation failed.", "VALIDATION_ERROR");

        try
        {
            return def.Name.Value switch
            {
                "calculator" => ExecuteCalculator(requestId, inputs),
                "get_time" => ExecuteGetTime(requestId),
                _ => Error(requestId, "No executor registered for tool.", "NO_EXECUTOR")
            };
        }
        catch (Exception ex)
        {
            return Error(requestId, $"Execution error: {ex.Message}", "EXECUTION_ERROR");
        }
    }

    private static WorkerProtocolMessage ExecuteCalculator(string requestId, IReadOnlyDictionary<string, object?> inputs)
    {
        if (!inputs.TryGetValue("expr", out var exprValue) || exprValue is not string expr)
            return Error(requestId, "Expression is required.", "VALIDATION_ERROR");

        var result = CalculatorExecutor.Execute(expr);
        return Success(requestId, result);
    }

    private static WorkerProtocolMessage ExecuteGetTime(string requestId)
    {
        var result = GetTimeExecutor.Execute();
        return Success(requestId, result);
    }

    private static WorkerProtocolMessage Success(string requestId, object? result) => new()
    {
        Protocol = "keji-toolworker-v1",
        RequestId = requestId,
        Success = true,
        Result = result
    };

    private static WorkerProtocolMessage Error(string requestId, string error, string errorCode) => new()
    {
        Protocol = "keji-toolworker-v1",
        RequestId = requestId,
        Success = false,
        Error = error,
        ErrorCode = errorCode
    };
}
