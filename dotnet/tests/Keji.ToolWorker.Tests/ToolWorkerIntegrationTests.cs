using System.Text.Json;
using Keji.ToolWorker.Protocol;

namespace Keji.ToolWorker.Tests;

public class ToolWorkerIntegrationTests
{
    private static string? _workerPath;

    private static string GetWorkerPath()
    {
        if (_workerPath is not null) return _workerPath;

        var baseDir = AppContext.BaseDirectory;
        var dir = baseDir;
        while (dir is not null)
        {
            var candidates = Directory.EnumerateFiles(dir, "Keji.ToolWorker.exe").ToList();
            if (candidates.Count > 0)
            {
                _workerPath = candidates[0];
                return _workerPath;
            }

            candidates = Directory.EnumerateFiles(dir, "Keji.ToolWorker").ToList();
            if (candidates.Count > 0)
            {
                _workerPath = candidates[0];
                return _workerPath;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new FileNotFoundException("Keji.ToolWorker.exe not found. Build the ToolWorker project first.");
    }

    [Fact]
    public async Task Process_Calculator_ReturnsResult()
    {
        var exePath = GetWorkerPath();

        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var reader = new WorkerFrameReader(process.StandardOutput.BaseStream);
        var writer = new WorkerFrameWriter(process.StandardInput.BaseStream);

        var request = new ToolWorkerRequest
        {
            ProtocolVersion = "1.0",
            RequestId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"),
            ToolName = "calculator",
            ContractVersion = "1",
            InputJson = "{\"expr\":\"2+2\"}"
        };

        await writer.WriteRequestAsync(request);
        process.StandardInput.Close();

        var response = await reader.ReadResponseAsync();
        process.WaitForExit(5000);

        Assert.NotNull(response);
        Assert.Equal(0, response.ErrorCode);
        Assert.NotNull(response.ResultJson);

        using var doc = JsonDocument.Parse(response.ResultJson);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("result", out var resultProp));
    }

    [Fact]
    public async Task Process_GetTime_ReturnsResult()
    {
        var exePath = GetWorkerPath();

        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var reader = new WorkerFrameReader(process.StandardOutput.BaseStream);
        var writer = new WorkerFrameWriter(process.StandardInput.BaseStream);

        var request = new ToolWorkerRequest
        {
            ProtocolVersion = "1.0",
            RequestId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"),
            ToolName = "get_time",
            ContractVersion = "1",
            InputJson = "{}"
        };

        await writer.WriteRequestAsync(request);
        process.StandardInput.Close();

        var response = await reader.ReadResponseAsync();
        process.WaitForExit(5000);

        Assert.NotNull(response);
        Assert.Equal(0, response.ErrorCode);
        Assert.NotNull(response.ResultJson);

        using var doc = JsonDocument.Parse(response.ResultJson);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("utc_iso8601", out _));
    }

    [Fact]
    public async Task Process_UnknownTool_ReturnsError()
    {
        var exePath = GetWorkerPath();

        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var reader = new WorkerFrameReader(process.StandardOutput.BaseStream);
        var writer = new WorkerFrameWriter(process.StandardInput.BaseStream);

        var request = new ToolWorkerRequest
        {
            ProtocolVersion = "1.0",
            RequestId = "cccccccccccccccccccccccccccccccc",
            DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"),
            ToolName = "nonexistent",
            ContractVersion = "1",
            InputJson = "{}"
        };

        await writer.WriteRequestAsync(request);
        process.StandardInput.Close();

        var response = await reader.ReadResponseAsync();
        process.WaitForExit(5000);

        Assert.NotNull(response);
        Assert.NotEqual(0, response.ErrorCode);
    }

    [Fact]
    public async Task Process_InvalidRequestId_ReturnsError()
    {
        var exePath = GetWorkerPath();

        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var reader = new WorkerFrameReader(process.StandardOutput.BaseStream);
        var writer = new WorkerFrameWriter(process.StandardInput.BaseStream);

        var request = new ToolWorkerRequest
        {
            ProtocolVersion = "1.0",
            RequestId = "short", 
            DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"),
            ToolName = "calculator",
            ContractVersion = "1",
            InputJson = "{\"expr\":\"2+2\"}"
        };

        await writer.WriteRequestAsync(request);
        process.StandardInput.Close();

        var response = await reader.ReadResponseAsync();
        process.WaitForExit(5000);

        Assert.NotNull(response);
        Assert.NotEqual(0, response.ErrorCode);
    }

    [Fact]
    public async Task Process_ExpiredDeadline_ReturnsError()
    {
        var exePath = GetWorkerPath();

        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var reader = new WorkerFrameReader(process.StandardOutput.BaseStream);
        var writer = new WorkerFrameWriter(process.StandardInput.BaseStream);

        var request = new ToolWorkerRequest
        {
            ProtocolVersion = "1.0",
            RequestId = "dddddddddddddddddddddddddddddddd",
            DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O"),
            ToolName = "calculator",
            ContractVersion = "1",
            InputJson = "{\"expr\":\"2+2\"}"
        };

        await writer.WriteRequestAsync(request);
        process.StandardInput.Close();

        var response = await reader.ReadResponseAsync();
        process.WaitForExit(5000);

        Assert.NotNull(response);
        Assert.NotEqual(0, response.ErrorCode);
    }
}
