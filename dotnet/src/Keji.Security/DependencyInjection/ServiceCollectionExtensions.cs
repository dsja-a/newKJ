using Keji.Security.Authentication;
using Keji.Security.Auth;
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

        services.AddSingleton<IAccessTokenService, JwtAccessTokenService>();

        services.AddSingleton<IRequestAuthenticator, KejiRequestAuthenticator>();

        services.AddScoped<ICurrentUserAccessor, HttpCurrentUserAccessor>();

        services.AddScoped<IKejiLoginService, KejiLoginService>();

        services.AddSingleton<IBootstrapAdminService, BootstrapAdminService>();

        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
