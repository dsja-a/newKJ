namespace Keji.FileSystem.Workspace;

public sealed class KejiWorkspaceAccessDecision
{
    public bool IsAllowed { get; }
    public KejiWorkspaceAccessFailureReason FailureReason { get; }
    public KejiPathSandboxFailureReason PathFailureReason { get; }
    internal KejiWorkspacePathCandidateResult? Candidate { get; }

    private KejiWorkspaceAccessDecision(
        bool isAllowed,
        KejiWorkspaceAccessFailureReason failureReason,
        KejiPathSandboxFailureReason pathFailureReason,
        KejiWorkspacePathCandidateResult? candidate)
    {
        IsAllowed = isAllowed;
        FailureReason = failureReason;
        PathFailureReason = pathFailureReason;
        Candidate = candidate;
    }

    internal static KejiWorkspaceAccessDecision Allow(KejiWorkspacePathCandidateResult candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.IsValid)
            throw new ArgumentException("An allowed decision requires a valid candidate.", nameof(candidate));
        return new(true, KejiWorkspaceAccessFailureReason.None, KejiPathSandboxFailureReason.None, candidate);
    }

    internal static KejiWorkspaceAccessDecision Deny(
        KejiWorkspaceAccessFailureReason reason,
        KejiPathSandboxFailureReason pathReason = KejiPathSandboxFailureReason.None)
    {
        if (!Enum.IsDefined(reason) || reason == KejiWorkspaceAccessFailureReason.None)
            throw new ArgumentException("A denial requires a defined failure reason.", nameof(reason));
        if (!Enum.IsDefined(pathReason))
            throw new ArgumentException("Path failure reason must be defined.", nameof(pathReason));
        if (reason == KejiWorkspaceAccessFailureReason.CandidateRejected &&
            pathReason == KejiPathSandboxFailureReason.None)
            throw new ArgumentException("Candidate rejection requires a path reason.", nameof(pathReason));
        if (reason != KejiWorkspaceAccessFailureReason.CandidateRejected &&
            pathReason != KejiPathSandboxFailureReason.None)
            throw new ArgumentException("Only candidate rejection may include a path reason.", nameof(pathReason));
        return new(false, reason, pathReason, null);
    }
}
