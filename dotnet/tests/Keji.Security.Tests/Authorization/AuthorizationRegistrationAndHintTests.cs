using Keji.Security.Auth;
using Keji.Security.Authorization;
using Keji.Security.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Keji.Security.Tests.Authorization;

public sealed class AuthorizationRegistrationAndHintTests
{
    [Fact]
    public void SecurityFoundation_RegistersAllAuthorizationServicesAsSingletons()
    {
        var services = new ServiceCollection();

        services.AddKejiSecurityFoundation(new KejiSecurityOptions { Enabled = false });

        AssertSingleton<IKejiRolePermissionMatrix, KejiRolePermissionMatrix>(services);
        AssertSingleton<IKejiAuthorizationService, KejiAuthorizationService>(services);
        AssertSingleton<IKejiToolAuthorizationService, KejiToolAuthorizationService>(services);
        AssertSingleton<IKejiRolePermissionHintProvider, KejiRolePermissionHintProvider>(services);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Admin")]
    [InlineData("unknown")]
    public void HintProvider_InvalidOrMissingRole_ReturnsNoHint(string? role)
    {
        var provider = new KejiRolePermissionHintProvider();
        var user = role is null ? null : User(role);

        Assert.Equal(string.Empty, provider.GetHint(user));
    }

    [Fact]
    public void HintProvider_AdminHint_PreservesRegistrationAndWorkspaceBoundaries()
    {
        var provider = new KejiRolePermissionHintProvider();

        var hint = provider.GetHint(User(KejiRoles.Admin));

        Assert.Equal("当前为管理员：可使用已注册且获授权的工具，并访问授权工作区范围。", hint);
    }

    [Fact]
    public void HintProvider_MemberHint_DescribesMemberBoundary()
    {
        var provider = new KejiRolePermissionHintProvider();

        var hint = provider.GetHint(User(KejiRoles.Member));

        Assert.Equal("当前为成员账号：可读写共享目录与个人目录，不可访问其他用户私人文件夹。", hint);
    }

    [Fact]
    public void HintProvider_ReadonlyHint_DescribesReadonlyBoundary()
    {
        var provider = new KejiRolePermissionHintProvider();

        var hint = provider.GetHint(User(KejiRoles.Readonly));

        Assert.Equal(
            "当前为只读账号：仅可查询/读取，不可创建、修改、删除文件或执行写入类工具；文件路径仅限「共享文件」与「我的文件」。",
            hint);
    }

    private static CurrentUser User(string role) =>
        new("user-id", "test-user", role, "Test User", KejiAuthenticationKind.Jwt);

    private static void AssertSingleton<TService, TImplementation>(IServiceCollection services)
    {
        var descriptor = Assert.Single(
            services,
            candidate => candidate.ServiceType == typeof(TService));

        Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }
}
