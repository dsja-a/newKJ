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
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<IKejiSmartQuerySecretResolver, EnvironmentKejiSmartQuerySecretResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKejiSmartQueryDialectCompiler, MySqlSmartQueryDialect>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKejiSmartQueryDialectCompiler, PostgreSqlSmartQueryDialect>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKejiSmartQueryExecutor, MySqlSmartQueryExecutor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IKejiSmartQueryExecutor, PostgreSqlSmartQueryExecutor>());
        services.TryAddScoped<IKejiSmartQuery, KejiSmartQueryService>();
        return services;
    }
}
