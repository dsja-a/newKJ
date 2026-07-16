namespace Keji.Providers;

public enum KejiChatRole
{
    Invalid = 0,
    System = 1,
    Developer = 2,
    User = 3,
    Assistant = 4,
    Tool = 5,
    Function = 6,
}

public enum KejiFinishReason
{
    Invalid = 0,
    Stop = 1,
    Length = 2,
    ToolCalls = 3,
    ContentFilter = 4,
    Error = 5,
    EndTurn = 6,
}

public enum KejiProviderErrorCode
{
    Invalid = 0,
    Timeout = 1,
    AuthFailed = 2,
    EndpointNotFound = 3,
    RequestTooLarge = 4,
    RateLimited = 5,
    QuotaExceeded = 6,
    ServiceUnavailable = 7,
    GatewayError = 8,
    GatewayTimeout = 9,
    ServerError = 10,
    RequestError = 11,
    InvalidRequest = 12,
    InvalidResponse = 13,
    ResponseTooLarge = 14,
    InvalidContentType = 15,
    StreamProtocolError = 16,
    StreamTruncated = 17,
    StreamInterrupted = 18,
    Cancelled = 19,
    ConnectionError = 20,
    ProviderError = 21,
}

public enum KejiProviderStreamEventKind
{
    Invalid = 0,
    ReasoningToken = 1,
    Token = 2,
    ToolCallBegin = 3,
    ToolCallDelta = 4,
    ToolCallEnd = 5,
    ChoiceFinished = 6,
    Usage = 7,
    Error = 8,
    Done = 9,
}

public enum KejiToolChoice
{
    Invalid = 0,
    Auto = 1,
    Required = 2,
    None = 3,
}
