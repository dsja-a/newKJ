using System.Reflection;
using Keji.Security.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Keji.Integration.Tests.Authorization;

public sealed class ProductionControllerMetadataTests
{
    [Fact]
    public void Every_Public_Http_Action_Has_Exactly_One_Authorization_Metadata_Mode()
    {
        var actions = typeof(Program).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
                .Select(method => new { Controller = type, Action = method }))
            .ToArray();

        Assert.NotEmpty(actions);

        foreach (var action in actions)
        {
            var metadata = action.Controller.GetCustomAttributes(inherit: true)
                .Concat(action.Action.GetCustomAttributes(inherit: true))
                .ToArray();
            var hasAnonymous = metadata.OfType<IAllowAnonymous>().Any();
            var permissions = metadata.OfType<KejiRequirePermissionAttribute>().ToArray();
            var displayName = $"{action.Controller.FullName}.{action.Action.Name}";

            Assert.True(
                hasAnonymous ^ permissions.Length > 0,
                $"{displayName} must declare either IAllowAnonymous or typed permission metadata, but not both.");

            Assert.All(permissions, attribute =>
                Assert.True(
                    Enum.IsDefined(attribute.Permission),
                    $"{displayName} contains undefined permission value {(int)attribute.Permission}."));
        }
    }
}
