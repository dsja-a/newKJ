using System.Text.Json;
using Keji.Tools.Definitions;
using Keji.Tools.Names;
using Keji.Tools.Registry;
using Keji.ToolWorker;
using Keji.ToolWorker.Protocol;
using static Keji.Tools.Catalog.BuiltInToolCatalog;

var executors = BuildExecutorRegistry();
var toolRegistry = BuildToolRegistry();
var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

using var stdin = Console.OpenStandardInput();
using var stdout = Console.OpenStandardOutput();
var reader = new WorkerFrameReader(stdin);
var writer = new WorkerFrameWriter(stdout);

while (true)
{
    ToolWorkerRequest? request;
    try
    {
        request = await reader.ReadRequestAsync();
    }
    catch (EndOfStreamException)
    {
        return 0;
    }
    catch (Exception)
    {
        await writer.WriteResponseAsync(new ToolWorkerResponse
        {
            ProtocolVersion = ProtocolVersion.String,
            RequestId = "",
            ErrorCode = (int)ToolWorkerErrorCode.ProtocolError,
            ErrorMessage = "Protocol error"
        });
        return 1;
    }

    if (request is null)
    {
        await writer.WriteResponseAsync(new ToolWorkerResponse
        {
            ProtocolVersion = ProtocolVersion.String,
            RequestId = "",
            ErrorCode = (int)ToolWorkerErrorCode.InvalidRequest,
            ErrorMessage = "Empty request"
        });
        return 1;
    }

    var requestId = request.RequestId;
    if (string.IsNullOrEmpty(requestId) || requestId.Length != 32 || !IsHexString(requestId))
    {
        await writer.WriteResponseAsync(MakeError("", ToolWorkerErrorCode.InvalidRequest, "RequestId must be 32-character hex string"));
        return 1;
    }

    if (request.ProtocolVersion != ProtocolVersion.String)
    {
        await writer.WriteResponseAsync(MakeError(requestId, ToolWorkerErrorCode.ProtocolError, $"Unsupported protocol version: {request.ProtocolVersion}"));
        return 1;
    }

    if (!DateTimeOffset.TryParse(request.DeadlineUtc, out var deadline) || deadline <= DateTimeOffset.UtcNow)
    {
        await writer.WriteResponseAsync(MakeError(requestId, ToolWorkerErrorCode.InvalidRequest, "Missing or past deadline"));
        return 1;
    }

    var toolName = request.ToolName;
    if (string.IsNullOrEmpty(toolName) || !KejiToolName.TryCreate(toolName, out var name))
    {
        await writer.WriteResponseAsync(MakeError(requestId, ToolWorkerErrorCode.UnknownTool, $"Invalid tool name: {toolName}"));
        return 1;
    }

    var resolution = toolRegistry.Resolve(name);
    if (resolution.Status != KejiToolResolutionStatus.Found || resolution.Definition is null)
    {
        await writer.WriteResponseAsync(MakeError(requestId, ToolWorkerErrorCode.UnknownTool, $"Unknown tool: {toolName}"));
        return 1;
    }

    var def = resolution.Definition;

    if (def.ContractVersion.ToString() != request.ContractVersion)
    {
        await writer.WriteResponseAsync(MakeError(requestId, ToolWorkerErrorCode.ContractMismatch, $"Contract version mismatch: expected {def.ContractVersion}, got {request.ContractVersion}"));
        return 1;
    }

    if (def.Availability != KejiToolAvailability.Executable)
    {
        await writer.WriteResponseAsync(MakeError(requestId, ToolWorkerErrorCode.InvalidRequest, $"Tool {toolName} is not executable"));
        return 1;
    }

    if (def.ExecutionTarget != KejiToolExecutionTarget.ToolWorker)
    {
        await writer.WriteResponseAsync(MakeError(requestId, ToolWorkerErrorCode.InvalidRequest, $"Tool {toolName} is not targeted for Worker execution"));
        return 1;
    }

    var executor = executors.Get(toolName);
    if (executor is null)
    {
        await writer.WriteResponseAsync(MakeError(requestId, ToolWorkerErrorCode.UnknownTool, $"No executor registered for {toolName}"));
        return 1;
    }

    try
    {
        var result = executor.Execute(request.InputJson);
        var resultJson = JsonSerializer.Serialize(result, jsonOptions);

        await writer.WriteResponseAsync(new ToolWorkerResponse
        {
            ProtocolVersion = ProtocolVersion.String,
            RequestId = requestId,
            ErrorCode = (int)ToolWorkerErrorCode.None,
            ResultJson = resultJson
        });
    }
    catch (Exception)
    {
        await writer.WriteResponseAsync(new ToolWorkerResponse
        {
            ProtocolVersion = ProtocolVersion.String,
            RequestId = requestId,
            ErrorCode = (int)ToolWorkerErrorCode.ExecutionFailed,
            ErrorMessage = "Execution failed"
        });
    }
}

static KejiFrozenToolExecutorRegistry BuildExecutorRegistry()
{
    var builder = new KejiToolExecutorRegistryBuilder();
    builder.Register(new CalculatorToolExecutor());
    builder.Register(new GetTimeToolExecutor());
    return builder.Freeze();
}

static IKejiToolRegistry BuildToolRegistry()
{
    var builder = CreateBuilder();
    return builder.Build();
}

static ToolWorkerResponse MakeError(string requestId, ToolWorkerErrorCode code, string message) => new()
{
    ProtocolVersion = ProtocolVersion.String,
    RequestId = requestId,
    ErrorCode = (int)code,
    ErrorMessage = message
};

static bool IsHexString(string s)
{
    foreach (char c in s)
        if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
            return false;
    return true;
}
