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
        services.AddScoped<IKejiAgentLoop>(provider => provider.GetRequiredService<IAgentLoop>() as IKejiAgentLoop
            ?? throw new InvalidOperationException("Agent loop does not support streaming."));
        services.AddSingleton<IKejiAgentSessionGate, KejiAgentSessionGate>();
        services.AddSingleton<KejiAgentSseAdapter>();
        return services;
    }
}
