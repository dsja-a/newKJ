using Keji.Security.Auth;
using Keji.Security.Authorization;

namespace Keji.Security.Tests.Authorization;

public sealed class AuthorizationServiceTests
{
    private readonly KejiAuthorizationService _service =
        new(new KejiRolePermissionMatrix());

    public static TheoryData<KejiPermission> AllPermissions =>
        CreateTheoryData(AuthorizationExpectations.AllPermissions);

    public static TheoryData<KejiPermission> MemberPermissions =>
        CreateTheoryData(AuthorizationExpectations.MemberPermissions);

    public static TheoryData<KejiPermission> ReadonlyPermissions =>
        CreateTheoryData(AuthorizationExpectations.ReadonlyPermissions);

    public static TheoryData<KejiPermission> WritePermissions =>
        CreateTheoryData(AuthorizationExpectations.WritePermissions);

    [Theory]
    [MemberData(nameof(AllPermissions))]
    public void Admin_SinglePermission_AllTwentyFiveAreAllowed(KejiPermission permission)
    {
        AssertAllowed(_service.Authorize(User(KejiRoles.Admin), permission));
    }

    [Theory]
    [MemberData(nameof(MemberPermissions))]
    public void Member_SinglePermission_AllSixteenMatrixPermissionsAreAllowed(
        KejiPermission permission)
    {
        AssertAllowed(_service.Authorize(User(KejiRoles.Member), permission));
    }

    [Theory]
    [MemberData(nameof(ReadonlyPermissions))]
    public void Readonly_SinglePermission_AllThirteenMatrixPermissionsAreAllowed(
        KejiPermission permission)
    {
        AssertAllowed(_service.Authorize(User(KejiRoles.Readonly), permission));
    }

    [Theory]
    [MemberData(nameof(WritePermissions))]
    public void Readonly_EveryWritePermission_IsDeniedAsReadonlyWrite(
        KejiPermission permission)
    {
        AssertDenied(
            _service.Authorize(User(KejiRoles.Readonly), permission),
            KejiPermissionCatalog.IsAdminOnlyPermission(permission)
                ? KejiAuthorizationFailureReason.AdminRequired
                : KejiAuthorizationFailureReason.ReadonlyWriteDenied);
    }

    [Theory]
    [MemberData(nameof(WritePermissions))]
    public void Readonly_MultiplePermissionApi_EveryWritePermissionIsDeniedAsReadonlyWrite(
        KejiPermission permission)
    {
        AssertDenied(
            _service.AuthorizeAll(
                User(KejiRoles.Readonly),
                [KejiPermission.AccountSelfRead, permission]),
            KejiPermissionCatalog.IsAdminOnlyPermission(permission)
                ? KejiAuthorizationFailureReason.AdminRequired
                : KejiAuthorizationFailureReason.ReadonlyWriteDenied);
    }

    [Theory]
    [InlineData(KejiPermission.SettingsWrite)]
    [InlineData(KejiPermission.ModelManage)]
    [InlineData(KejiPermission.McpManage)]
    [InlineData(KejiPermission.DatabaseManage)]
    [InlineData(KejiPermission.SkillsManage)]
    [InlineData(KejiPermission.SystemManage)]
    [InlineData(KejiPermission.AdminUsers)]
    [InlineData(KejiPermission.AdminConversations)]
    [InlineData(KejiPermission.AuditRead)]
    public void Member_EveryAdminOnlyPermission_IsDeniedAsAdminRequired(
        KejiPermission permission)
    {
        AssertDenied(
            _service.Authorize(User(KejiRoles.Member), permission),
            KejiAuthorizationFailureReason.AdminRequired);
    }

