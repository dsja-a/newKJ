namespace Keji.ToolWorker.Protocol;

public interface IWorkerFrameWriter
{
    ValueTask WriteRequestAsync(ToolWorkerRequest request, CancellationToken ct = default);
    ValueTask WriteResponseAsync(ToolWorkerResponse response, CancellationToken ct = default);
}
