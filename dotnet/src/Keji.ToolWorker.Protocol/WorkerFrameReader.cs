using System.Text.Json;

namespace Keji.ToolWorker.Protocol;

public sealed class WorkerFrameReader : IWorkerFrameReader, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Stream _stream;
    private readonly int _maxFrameSize;

    public WorkerFrameReader(Stream stream, int maxFrameSize = 1_048_576)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _maxFrameSize = maxFrameSize;
    }

    public async ValueTask<ToolWorkerRequest?> ReadRequestAsync(CancellationToken ct = default)
    {
        var payload = await ReadFrameAsync(ct).ConfigureAwait(false);
        if (payload is null) return null;
        return JsonSerializer.Deserialize<ToolWorkerRequest>(payload, JsonOptions);
    }

    public async ValueTask<ToolWorkerResponse?> ReadResponseAsync(CancellationToken ct = default)
    {
        var payload = await ReadFrameAsync(ct).ConfigureAwait(false);
        if (payload is null) return null;
        return JsonSerializer.Deserialize<ToolWorkerResponse>(payload, JsonOptions);
    }

    private async ValueTask<byte[]?> ReadFrameAsync(CancellationToken ct)
    {
        var header = new byte[4];
        int offset = 0;
        while (offset < 4)
        {
            int read = await _stream.ReadAsync(header.AsMemory(offset, 4 - offset), ct).ConfigureAwait(false);
            if (read == 0) return offset == 0 ? null : throw new EndOfStreamException("Incomplete frame header");
            offset += read;
        }

        if (BitConverter.IsLittleEndian)
            Array.Reverse(header);

        int length = BitConverter.ToInt32(header, 0);
        if (length < 0 || length > _maxFrameSize)
            throw new InvalidOperationException($"Frame size {length} exceeds limit of {_maxFrameSize}");

        var payload = new byte[length];
        offset = 0;
        while (offset < length)
        {
            int read = await _stream.ReadAsync(payload.AsMemory(offset, length - offset), ct).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException("Incomplete frame payload");
            offset += read;
        }

        return payload;
    }

    public void Dispose() { }
}
