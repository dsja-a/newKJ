namespace Keji.ToolWorker.Protocol;

public interface IWorkerFrameReader
{
    ValueTask<ToolWorkerRequest?> ReadRequestAsync(CancellationToken ct = default);
    ValueTask<ToolWorkerResponse?> ReadResponseAsync(CancellationToken ct = default);
}
