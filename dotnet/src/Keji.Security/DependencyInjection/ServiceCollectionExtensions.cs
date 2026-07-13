using Keji.Security.Authentication;
using Keji.Security.Auth;
using Keji.Security.Authorization;
using Keji.Security.Middleware;
using Keji.Security.Options;
using Keji.Security.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKejiSecurityFoundation(
        this IServiceCollection services,
        KejiSecurityOptions options)
    {
        services.AddSingleton(options);

        services.AddSingleton(new PasswordHashOptions { WorkFactor = 12 });
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();

        if (options.Enabled && (options.AuthMode == KejiAuthMode.UserOnly || options.AuthMode == KejiAuthMode.Both))
        {
            services.AddSingleton<IAccessTokenService, JwtAccessTokenService>();
        }
        else
        {
            services.AddSingleton<IAccessTokenService, UnavailableAccessTokenService>();
        }

        services.AddSingleton<IRequestAuthenticator, KejiRequestAuthenticator>();

        services.AddScoped<ICurrentUserAccessor, HttpCurrentUserAccessor>();

        services.AddScoped<IKejiLoginService, KejiLoginService>();

        services.AddSingleton<IBootstrapAdminService, BootstrapAdminService>();

        services.AddSingleton<IKejiRolePermissionMatrix, KejiRolePermissionMatrix>();
        services.AddSingleton<IKejiAuthorizationService, KejiAuthorizationService>();
        services.AddSingleton<IKejiToolAuthorizationService, KejiToolAuthorizationService>();
        services.AddSingleton<IKejiRolePermissionHintProvider, KejiRolePermissionHintProvider>();

        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
