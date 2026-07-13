using System.Reflection;
using Keji.Security.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace Keji.Security.Tests.Authorization;

public sealed class PermissionContractTests
{
    [Fact]
    public void PermissionType_IsEnum_WithExactTwentyFiveValues()
    {
        Assert.True(typeof(KejiPermission).IsEnum);
        Assert.Equal(AuthorizationExpectations.AllPermissions, Enum.GetValues<KejiPermission>());
    }

    [Fact]
    public void PermissionCatalog_All_IsExactCompleteSet()
    {
        AuthorizationExpectations.AssertExactPermissions(
            AuthorizationExpectations.AllPermissions,
            KejiPermissionCatalog.All);
    }

    [Fact]
    public void PermissionCatalog_WriteClassification_IsExactCompleteSet()
    {
        var actual = AuthorizationExpectations.AllPermissions
            .Where(KejiPermissionCatalog.IsWritePermission);

        AuthorizationExpectations.AssertExactPermissions(
            AuthorizationExpectations.WritePermissions,
            actual);
    }

    [Fact]
    public void PermissionCatalog_AdminOnlyClassification_IsExactCompleteSet()
    {
        var actual = AuthorizationExpectations.AllPermissions
            .Where(KejiPermissionCatalog.IsAdminOnlyPermission);

        AuthorizationExpectations.AssertExactPermissions(
            AuthorizationExpectations.AdminOnlyPermissions,
            actual);
    }

    [Fact]
    public void RequirePermissionAttribute_HasOnlyTypedEnumConstructor()
    {
        var constructors = typeof(KejiRequirePermissionAttribute)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public);

        var constructor = Assert.Single(constructors);
        var parameter = Assert.Single(constructor.GetParameters());
        Assert.Equal(typeof(KejiPermission), parameter.ParameterType);
    }

    [Fact]
    public void RequirePermissionAttribute_PreservesTypedPermission()
    {
        var attribute = new KejiRequirePermissionAttribute(KejiPermission.FileRead);

        Assert.Equal(KejiPermission.FileRead, attribute.Permission);
    }

    [Fact]
    public void RequirePermissionAttribute_AllowsMultipleInstances()
    {
        var usage = Assert.Single(typeof(KejiRequirePermissionAttribute)
            .GetCustomAttributes<AttributeUsageAttribute>());

        Assert.True(usage.AllowMultiple);
        Assert.Equal(AttributeTargets.Method | AttributeTargets.Class, usage.ValidOn);
    }

    [Fact]
    public void CustomAllowAnonymousAttribute_ImplementsStandardMarker()
    {
        var attribute = new KejiAllowAnonymousAttribute();

        Assert.IsAssignableFrom<IAllowAnonymous>(attribute);
    }

    [Fact]
    public void AuthorizationFailureReason_HasExactRequiredValues()
    {
        KejiAuthorizationFailureReason[] expected =
        [
            KejiAuthorizationFailureReason.None,
            KejiAuthorizationFailureReason.Unauthenticated,
            KejiAuthorizationFailureReason.UnknownRole,
            KejiAuthorizationFailureReason.MissingPermissionMetadata,
            KejiAuthorizationFailureReason.InvalidPermissionMetadata,
            KejiAuthorizationFailureReason.PermissionDenied,
            KejiAuthorizationFailureReason.AdminRequired,
            KejiAuthorizationFailureReason.ReadonlyWriteDenied,
            KejiAuthorizationFailureReason.UnknownTool,
            KejiAuthorizationFailureReason.InvalidToolDescriptor,
        ];

        Assert.Equal(expected, Enum.GetValues<KejiAuthorizationFailureReason>());
    }

    [Fact]
    public void AuthorizationDecision_Allow_HasNoFailureReason()
    {
        var decision = KejiAuthorizationDecision.Allow();

        Assert.True(decision.IsAllowed);
        Assert.Equal(KejiAuthorizationFailureReason.None, decision.FailureReason);
    }

    [Fact]
    public void AuthorizationDecision_Deny_PreservesFailureReason()
    {
        var decision = KejiAuthorizationDecision.Deny(
            KejiAuthorizationFailureReason.PermissionDenied);

        Assert.False(decision.IsAllowed);
        Assert.Equal(KejiAuthorizationFailureReason.PermissionDenied, decision.FailureReason);
    }
}
