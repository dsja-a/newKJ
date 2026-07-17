using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Keji.SmartQuery;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKejiSmartQuery(
        this IServiceCollection services,
        KejiSmartQueryOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(options ?? new KejiSmartQueryOptions());
        services.TryAddSingleton<KejiSmartQueryCompiler>();
        services.TryAddScoped<IKejiSmartQuery, KejiSmartQueryService>();
        return services;
    }
}
