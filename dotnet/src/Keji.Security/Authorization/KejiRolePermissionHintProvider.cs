using Keji.Security.Auth;

namespace Keji.Security.Authorization;

public sealed class KejiRolePermissionHintProvider : IKejiRolePermissionHintProvider
{
    public string GetHint(CurrentUser? user)
    {
        if (user is null || !KejiRoles.IsValid(user.Role))
            return string.Empty;

        if (user.IsAdmin)
            return "当前为管理员：可使用已注册且获授权的工具，并访问授权工作区范围。";

        if (string.Equals(user.Role, KejiRoles.Readonly, StringComparison.Ordinal))
            return "当前为只读账号：仅可查询/读取，不可创建、修改、删除文件或执行写入类工具；文件路径仅限「共享文件」与「我的文件」。";

        return "当前为成员账号：可读写共享目录与个人目录，不可访问其他用户私人文件夹。";
    }
}
