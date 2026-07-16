using System.Collections.Concurrent;

namespace Keji.Agent;

public interface IKejiAgentSessionGate
{
    IDisposable? TryEnter(string ownerUserId, string conversationId);
}

public sealed class KejiAgentSessionGate : IKejiAgentSessionGate
{
    private readonly ConcurrentDictionary<string, byte> _active = new(StringComparer.Ordinal);

    public IDisposable? TryEnter(string ownerUserId, string conversationId)
    {
        var key = string.Concat(ownerUserId, "\n", conversationId);
        return _active.TryAdd(key, 0) ? new Lease(_active, key) : null;
    }

    private sealed class Lease(ConcurrentDictionary<string, byte> active, string key) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                active.TryRemove(key, out _);
        }
    }
}
