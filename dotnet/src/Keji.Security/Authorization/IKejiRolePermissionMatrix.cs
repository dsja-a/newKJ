namespace Keji.Security.Authorization;

public interface IKejiRolePermissionMatrix
{
    IReadOnlySet<KejiPermission> GetEffectivePermissions(string? role);
    bool HasPermission(string? role, KejiPermission permission);
}
