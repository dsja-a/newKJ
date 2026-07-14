namespace Keji.FileSystem.Workspace;

public sealed class KejiWorkspacePathCandidateResolver : IKejiWorkspacePathCandidateResolver
{
    private readonly KejiWorkspaceOptions _options;

    public KejiWorkspacePathCandidateResolver(KejiWorkspaceOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public KejiWorkspacePathCandidateResult Resolve(KejiWorkspacePathRequest? request)
    {
        if (request is null)
            return KejiWorkspacePathCandidateResult.Reject(
                KejiPathSandboxFailureReason.InvalidScope);

        var identityFailure = KejiWorkspaceIdentityRules.Validate(
            request.Scope,
            request.TargetUserId);
        if (identityFailure != KejiPathSandboxFailureReason.None)
            return KejiWorkspacePathCandidateResult.Reject(identityFailure);
        var pathFailure = KejiWindowsPathRules.Validate(
            request.RelativePath,
            _options.MaxRelativePathLength,
            _options.MaxSegmentLength,
            out var normalized);
        if (pathFailure != KejiPathSandboxFailureReason.None)
            return KejiWorkspacePathCandidateResult.Reject(pathFailure);
        try
        {
            var root = KejiWorkspaceRootLayout.ScopeRoot(
                _options.WorkspaceRoot,
                request.Scope,
                request.TargetUserId);
            var full = string.IsNullOrEmpty(normalized)
                ? Path.GetFullPath(root)
                : Path.GetFullPath(Path.Combine(root, normalized));
            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            if (!KejiWorkspaceRootLayout.IsWithinRoot(normalizedRoot, full))
                return KejiWorkspacePathCandidateResult.Reject(
                    KejiPathSandboxFailureReason.PathEscapesRoot);
            return KejiWorkspacePathCandidateResult.Success(normalizedRoot, full, normalized!);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return KejiWorkspacePathCandidateResult.Reject(
                KejiPathSandboxFailureReason.PathNormalizationFailed);
        }
    }
}
