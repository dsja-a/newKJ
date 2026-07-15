using System.Text.Json;
using Keji.ToolWorker.Client;
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

    private static ToolWorkerRequest MakeRequest(string toolName, string inputJson)
    {
        return new ToolWorkerRequest
        {
            ProtocolVersion = "1.0",
            RequestId = Guid.NewGuid().ToString("N"),
            DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(10).ToString("O"),
            ToolName = toolName,
            ContractVersion = "1",
            InputJson = inputJson
        };
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
        Assert.Equal("calculator", response.ToolName);
        Assert.Equal("1", response.ContractVersion);
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

    [Fact]
    public async Task Process_ContractOnlyTool_ReturnsError()
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
            RequestId = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee",
            DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"),
            ToolName = "read_file",
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
    public async Task Client_Calculator_Success()
    {
        var exePath = GetWorkerPath();
        var options = new WorkerProcessOptions
        {
            ExecutablePath = exePath,
            Timeout = TimeSpan.FromSeconds(10)
        };

        using var client = new ToolWorkerClient(options);
        var request = MakeRequest("calculator", "{\"expr\":\"2+2\"}");

        var response = await client.ExecuteAsync(request);

        Assert.Equal(0, response.ErrorCode);
        Assert.Equal("1.0", response.ProtocolVersion);
        Assert.Equal(request.RequestId, response.RequestId);
        Assert.Equal("calculator", response.ToolName);
        Assert.Equal("1", response.ContractVersion);
        Assert.NotNull(response.ResultJson);
    }

    [Fact]
    public async Task Client_Timeout_ReturnsTimeout()
    {
        var exePath = GetWorkerPath();
        var options = new WorkerProcessOptions
        {
            ExecutablePath = exePath,
            Timeout = TimeSpan.FromMilliseconds(1)
        };

        using var client = new ToolWorkerClient(options);
        var request = MakeRequest("calculator", "{\"expr\":\"2+2\"}");

        var response = await client.ExecuteAsync(request);

        Assert.Equal((int)ToolWorkerErrorCode.Timeout, response.ErrorCode);
        Assert.Equal(request.RequestId, response.RequestId);
    }

    [Fact]
    public async Task Client_Cancellation_ReturnsCancelled()
    {
        var exePath = GetWorkerPath();
        var options = new WorkerProcessOptions
        {
            ExecutablePath = exePath,
            Timeout = TimeSpan.FromSeconds(10)
        };

        using var client = new ToolWorkerClient(options);
        var request = MakeRequest("calculator", "{\"expr\":\"2+2\"}");

        using var cts = new CancellationTokenSource();

        var task = client.ExecuteAsync(request, cts.Token);

        // Wait briefly for the process to start
        await Task.Delay(100);
        cts.Cancel();

        var response = await task;

        Assert.Equal((int)ToolWorkerErrorCode.Cancelled, response.ErrorCode);
        Assert.Equal(request.RequestId, response.RequestId);
    }

    [Fact]
    public async Task Client_Cancellation_PreCancelled_ReturnsCancelled()
    {
        var exePath = GetWorkerPath();
        var options = new WorkerProcessOptions
        {
            ExecutablePath = exePath,
            Timeout = TimeSpan.FromSeconds(10)
        };

        using var client = new ToolWorkerClient(options);
        var request = MakeRequest("calculator", "{\"expr\":\"2+2\"}");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var response = await client.ExecuteAsync(request, cts.Token);

        Assert.Equal((int)ToolWorkerErrorCode.Cancelled, response.ErrorCode);
    }

    [Fact]
    public async Task Client_ConsecutiveCalls_BothSucceed()
    {
        var exePath = GetWorkerPath();
        var options = new WorkerProcessOptions
        {
            ExecutablePath = exePath,
            Timeout = TimeSpan.FromSeconds(10)
        };

        using var client = new ToolWorkerClient(options);

        // First call - creates new process
        var resp1 = await client.ExecuteAsync(MakeRequest("calculator", "{\"expr\":\"2+2\"}"));
        Assert.Equal(0, resp1.ErrorCode);
        Assert.Equal("calculator", resp1.ToolName);

        // Second call - creates a different process (worker exited after first request)
        var resp2 = await client.ExecuteAsync(MakeRequest("calculator", "{\"expr\":\"3+3\"}"));
        Assert.Equal(0, resp2.ErrorCode);
        Assert.Equal("calculator", resp2.ToolName);

        // Different RequestIds prove separate requests
        Assert.NotEqual(resp1.RequestId, resp2.RequestId);
    }

    [Fact]
    public async Task Process_Client_PerRequest_UniquePids()
    {
        var exePath = GetWorkerPath();

        var pid1 = await ExecuteAndGetPidAsync(exePath, "calculator", "{\"expr\":\"2+2\"}");
        var pid2 = await ExecuteAndGetPidAsync(exePath, "calculator", "{\"expr\":\"3+3\"}");

        Assert.NotEqual(pid1, pid2);
    }

    [Fact]
    public async Task Client_InvalidRequestId_Rejected()
    {
        var exePath = GetWorkerPath();
        var options = new WorkerProcessOptions
        {
            ExecutablePath = exePath,
            Timeout = TimeSpan.FromSeconds(10)
        };

        using var client = new ToolWorkerClient(options);
        var request = MakeRequest("calculator", "{\"expr\":\"2+2\"}") with { RequestId = "short" };

        var response = await client.ExecuteAsync(request);

        Assert.NotEqual(0, response.ErrorCode);
    }

    [Fact]
    public async Task Client_ExpiredDeadline_Rejected()
    {
        var exePath = GetWorkerPath();
        var options = new WorkerProcessOptions
        {
            ExecutablePath = exePath,
            Timeout = TimeSpan.FromSeconds(10)
        };

        using var client = new ToolWorkerClient(options);
        var request = MakeRequest("calculator", "{\"expr\":\"2+2\"}") with
        {
            DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O")
        };

        var response = await client.ExecuteAsync(request);

        Assert.NotEqual(0, response.ErrorCode);
    }

    [Fact]
    public async Task Client_UnknownTool_Rejected()
    {
        var exePath = GetWorkerPath();
        var options = new WorkerProcessOptions
        {
            ExecutablePath = exePath,
            Timeout = TimeSpan.FromSeconds(10)
        };

        using var client = new ToolWorkerClient(options);
        var request = MakeRequest("nonexistent_tool", "{}");

        var response = await client.ExecuteAsync(request);

        Assert.NotEqual(0, response.ErrorCode);
    }

    private static async Task<int> ExecuteAndGetPidAsync(string exePath, string toolName, string inputJson)
    {
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
        var pid = process.Id;

        var reader = new WorkerFrameReader(process.StandardOutput.BaseStream);
        var writer = new WorkerFrameWriter(process.StandardInput.BaseStream);

        var request = new ToolWorkerRequest
        {
            ProtocolVersion = "1.0",
            RequestId = Guid.NewGuid().ToString("N"),
            DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"),
            ToolName = toolName,
            ContractVersion = "1",
            InputJson = inputJson
        };

        await writer.WriteRequestAsync(request);
        process.StandardInput.Close();

        await reader.ReadResponseAsync();
        process.WaitForExit(5000);

        return pid;
    }
}