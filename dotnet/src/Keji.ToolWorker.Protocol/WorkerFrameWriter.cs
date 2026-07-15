using System.Text;
using System.Text.Json;

namespace Keji.ToolWorker.Protocol;

public sealed class WorkerFrameWriter : IWorkerFrameWriter, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly Stream _stream;
    private readonly int _maxFrameSize;

    public WorkerFrameWriter(Stream stream, int maxFrameSize = 1_048_576)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _maxFrameSize = maxFrameSize;
    }

    public async ValueTask WriteRequestAsync(ToolWorkerRequest request, CancellationToken ct = default)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
        await WriteFrameAsync(json, ct).ConfigureAwait(false);
    }

    public async ValueTask WriteResponseAsync(ToolWorkerResponse response, CancellationToken ct = default)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
        await WriteFrameAsync(json, ct).ConfigureAwait(false);
    }

    private async ValueTask WriteFrameAsync(byte[] payload, CancellationToken ct)
    {
        if (payload.Length > _maxFrameSize)
            throw new InvalidOperationException($"Payload size {payload.Length} exceeds limit of {_maxFrameSize}");

        var lengthBytes = BitConverter.GetBytes(payload.Length);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBytes);

        await _stream.WriteAsync(lengthBytes, ct).ConfigureAwait(false);
        await _stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public void Dispose() { }
}
