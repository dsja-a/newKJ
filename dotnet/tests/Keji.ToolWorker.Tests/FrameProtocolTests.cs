using System.Text;
using System.Text.Json;
using Keji.ToolWorker.Protocol;

namespace Keji.ToolWorker.Tests;

public class FrameProtocolTests
{
    [Fact]
    public async Task WriteThenReadRequest_Roundtrips()
    {
        using var mem = new MemoryStream();
        var writer = new WorkerFrameWriter(mem);
        var reader = new WorkerFrameReader(mem);

        var original = new ToolWorkerRequest
        {
            ProtocolVersion = "1.0",
            RequestId = new string('a', 32),
            DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"),
            ToolName = "calculator",
            ContractVersion = "1",
            InputJson = "{\"expr\":\"2+2\"}"
        };

        await writer.WriteRequestAsync(original);
        mem.Position = 0;
        var result = await reader.ReadRequestAsync();

        Assert.NotNull(result);
        Assert.Equal(original.ProtocolVersion, result.ProtocolVersion);
        Assert.Equal(original.RequestId, result.RequestId);
        Assert.Equal(original.ToolName, result.ToolName);
        Assert.Equal(original.ContractVersion, result.ContractVersion);
        Assert.Equal(original.InputJson, result.InputJson);
    }

    [Fact]
    public async Task WriteThenReadResponse_Roundtrips()
    {
        using var mem = new MemoryStream();
        var writer = new WorkerFrameWriter(mem);
        var reader = new WorkerFrameReader(mem);

        var original = new ToolWorkerResponse
        {
            ProtocolVersion = "1.0",
            RequestId = new string('b', 32),
            ErrorCode = 0,
            ResultJson = "{\"result\":4.0}"
        };

        await writer.WriteResponseAsync(original);
        mem.Position = 0;
        var result = await reader.ReadResponseAsync();

        Assert.NotNull(result);
        Assert.Equal(original.ProtocolVersion, result.ProtocolVersion);
        Assert.Equal(original.RequestId, result.RequestId);
        Assert.Equal(original.ErrorCode, result.ErrorCode);
        Assert.Equal(original.ResultJson, result.ResultJson);
    }

    [Fact]
    public async Task Writer_ThrowsOnOversizedPayload()
    {
        using var mem = new MemoryStream();
        var writer = new WorkerFrameWriter(mem, maxFrameSize: 100);

        var request = new ToolWorkerRequest
        {
            ProtocolVersion = "1.0",
            RequestId = new string('c', 32),
            ToolName = "calculator",
            InputJson = new string('x', 200)
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteRequestAsync(request).AsTask());
    }

    [Fact]
    public async Task Reader_ThrowsOnOversizedFrame()
    {
        using var mem = new MemoryStream();
        var writer = new WorkerFrameWriter(mem, maxFrameSize: 1_048_576);
        var reader = new WorkerFrameReader(mem, maxFrameSize: 10);

        var response = new ToolWorkerResponse
        {
            ProtocolVersion = "1.0",
            RequestId = new string('d', 32),
            ResultJson = "{\"result\":4.0}"
        };

        await writer.WriteResponseAsync(response);
        mem.Position = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadResponseAsync().AsTask());
    }

    [Fact]
    public async Task EmptyStream_ReturnsNull()
    {
        using var mem = new MemoryStream();
        var reader = new WorkerFrameReader(mem);

        var result = await reader.ReadRequestAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task ProtocolVersion_Values()
    {
        Assert.Equal("1.0", ProtocolVersion.String);
    }
}
