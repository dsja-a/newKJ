namespace Keji.FileSystem.Workspace;

public sealed class KejiPathInspectionResult : IDisposable
{
    public bool IsSafe { get; }
    public KejiWorkspaceAccessFailureReason FailureReason { get; }
    public bool TargetExists { get; }
    public bool TargetIsDirectory { get; }
    internal KejiWindowsPathLease? Lease { get; }

    private KejiPathInspectionResult(
        bool isSafe,
        KejiWorkspaceAccessFailureReason failureReason,
        bool targetExists,
        bool targetIsDirectory,
        KejiWindowsPathLease? lease)
    {
        IsSafe = isSafe;
        FailureReason = failureReason;
        TargetExists = targetExists;
        TargetIsDirectory = targetIsDirectory;
        Lease = lease;
    }

    public static KejiPathInspectionResult Safe(bool targetExists, bool targetIsDirectory)
    {
        if (targetIsDirectory && !targetExists)
            throw new ArgumentException("A directory target must exist.", nameof(targetIsDirectory));
        return new(true, KejiWorkspaceAccessFailureReason.None, targetExists, targetIsDirectory, null);
    }

    internal static KejiPathInspectionResult Safe(KejiWindowsPathLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);
        return new(
            true,
            KejiWorkspaceAccessFailureReason.None,
            lease.TargetExists,
            lease.TargetIsDirectory,
            lease);
    }

    public static KejiPathInspectionResult Unsafe(KejiWorkspaceAccessFailureReason reason)
    {
        if (!Enum.IsDefined(reason) || reason == KejiWorkspaceAccessFailureReason.None)
            throw new ArgumentException("An unsafe result requires a defined failure reason.", nameof(reason));
        return new(false, reason, false, false, null);
    }

    public void Dispose() => Lease?.Dispose();
}
