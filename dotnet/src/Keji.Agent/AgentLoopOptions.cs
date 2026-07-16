namespace Keji.Agent;

public sealed class AgentLoopOptions
{
    public int MaxIterations { get; }
    public int MaxToolCalls { get; }
    public int MaxContextMessages { get; }
    public int MaxContextBytes { get; }
    public int MaxToolResultBytes { get; }
    public int MaxTotalToolResultBytes { get; }

    public AgentLoopOptions(
        int maxIterations = 8,
        int maxToolCalls = 16,
        int maxContextMessages = 64,
        int maxContextBytes = 512 * 1024,
        int maxToolResultBytes = 64 * 1024,
        int maxTotalToolResultBytes = 256 * 1024)
    {
        if (maxIterations is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(maxIterations));
        if (maxToolCalls is < 0 or > 32) throw new ArgumentOutOfRangeException(nameof(maxToolCalls));
        if (maxContextMessages is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maxContextMessages));
        if (maxContextBytes is < 1024 or > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxContextBytes));
        if (maxToolResultBytes is < 256 or > 64 * 1024) throw new ArgumentOutOfRangeException(nameof(maxToolResultBytes));
        if (maxTotalToolResultBytes < maxToolResultBytes || maxTotalToolResultBytes > 256 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxTotalToolResultBytes));

        MaxIterations = maxIterations;
        MaxToolCalls = maxToolCalls;
        MaxContextMessages = maxContextMessages;
        MaxContextBytes = maxContextBytes;
        MaxToolResultBytes = maxToolResultBytes;
        MaxTotalToolResultBytes = maxTotalToolResultBytes;
    }
}
