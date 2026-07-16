namespace Keji.Agent;

public interface IAgentLoop
{
    Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default);
}
