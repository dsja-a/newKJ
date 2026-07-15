using System.Text.Json;
using Keji.Tools.Execution;
using Keji.ToolWorker.Worker;

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

var requestJson = Console.In.ReadLine();
if (string.IsNullOrEmpty(requestJson))
{
    var empty = new WorkerProtocolMessage
    {
        Protocol = "keji-toolworker-v1",
        RequestId = "",
        Success = false,
        Error = "No request received.",
        ErrorCode = "EMPTY_REQUEST"
    };
    Console.Out.WriteLine(JsonSerializer.Serialize(empty, jsonOptions));
    return;
}

WorkerProtocolMessage? request;
try
{
    request = JsonSerializer.Deserialize<WorkerProtocolMessage>(requestJson, jsonOptions);
}
catch (JsonException ex)
{
    var invalid = new WorkerProtocolMessage
    {
        Protocol = "keji-toolworker-v1",
        RequestId = "",
        Success = false,
        Error = $"Invalid request JSON: {ex.Message}",
        ErrorCode = "INVALID_JSON"
    };
    Console.Out.WriteLine(JsonSerializer.Serialize(invalid, jsonOptions));
    return;
}

if (request is null)
{
    var nullRequest = new WorkerProtocolMessage
    {
        Protocol = "keji-toolworker-v1",
        RequestId = "",
        Success = false,
        Error = "Request is null.",
        ErrorCode = "NULL_REQUEST"
    };
    Console.Out.WriteLine(JsonSerializer.Serialize(nullRequest, jsonOptions));
    return;
}

var registry = BuiltInToolWorkerRegistry.Instance;
var handler = new WorkerRequestHandler(registry);
var response = handler.Handle(request);

Console.Out.WriteLine(JsonSerializer.Serialize(response, jsonOptions));
