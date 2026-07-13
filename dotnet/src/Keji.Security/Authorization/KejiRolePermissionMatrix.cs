using System.Collections.Frozen;

namespace Keji.Security.Authorization;

public sealed class KejiRolePermissionMatrix : IKejiRolePermissionMatrix
{
    private static readonly FrozenSet<KejiPermission> EmptyPermissions =
        Array.Empty<KejiPermission>().ToFrozenSet();

    private static readonly FrozenSet<KejiPermission> AdminPermissions = new[]
    {
        KejiPermission.AccountSelfRead,
        KejiPermission.ChatUse,
        KejiPermission.ConversationRead,
        KejiPermission.ConversationWrite,
        KejiPermission.FileRead,
        KejiPermission.FileWrite,
        KejiPermission.KnowledgeRead,
        KejiPermission.KnowledgeWrite,
        KejiPermission.ToolCatalogRead,
        KejiPermission.ToolExecuteRead,
        KejiPermission.ToolExecuteWrite,
        KejiPermission.SmartQueryExecute,
        KejiPermission.SettingsRead,
        KejiPermission.SettingsWrite,
        KejiPermission.ModelManage,
        KejiPermission.McpManage,
        KejiPermission.DatabaseRead,
        KejiPermission.DatabaseManage,
        KejiPermission.SkillsRead,
        KejiPermission.SkillsManage,
        KejiPermission.SystemRead,
        KejiPermission.SystemManage,
        KejiPermission.AdminUsers,
        KejiPermission.AdminConversations,
        KejiPermission.AuditRead,
    }.ToFrozenSet();

    private static readonly FrozenSet<KejiPermission> MemberPermissions = new[]
    {
        KejiPermission.AccountSelfRead,
        KejiPermission.ChatUse,
        KejiPermission.ConversationRead,
        KejiPermission.ConversationWrite,
        KejiPermission.FileRead,
        KejiPermission.FileWrite,
        KejiPermission.KnowledgeRead,
        KejiPermission.KnowledgeWrite,
        KejiPermission.ToolCatalogRead,
        KejiPermission.ToolExecuteRead,
        KejiPermission.ToolExecuteWrite,
        KejiPermission.SmartQueryExecute,
        KejiPermission.SettingsRead,
        KejiPermission.DatabaseRead,
        KejiPermission.SkillsRead,
        KejiPermission.SystemRead,
    }.ToFrozenSet();

    private static readonly FrozenSet<KejiPermission> ReadonlyPermissions = new[]
    {
        KejiPermission.AccountSelfRead,
        KejiPermission.ChatUse,
        KejiPermission.ConversationRead,
        KejiPermission.ConversationWrite,
        KejiPermission.FileRead,
        KejiPermission.KnowledgeRead,
        KejiPermission.ToolCatalogRead,
        KejiPermission.ToolExecuteRead,
        KejiPermission.SmartQueryExecute,
        KejiPermission.SettingsRead,
        KejiPermission.DatabaseRead,
        KejiPermission.SkillsRead,
        KejiPermission.SystemRead,
    }.ToFrozenSet();

    private static readonly FrozenDictionary<string, FrozenSet<KejiPermission>> RoleToPermissions =
        new Dictionary<string, FrozenSet<KejiPermission>>(StringComparer.Ordinal)
    {
        [KejiRoles.Admin] = AdminPermissions,
        [KejiRoles.Member] = MemberPermissions,
        [KejiRoles.Readonly] = ReadonlyPermissions,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public IReadOnlySet<KejiPermission> GetEffectivePermissions(string? role)
    {
        if (role is not null && RoleToPermissions.TryGetValue(role, out var permissions))
            return permissions;

        return EmptyPermissions;
    }

    public bool HasPermission(string? role, KejiPermission permission) =>
        GetEffectivePermissions(role).Contains(permission);
}
