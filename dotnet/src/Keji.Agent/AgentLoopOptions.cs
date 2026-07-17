using System.Text;

namespace Keji.Agent;

public sealed class AgentLoopOptions
{
    public int MaxIterations { get; }
    public int MaxToolCalls { get; }
    public int MaxTotalToolCalls => MaxToolCalls;
    public int MaxContextMessages { get; }
    public int MaxContextBytes { get; }
    public int MaxToolResultBytes { get; }
    public int MaxTotalToolResultBytes { get; }
    public int MaxToolResultBytesPerCall => MaxToolResultBytes;
    public int MaxToolResultBytesTotal => MaxTotalToolResultBytes;
    public TimeSpan RunTimeout { get; }
    public int MaxRecoveryAttempts { get; }
    public int EventBufferCapacity { get; }
    public int MaxProviderEventsPerIteration { get; }
    public int MaxToolCallsPerIteration { get; }
    public int MaxToolArgumentBytesPerCall { get; }
    public int MaxToolArgumentBytesTotal { get; }
    public string SystemPrompt { get; }

    public AgentLoopOptions(
        int maxIterations = 8,
        int maxToolCalls = 16,
        int maxContextMessages = 64,
        int maxContextBytes = 512 * 1024,
        int maxToolResultBytes = 64 * 1024,
        int maxTotalToolResultBytes = 256 * 1024,
        TimeSpan? runTimeout = null,
        int maxRecoveryAttempts = 2,
        int eventBufferCapacity = 32,
        int maxProviderEventsPerIteration = 262144,
        int maxToolCallsPerIteration = 16,
        int maxToolArgumentBytesPerCall = 64 * 1024,
        int maxToolArgumentBytesTotal = 1024 * 1024,
        string systemPrompt = "You are Keji, a secure and helpful assistant.")
    {
        if (maxIterations is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(maxIterations));
        if (maxToolCalls is < 0 or > 64) throw new ArgumentOutOfRangeException(nameof(maxToolCalls));
        if (maxContextMessages is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(maxContextMessages));
        if (maxContextBytes is < 1024 or > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxContextBytes));
        if (maxToolResultBytes is < 256 or > 64 * 1024) throw new ArgumentOutOfRangeException(nameof(maxToolResultBytes));
        if (maxTotalToolResultBytes < maxToolResultBytes || maxTotalToolResultBytes > 256 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maxTotalToolResultBytes));
        var effectiveTimeout = runTimeout ?? TimeSpan.FromMinutes(2);
        if (effectiveTimeout < TimeSpan.FromSeconds(1) || effectiveTimeout > TimeSpan.FromMinutes(10))
            throw new ArgumentOutOfRangeException(nameof(runTimeout));
        if (maxRecoveryAttempts is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(maxRecoveryAttempts));
        if (eventBufferCapacity is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(eventBufferCapacity));
        if (maxProviderEventsPerIteration is < 1 or > 262144) throw new ArgumentOutOfRangeException(nameof(maxProviderEventsPerIteration));
        if (maxToolCallsPerIteration is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(maxToolCallsPerIteration));
        if (maxToolArgumentBytesPerCall is < 256 or > 256 * 1024) throw new ArgumentOutOfRangeException(nameof(maxToolArgumentBytesPerCall));
        if (maxToolArgumentBytesTotal < maxToolArgumentBytesPerCall || maxToolArgumentBytesTotal > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxToolArgumentBytesTotal));
        if (string.IsNullOrWhiteSpace(systemPrompt) || systemPrompt.Length > 4096 ||
            systemPrompt.Any(char.IsControl) || !IsStrictUtf8(systemPrompt))
            throw new ArgumentException("System prompt is invalid.", nameof(systemPrompt));

        MaxIterations = maxIterations;
        MaxToolCalls = maxToolCalls;
        MaxContextMessages = maxContextMessages;
        MaxContextBytes = maxContextBytes;
        MaxToolResultBytes = maxToolResultBytes;
        MaxTotalToolResultBytes = maxTotalToolResultBytes;
        RunTimeout = effectiveTimeout;
        MaxRecoveryAttempts = maxRecoveryAttempts;
        EventBufferCapacity = eventBufferCapacity;
        MaxProviderEventsPerIteration = maxProviderEventsPerIteration;
        MaxToolCallsPerIteration = maxToolCallsPerIteration;
        MaxToolArgumentBytesPerCall = maxToolArgumentBytesPerCall;
        MaxToolArgumentBytesTotal = maxToolArgumentBytesTotal;
        SystemPrompt = systemPrompt;
    }

    private static bool IsStrictUtf8(string value)
    {
        try
        {
            _ = new UTF8Encoding(false, true).GetByteCount(value);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }
}
