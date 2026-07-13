using System.Collections.Frozen;

namespace Keji.Security.Authorization;

public static class KejiRoles
{
    public const string Admin = "admin";
    public const string Member = "member";
    public const string Readonly = "readonly";

    private static readonly FrozenSet<string> ValidRoles = new[]
    {
        Admin,
        Member,
        Readonly,
    }.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsValid(string? role) => role is not null && ValidRoles.Contains(role);

    public static IReadOnlySet<string> All => ValidRoles;
}
