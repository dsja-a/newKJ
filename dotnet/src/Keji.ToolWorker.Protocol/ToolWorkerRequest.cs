namespace Keji.ToolWorker.Protocol;

public sealed record ToolWorkerRequest
{
    public string ProtocolVersion { get; init; } = "";
    public string RequestId { get; init; } = "";
    public string DeadlineUtc { get; init; } = "";
    public string ToolName { get; init; } = "";
    public string ContractVersion { get; init; } = "";
    public string InputJson { get; init; } = "";
}