    [Fact]
    public void NullUser_SinglePermission_IsDeniedAsUnauthenticated()
    {
        AssertDenied(
            _service.Authorize(null!, KejiPermission.AccountSelfRead),
            KejiAuthorizationFailureReason.Unauthenticated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Admin")]
    [InlineData("MEMBER")]
    [InlineData("Readonly")]
    [InlineData("unknown")]
    public void InvalidRole_SinglePermission_IsDeniedAsUnknownRole(string? role)
    {
        AssertDenied(
            _service.Authorize(User(role!), KejiPermission.AccountSelfRead),
            KejiAuthorizationFailureReason.UnknownRole);
    }

    [Fact]
    public void InvalidEnum_SinglePermission_IsDeniedAsInvalidMetadata()
    {
        AssertDenied(
            _service.Authorize(User(KejiRoles.Admin), (KejiPermission)int.MaxValue),
            KejiAuthorizationFailureReason.InvalidPermissionMetadata);
    }

    [Fact]
    public void MatrixDenial_SinglePermission_IsDeniedAsPermissionDenied()
    {
        var service = new KejiAuthorizationService(new DenyAllPermissionMatrix());

        AssertDenied(
            service.Authorize(User(KejiRoles.Member), KejiPermission.AccountSelfRead),
            KejiAuthorizationFailureReason.PermissionDenied);
    }

    [Fact]
    public void MultiplePermissions_Admin_AllRequirementsUseAndSemantics()
    {
        AssertAllowed(_service.AuthorizeAll(
            User(KejiRoles.Admin),
            [KejiPermission.AccountSelfRead, KejiPermission.FileRead, KejiPermission.AdminUsers]));
    }

    [Fact]
    public void MultiplePermissions_Member_AllAllowedRequirementsSucceed()
    {
        AssertAllowed(_service.AuthorizeAll(
            User(KejiRoles.Member),
            [KejiPermission.AccountSelfRead, KejiPermission.FileRead, KejiPermission.FileWrite]));
    }

    [Fact]
    public void MultiplePermissions_OneDeniedRequirementDeniesWholeRequest()
    {
        AssertDenied(
            _service.AuthorizeAll(
                User(KejiRoles.Member),
                [KejiPermission.AccountSelfRead, KejiPermission.AdminUsers]),
            KejiAuthorizationFailureReason.AdminRequired);
    }

    [Fact]
    public void MultiplePermissions_ReadonlyWriteRequirementDeniesWholeRequest()
    {
        AssertDenied(
            _service.AuthorizeAll(
                User(KejiRoles.Readonly),
                [KejiPermission.FileRead, KejiPermission.FileWrite]),
            KejiAuthorizationFailureReason.ReadonlyWriteDenied);
    }

    [Fact]
    public void MultiplePermissions_AdminOnlyDenialHasPriorityOverReadonlyWrite()
    {
        AssertDenied(
            _service.AuthorizeAll(
                User(KejiRoles.Readonly),
                [KejiPermission.AdminConversations, KejiPermission.FileWrite]),
            KejiAuthorizationFailureReason.AdminRequired);
    }

    [Theory]
    [InlineData(KejiPermission.AdminUsers)]
    [InlineData(KejiPermission.AdminConversations)]
    public void Readonly_AdminOnlyPermission_IsDeniedAsAdminRequired(
        KejiPermission permission)
    {
        AssertDenied(
            _service.Authorize(User(KejiRoles.Readonly), permission),
            KejiAuthorizationFailureReason.AdminRequired);
    }

    [Fact]
    public void Readonly_FileWritePermission_IsDeniedAsReadonlyWrite()
    {
        AssertDenied(
            _service.Authorize(User(KejiRoles.Readonly), KejiPermission.FileWrite),
            KejiAuthorizationFailureReason.ReadonlyWriteDenied);
    }

    [Fact]
    public void Readonly_FileWriteAndAdminOnlyPermissions_AreDeniedAsAdminRequired()
    {
        AssertDenied(
            _service.AuthorizeAll(
                User(KejiRoles.Readonly),
                [KejiPermission.FileWrite, KejiPermission.AdminUsers]),
            KejiAuthorizationFailureReason.AdminRequired);
    }

    [Fact]
    public void NullUser_MultiplePermissions_IsDeniedAsUnauthenticated()
    {
        AssertDenied(
            _service.AuthorizeAll(null!, [KejiPermission.AccountSelfRead]),
            KejiAuthorizationFailureReason.Unauthenticated);
    }

    [Fact]
    public void NullPermissionList_IsDeniedAsMissingMetadata()
    {
        AssertDenied(
            _service.AuthorizeAll(User(KejiRoles.Admin), null!),
            KejiAuthorizationFailureReason.MissingPermissionMetadata);
    }

    [Fact]
    public void EmptyPermissionList_IsDeniedAsMissingMetadata()
    {
        AssertDenied(
            _service.AuthorizeAll(User(KejiRoles.Admin), []),
            KejiAuthorizationFailureReason.MissingPermissionMetadata);
    }

    [Fact]
    public void InvalidEnumInPermissionList_IsDeniedAsInvalidMetadata()
    {
        AssertDenied(
            _service.AuthorizeAll(
                User(KejiRoles.Admin),
                [KejiPermission.AccountSelfRead, (KejiPermission)int.MaxValue]),
            KejiAuthorizationFailureReason.InvalidPermissionMetadata);
    }

    [Fact]
    public void UnknownRole_MultiplePermissions_IsDeniedAsUnknownRole()
    {
        AssertDenied(
            _service.AuthorizeAll(User("Admin"), [KejiPermission.AccountSelfRead]),
            KejiAuthorizationFailureReason.UnknownRole);
    }

    private static CurrentUser User(string role) =>
        new("user-id", "test-user", role, "Test User", KejiAuthenticationKind.Jwt);

    private static TheoryData<KejiPermission> CreateTheoryData(
        IEnumerable<KejiPermission> permissions)
    {
        var data = new TheoryData<KejiPermission>();
        foreach (var permission in permissions)
            data.Add(permission);
        return data;
    }

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

    private sealed class DenyAllPermissionMatrix : IKejiRolePermissionMatrix
    {
        public IReadOnlySet<KejiPermission> GetEffectivePermissions(string? role) =>
            new HashSet<KejiPermission>();

        public bool HasPermission(string? role, KejiPermission permission) => false;
    }
}
