using System.Text.Json.Serialization;

namespace Keji.Tools.Execution;

public sealed class WorkerProtocolMessage
{
    [JsonPropertyName("protocol")]
    public string Protocol { get; init; } = "keji-toolworker-v1";

    [JsonPropertyName("requestId")]
    public string RequestId { get; init; } = "";

    [JsonPropertyName("tool")]
    public string? Tool { get; init; }

    [JsonPropertyName("args")]
    public Dictionary<string, object?>? Args { get; init; }

    [JsonPropertyName("contractVersion")]
    public int ContractVersion { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("result")]
    public object? Result { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }
}
