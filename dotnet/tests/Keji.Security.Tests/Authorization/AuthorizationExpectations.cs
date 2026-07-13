using Keji.Security.Authorization;

namespace Keji.Security.Tests.Authorization;

internal static class AuthorizationExpectations
{
    internal static readonly KejiPermission[] AllPermissions =
    [
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
    ];

    internal static readonly KejiPermission[] MemberPermissions =
    [
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
    ];

    internal static readonly KejiPermission[] ReadonlyPermissions =
    [
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
    ];

    internal static readonly KejiPermission[] WritePermissions =
    [
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
    ];

    internal static readonly KejiPermission[] AdminOnlyPermissions =
    [
        KejiPermission.SettingsWrite,
        KejiPermission.ModelManage,
        KejiPermission.McpManage,
        KejiPermission.DatabaseManage,
        KejiPermission.SkillsManage,
        KejiPermission.SystemManage,
        KejiPermission.AdminUsers,
        KejiPermission.AdminConversations,
        KejiPermission.AuditRead,
    ];

    internal static void AssertExactPermissions(
        IEnumerable<KejiPermission> expected,
        IEnumerable<KejiPermission> actual)
    {
        Assert.Equal(expected.OrderBy(static permission => permission),
            actual.OrderBy(static permission => permission));
    }
}
