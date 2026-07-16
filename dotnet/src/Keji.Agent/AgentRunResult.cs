namespace Keji.Agent;

public enum AgentRunStatus
{
    Completed = 1,
    InvalidRequest = 2,
    Unauthenticated = 3,
    ConversationNotFound = 4,
    ProviderNotFound = 5,
    ProviderFailed = 6,
    ToolFailed = 7,
    LimitExceeded = 8,
}

public sealed class AgentRunResult
{
    public AgentRunStatus Status { get; }
    public string Content { get; }
    public int Iterations { get; }
    public int ToolCalls { get; }
    public bool Success => Status == AgentRunStatus.Completed;

    private AgentRunResult(AgentRunStatus status, string content, int iterations, int toolCalls)
    {
        Status = status;
        Content = content;
        Iterations = iterations;
        ToolCalls = toolCalls;
    }

    public static AgentRunResult Completed(string content, int iterations, int toolCalls) =>
        new(AgentRunStatus.Completed, content, iterations, toolCalls);

    public static AgentRunResult Failed(AgentRunStatus status, int iterations = 0, int toolCalls = 0) =>
        new(status, string.Empty, iterations, toolCalls);
}
