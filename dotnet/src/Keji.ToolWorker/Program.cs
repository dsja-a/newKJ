using System.Text.Json;
using Keji.Tools.Definitions;
using Keji.Tools.Names;
using Keji.Tools.Registry;
using Keji.Tools.Validation;
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
if (string.IsNullOrEmpty(requestId) || requestId.Length != 32 || !IsLowerHexString(requestId))
{
    await writer.WriteResponseAsync(MakeError("", ToolWorkerErrorCode.InvalidRequest, "RequestId must be 32-character lowercase hex string"));
    return 1;
}

if (request.ProtocolVersion != ProtocolVersion.String)
{
    await writer.WriteResponseAsync(MakeError(requestId, ToolWorkerErrorCode.ProtocolError, $"Unsupported protocol version: {request.ProtocolVersion}"));
    return 1;
}

if (!DateTimeOffset.TryParse(request.DeadlineUtc, out var deadline) || deadline <= DateTimeOffset.UtcNow || deadline.Offset != TimeSpan.Zero)
{
    await writer.WriteResponseAsync(MakeError(requestId, ToolWorkerErrorCode.InvalidRequest, "Missing, past, or non-UTC deadline"));
    return 1;
}

var maxDeadline = DateTimeOffset.UtcNow.AddSeconds(61);
if (deadline > maxDeadline)
{
    await writer.WriteResponseAsync(MakeError(requestId, ToolWorkerErrorCode.InvalidRequest, "Deadline too far in the future"));
    return 1;
}

var toolName = request.ToolName;
if (string.IsNullOrEmpty(toolName) || !KejiToolName.TryCreate(toolName, out var name))
{
    await writer.WriteResponseAsync(MakeError(requestId, ToolWorkerErrorCode.UnknownTool, $"Invalid tool name: {toolName}", toolName, request.ContractVersion));
    return 1;
}

var resolution = toolRegistry.Resolve(name);
if (resolution.Status != KejiToolResolutionStatus.Found || resolution.Definition is null)
{
    await writer.WriteResponseAsync(MakeError(requestId, ToolWorkerErrorCode.UnknownTool, $"Unknown tool: {toolName}", toolName, request.ContractVersion));
    return 1;
}

var def = resolution.Definition;

if (def.ContractVersion.ToString() != request.ContractVersion)
{
    await writer.WriteResponseAsync(MakeError(requestId, ToolWorkerErrorCode.ContractMismatch, $"Contract version mismatch: expected {def.ContractVersion}, got {request.ContractVersion}", toolName, request.ContractVersion));
    return 1;
}

if (def.Availability != KejiToolAvailability.Executable)
{
    await writer.WriteResponseAsync(MakeError(requestId, ToolWorkerErrorCode.InvalidRequest, $"Tool {toolName} is not executable", toolName, request.ContractVersion));
    return 1;
}

if (def.ExecutionTarget != KejiToolExecutionTarget.ToolWorker)
{
    await writer.WriteResponseAsync(MakeError(requestId, ToolWorkerErrorCode.InvalidRequest, $"Tool {toolName} is not targeted for Worker execution", toolName, request.ContractVersion));
    return 1;
}

// Re-validate input schema on worker side
var inputs = DeserializeInputs(request.InputJson);
var validation = KejiToolInputValidator.Validate(toolRegistry, toolName, inputs);
if (!validation.IsValid)
{
    await writer.WriteResponseAsync(MakeError(requestId, ToolWorkerErrorCode.InvalidRequest, validation.ErrorMessage ?? "Input validation failed", toolName, request.ContractVersion));
    return 1;
}

var executor = executors.Get(toolName);
if (executor is null)
{
    await writer.WriteResponseAsync(MakeError(requestId, ToolWorkerErrorCode.UnknownTool, $"No executor registered for {toolName}", toolName, request.ContractVersion));
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
        ToolName = toolName,
        ContractVersion = request.ContractVersion,
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
        ToolName = toolName,
        ContractVersion = request.ContractVersion,
        ErrorCode = (int)ToolWorkerErrorCode.ExecutionFailed,
        ErrorMessage = "Execution failed"
    });
}

return 0;

static IReadOnlyDictionary<string, object?>? DeserializeInputs(string inputJson)
{
    if (string.IsNullOrEmpty(inputJson) || inputJson == "{}")
        return null;

    using var doc = JsonDocument.Parse(inputJson);
    var root = doc.RootElement;
    if (root.ValueKind != JsonValueKind.Object)
        return null;

    var dict = new Dictionary<string, object?>();
    foreach (var prop in root.EnumerateObject())
    {
        dict[prop.Name] = prop.Value.ValueKind switch
        {
            JsonValueKind.String => prop.Value.GetString(),
            JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? l : prop.Value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => prop.Value.GetRawText()
        };
    }
    return dict;
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

static ToolWorkerResponse MakeError(string requestId, ToolWorkerErrorCode code, string message, string? toolName = null, string? contractVersion = null) => new()
{
    ProtocolVersion = ProtocolVersion.String,
    RequestId = requestId,
    ToolName = toolName ?? "",
    ContractVersion = contractVersion ?? "",
    ErrorCode = (int)code,
    ErrorMessage = message
};

static bool IsLowerHexString(string s)
{
    foreach (char c in s)
        if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
            return false;
    return true;
}
