namespace Keji.Tools.Execution;

public sealed class ToolExecutionResult
{
    public bool Success { get; }
    public object? Value { get; }
    public string? ErrorMessage { get; }
    public string? ErrorCode { get; }
    public TimeSpan Duration { get; }

    public ToolExecutionResult(bool success, object? value = null, string? errorMessage = null, string? errorCode = null, TimeSpan? duration = null)
    {
        Success = success;
        Value = value;
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
        Duration = duration ?? TimeSpan.Zero;
    }

    public static ToolExecutionResult Successful(object? value, TimeSpan duration) =>
        new(true, value: value, duration: duration);

    public static ToolExecutionResult Failed(string error, string? errorCode = null, TimeSpan? duration = null) =>
        new(false, errorMessage: error, errorCode: errorCode, duration: duration);
}
