using Keji.Agent;

namespace Microsoft.Extensions.DependencyInjection;

public static class KejiAgentServiceCollectionExtensions
{
    public static IServiceCollection AddKejiAgentLoop(
        this IServiceCollection services,
        AgentLoopOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(options ?? new AgentLoopOptions());
        services.AddScoped<IAgentLoop, AgentLoop>();
        return services;
    }
}
