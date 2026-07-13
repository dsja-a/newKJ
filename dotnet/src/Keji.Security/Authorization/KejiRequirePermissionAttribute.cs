namespace Keji.Security.Authorization;

[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Class,
    AllowMultiple = true,
    Inherited = true)]
public sealed class KejiRequirePermissionAttribute : Attribute
{
    public KejiPermission Permission { get; }

    public KejiRequirePermissionAttribute(KejiPermission permission)
    {
        Permission = permission;
    }
}
