using Keji.Tools.Catalog;
using Keji.Tools.Execution;
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

    public static IServiceCollection AddKejiToolExecutionPipeline(this IServiceCollection services, string workerExecutablePath)
    {
        services.AddSingleton(new ToolWorkerLauncher(workerExecutablePath));
        services.AddSingleton<IToolExecutionPipeline, ToolExecutionPipeline>();
        return services;
    }
}
