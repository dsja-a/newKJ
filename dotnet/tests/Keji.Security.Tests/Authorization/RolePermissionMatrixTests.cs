using Keji.Security.Authorization;

namespace Keji.Security.Tests.Authorization;

public sealed class RolePermissionMatrixTests
{
    private readonly KejiRolePermissionMatrix _matrix = new();

    [Fact]
    public void Admin_HasExactCompleteTwentyFivePermissionSet()
    {
        AuthorizationExpectations.AssertExactPermissions(
            AuthorizationExpectations.AllPermissions,
            _matrix.GetEffectivePermissions(KejiRoles.Admin));
    }

    [Fact]
    public void Member_HasExactCompleteSixteenPermissionSet()
    {
        AuthorizationExpectations.AssertExactPermissions(
            AuthorizationExpectations.MemberPermissions,
            _matrix.GetEffectivePermissions(KejiRoles.Member));
    }

    [Fact]
    public void Readonly_HasExactCompleteThirteenPermissionSet()
    {
        AuthorizationExpectations.AssertExactPermissions(
            AuthorizationExpectations.ReadonlyPermissions,
            _matrix.GetEffectivePermissions(KejiRoles.Readonly));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("Admin")]
    [InlineData("MEMBER")]
    [InlineData("Readonly")]
    [InlineData("admin ")]
    [InlineData(" member")]
    [InlineData("unknown")]
    public void InvalidOrNonCanonicalRole_HasNoPermissions(string? role)
    {
        var permissions = _matrix.GetEffectivePermissions(role!);

        Assert.Empty(permissions);
        Assert.False(_matrix.HasPermission(role!, KejiPermission.AccountSelfRead));
        Assert.False(KejiRoles.IsValid(role!));
    }

    [Fact]
    public void PublishedRolePermissionSet_CannotMutateGlobalMatrix()
    {
        var published = _matrix.GetEffectivePermissions(KejiRoles.Admin);

        AssertMutationRejected(published, KejiPermission.AccountSelfRead);
        AuthorizationExpectations.AssertExactPermissions(
            AuthorizationExpectations.AllPermissions,
            _matrix.GetEffectivePermissions(KejiRoles.Admin));
    }

    [Fact]
    public void PublishedPermissionCatalog_CannotBeCastToMutableSet()
    {
        AssertMutationRejected(KejiPermissionCatalog.All, KejiPermission.AccountSelfRead);
        AuthorizationExpectations.AssertExactPermissions(
            AuthorizationExpectations.AllPermissions,
            KejiPermissionCatalog.All);
    }

    [Fact]
    public void PublishedRoleCatalog_IsExactAndCannotBeCastToMutableSet()
    {
        var expected = new[] { KejiRoles.Admin, KejiRoles.Member, KejiRoles.Readonly };

        Assert.Equal(expected.OrderBy(static role => role),
            KejiRoles.All.OrderBy(static role => role));
        AssertMutationRejected(KejiRoles.All, KejiRoles.Admin);
        Assert.Equal(expected.OrderBy(static role => role),
            KejiRoles.All.OrderBy(static role => role));
    }

    private static void AssertMutationRejected<T>(IReadOnlySet<T> published, T item)
    {
        Assert.False(published is HashSet<T>);

        var exception = Record.Exception(() => ((ICollection<T>)published).Remove(item));

        Assert.NotNull(exception);
        Assert.Contains(exception.GetType(),
            new[] { typeof(InvalidCastException), typeof(NotSupportedException) });
    }
}
