namespace Keji.ToolWorker.Protocol;

public sealed record ToolWorkerResponse
{
    public string ProtocolVersion { get; init; } = "";
    public string RequestId { get; init; } = "";
    public string ToolName { get; init; } = "";
    public string ContractVersion { get; init; } = "";
    public int ErrorCode { get; init; }
    public string ErrorMessage { get; init; } = "";
    public string ResultJson { get; init; } = "";
}
