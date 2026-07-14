using Keji.Tools.Catalog;
using Keji.Tools.Registry;

namespace Microsoft.Extensions.DependencyInjection;

public static class KejiToolsServiceCollectionExtensions
{
    public static IServiceCollection AddKejiToolRegistry(this IServiceCollection services)
    {
        var builder = BuiltInToolCatalog.CreateBuilder();
        var registry = builder.Build();
        services.AddSingleton<IKejiToolRegistry>(registry);
        return services;
    }
}
