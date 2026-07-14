namespace Keji.FileSystem.Workspace;

public sealed class KejiWorkspacePathCandidateResult
{
    public bool IsValid { get; }
    public KejiPathSandboxFailureReason FailureReason { get; }
    internal string? RootPath { get; }
    internal string? FullPath { get; }
    public string? NormalizedRelativePath { get; }

    private KejiWorkspacePathCandidateResult(
        bool isValid,
        KejiPathSandboxFailureReason failureReason,
        string? rootPath,
        string? fullPath,
        string? normalizedRelativePath)
    {
        IsValid = isValid;
        FailureReason = failureReason;
        RootPath = rootPath;
        FullPath = fullPath;
        NormalizedRelativePath = normalizedRelativePath;
    }

    internal static KejiWorkspacePathCandidateResult Success(
        string rootPath, string fullPath, string normalizedRelativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            throw new ArgumentException("RootPath is required.", nameof(rootPath));
        if (string.IsNullOrWhiteSpace(fullPath))
            throw new ArgumentException("FullPath is required.", nameof(fullPath));
        if (normalizedRelativePath is null)
            throw new ArgumentNullException(nameof(normalizedRelativePath));
        return new(true, KejiPathSandboxFailureReason.None, rootPath, fullPath, normalizedRelativePath);
    }

    internal static KejiWorkspacePathCandidateResult Reject(KejiPathSandboxFailureReason reason)
    {
        if (!Enum.IsDefined(reason) || reason == KejiPathSandboxFailureReason.None)
            throw new ArgumentException("A rejection requires a defined failure reason.", nameof(reason));
        return new(false, reason, null, null, null);
    }
}
