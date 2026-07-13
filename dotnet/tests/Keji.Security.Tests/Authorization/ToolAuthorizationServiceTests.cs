using Keji.Security.Auth;
using Keji.Security.Authorization;

namespace Keji.Security.Tests.Authorization;

public sealed class ToolAuthorizationServiceTests
{
    private readonly KejiToolAuthorizationService _service = new();

    [Fact]
    public void Service_ImplementsDedicatedToolAuthorizationInterface()
    {
        Assert.IsAssignableFrom<IKejiToolAuthorizationService>(_service);
    }

    [Theory]
    [InlineData(KejiRoles.Admin)]
    [InlineData(KejiRoles.Member)]
    [InlineData(KejiRoles.Readonly)]
    public void ReadDescriptor_AllThreeKnownRolesAreAllowed(string role)
    {
        AssertAllowed(_service.Authorize(
            User(role),
            new KejiToolPermissionDescriptor("read_file", KejiToolAccessLevel.Read)));
    }

    [Theory]
    [InlineData(KejiRoles.Admin)]
    [InlineData(KejiRoles.Member)]
    public void WriteDescriptor_AdminAndMemberAreAllowed(string role)
    {
        AssertAllowed(_service.Authorize(
            User(role),
            new KejiToolPermissionDescriptor("write_file", KejiToolAccessLevel.Write)));
    }

    [Fact]
    public void WriteDescriptor_ReadonlyIsDeniedAsReadonlyWrite()
    {
        AssertDenied(
            _service.Authorize(
                User(KejiRoles.Readonly),
                new KejiToolPermissionDescriptor("write_file", KejiToolAccessLevel.Write)),
            KejiAuthorizationFailureReason.ReadonlyWriteDenied);
    }

    [Fact]
    public void AdminDescriptor_AdminIsAllowed()
    {
        AssertAllowed(_service.Authorize(
            User(KejiRoles.Admin),
            new KejiToolPermissionDescriptor("system_admin", KejiToolAccessLevel.Admin)));
    }

    [Theory]
    [InlineData(KejiRoles.Member)]
    [InlineData(KejiRoles.Readonly)]
    public void AdminDescriptor_NonAdminIsDeniedAsAdminRequired(string role)
    {
        AssertDenied(
            _service.Authorize(
                User(role),
                new KejiToolPermissionDescriptor("system_admin", KejiToolAccessLevel.Admin)),
            KejiAuthorizationFailureReason.AdminRequired);
    }

    [Fact]
    public void NullUser_IsDeniedAsUnauthenticated()
    {
        AssertDenied(
            _service.Authorize(
                null!,
                new KejiToolPermissionDescriptor("read_file", KejiToolAccessLevel.Read)),
            KejiAuthorizationFailureReason.Unauthenticated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Admin")]
    [InlineData("MEMBER")]
    [InlineData("unknown")]
    public void InvalidRole_IsDeniedAsUnknownRole(string? role)
    {
        AssertDenied(
            _service.Authorize(
                User(role!),
                new KejiToolPermissionDescriptor("read_file", KejiToolAccessLevel.Read)),
            KejiAuthorizationFailureReason.UnknownRole);
    }

    [Theory]
    [InlineData(KejiRoles.Admin)]
    [InlineData(KejiRoles.Member)]
    [InlineData(KejiRoles.Readonly)]
    public void NullDescriptor_EveryRoleIsDeniedAsInvalidDescriptor(string role)
    {
        AssertDenied(
            _service.Authorize(User(role), null!),
            KejiAuthorizationFailureReason.InvalidToolDescriptor);
    }

    [Theory]
    [InlineData(KejiRoles.Admin)]
    [InlineData(KejiRoles.Member)]
    [InlineData(KejiRoles.Readonly)]
    public void LegacyUnknownTool_EveryRoleIsDeniedAsUnknownTool(string role)
    {
        AssertDenied(
            _service.AuthorizeLegacy(User(role), "totally_unknown_tool"),
            KejiAuthorizationFailureReason.UnknownTool);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void LegacyInvalidToolName_IsDeniedAsInvalidDescriptor(string? toolName)
    {
        AssertDenied(
            _service.AuthorizeLegacy(User(KejiRoles.Admin), toolName),
            KejiAuthorizationFailureReason.InvalidToolDescriptor);
    }

    [Fact]
    public void LegacyKnownReadTool_ReadonlyIsAllowed()
    {
        AssertAllowed(_service.AuthorizeLegacy(User(KejiRoles.Readonly), "read_file"));
    }

    [Fact]
    public void LegacyKnownWriteTool_ReadonlyIsDeniedAsReadonlyWrite()
    {
        AssertDenied(
            _service.AuthorizeLegacy(User(KejiRoles.Readonly), "write_file"),
            KejiAuthorizationFailureReason.ReadonlyWriteDenied);
    }

    [Fact]
    public void LegacyNullUser_IsDeniedAsUnauthenticated()
    {
        AssertDenied(
            _service.AuthorizeLegacy(null!, "read_file"),
            KejiAuthorizationFailureReason.Unauthenticated);
    }

    private static CurrentUser User(string role) =>
        new("user-id", "test-user", role, "Test User", KejiAuthenticationKind.Jwt);

    private static void AssertAllowed(KejiAuthorizationDecision decision)
    {
        Assert.True(decision.IsAllowed);
        Assert.Equal(KejiAuthorizationFailureReason.None, decision.FailureReason);
    }

    private static void AssertDenied(
        KejiAuthorizationDecision decision,
        KejiAuthorizationFailureReason expectedReason)
    {
        Assert.False(decision.IsAllowed);
        Assert.Equal(expectedReason, decision.FailureReason);
    }
}
