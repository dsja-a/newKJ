using System.Text.RegularExpressions;

namespace Keji.FileSystem.Workspace;

internal static partial class KejiWorkspaceIdentityRules
{
    [GeneratedRegex("^[0-9a-f]{16}$", RegexOptions.CultureInvariant)]
    private static partial Regex UserIdRegex();

    internal static bool IsValidUserId(string? userId) =>
        userId is not null && UserIdRegex().IsMatch(userId);

    public static KejiPathSandboxFailureReason Validate(
        KejiWorkspaceScope scope, string? userId)
    {
        if (scope == KejiWorkspaceScope.Shared)
            return userId is null ? KejiPathSandboxFailureReason.None : KejiPathSandboxFailureReason.UnexpectedUserId;
        if (scope != KejiWorkspaceScope.User)
            return KejiPathSandboxFailureReason.InvalidScope;
        if (userId is null)
            return KejiPathSandboxFailureReason.MissingUserId;
        if (userId.Length == 0 || string.IsNullOrWhiteSpace(userId))
            return KejiPathSandboxFailureReason.MissingUserId;
        return IsValidUserId(userId)
            ? KejiPathSandboxFailureReason.None
            : KejiPathSandboxFailureReason.InvalidUserId;
    }
}
