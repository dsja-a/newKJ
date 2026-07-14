using Keji.Auditing.Abstractions;
using Keji.Auditing.Services;
using Keji.Auditing.Sinks;

namespace Microsoft.Extensions.DependencyInjection;

public static class AuditingServiceCollectionExtensions
{
    public static IServiceCollection AddKejiAuditingFoundation(this IServiceCollection services)
    {
        services.AddSingleton<IKejiAuditSink, KejiDatabaseAuditSink>();
        services.AddScoped<IKejiAuditService, KejiAuditService>();
        services.AddSingleton<IKejiAuditCorrelationAccessor>(_ => new NullCorrelationAccessor());
        return services;
    }

    private sealed class NullCorrelationAccessor : IKejiAuditCorrelationAccessor
    {
        public string? CorrelationId => null;
    }
}
