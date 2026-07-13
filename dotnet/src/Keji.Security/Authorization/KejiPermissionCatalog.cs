using System.Collections.Frozen;

namespace Keji.Security.Authorization;

public static class KejiPermissionCatalog
{
    private static readonly FrozenSet<KejiPermission> AllPermissions = new[]
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

    private static readonly FrozenSet<KejiPermission> WritePermissions = new[]
    {
        KejiPermission.FileWrite,
        KejiPermission.KnowledgeWrite,
        KejiPermission.ToolExecuteWrite,
        KejiPermission.SettingsWrite,
        KejiPermission.ModelManage,
        KejiPermission.McpManage,
        KejiPermission.DatabaseManage,
        KejiPermission.SkillsManage,
        KejiPermission.SystemManage,
        KejiPermission.AdminUsers,
    }.ToFrozenSet();

    private static readonly FrozenSet<KejiPermission> AdminOnlyPermissions = new[]
    {
        KejiPermission.SettingsWrite,
        KejiPermission.ModelManage,
        KejiPermission.McpManage,
        KejiPermission.DatabaseManage,
        KejiPermission.SkillsManage,
        KejiPermission.SystemManage,
        KejiPermission.AdminUsers,
        KejiPermission.AdminConversations,
        KejiPermission.AuditRead,
    }.ToFrozenSet();

    public static IReadOnlySet<KejiPermission> All => AllPermissions;

    public static bool IsDefined(KejiPermission permission) => AllPermissions.Contains(permission);

    public static bool IsWritePermission(KejiPermission permission) => WritePermissions.Contains(permission);

    public static bool IsAdminOnlyPermission(KejiPermission permission) => AdminOnlyPermissions.Contains(permission);
}
