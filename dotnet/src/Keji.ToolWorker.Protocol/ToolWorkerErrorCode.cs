namespace Keji.ToolWorker.Protocol;

public enum ToolWorkerErrorCode
{
    None = 0,
    UnknownTool = 1,
    InvalidRequest = 2,
    ExecutionFailed = 3,
    Timeout = 4,
    InternalError = 5,
    ContractMismatch = 6,
    RequestTooLarge = 7,
    ProtocolError = 8,
    InvalidInput = 9,
    Cancelled = 10,
    WorkerUnavailable = 11,
    OutputTooLarge = 12
}
