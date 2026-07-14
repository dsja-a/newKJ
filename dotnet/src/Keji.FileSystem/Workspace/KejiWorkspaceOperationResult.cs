namespace Keji.FileSystem.Workspace;

public sealed class KejiWorkspaceOperationResult<T>
{
    public bool IsSuccess { get; }
    public KejiWorkspaceAccessFailureReason FailureReason { get; }
    public T? Value { get; }

    private KejiWorkspaceOperationResult(
        bool isSuccess,
        KejiWorkspaceAccessFailureReason failureReason,
        T? value)
    {
        IsSuccess = isSuccess;
        FailureReason = failureReason;
        Value = value;
    }

    public static KejiWorkspaceOperationResult<T> Success(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(true, KejiWorkspaceAccessFailureReason.None, value);
    }

    public static KejiWorkspaceOperationResult<T> Failure(KejiWorkspaceAccessFailureReason reason)
    {
        if (!Enum.IsDefined(reason) || reason == KejiWorkspaceAccessFailureReason.None)
            throw new ArgumentException("A failure requires a defined reason.", nameof(reason));
        return new(false, reason, default);
    }
}
