using System.Text.Json;
using Keji.ToolWorker.Protocol;
using Keji.Tools.Execution;

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
    public async Task Launcher_Timeout_ReturnsTimeoutError()
    {
        var exePath = GetWorkerPath();

        var options = new ToolWorkerOptions(exePath, TimeSpan.FromMilliseconds(1));
        var launcher = new ToolWorkerLauncher(options);

        var definition = Keji.Tools.Catalog.BuiltInToolCatalog.All.First(d => d.Name.Value == "calculator");
        var inputs = new Dictionary<string, object?> { ["expr"] = "2+2" };

        var result = await launcher.ExecuteAsync(definition, inputs);

        // 1ms timeout is too short for process startup + IPC round-trip; always times out
        Assert.False(result.Success);
        Assert.Equal("TIMEOUT", result.ErrorCode);
    }

    [Fact]
    public async Task Launcher_Cancellation_ReturnsCancelledError()
    {
        var exePath = GetWorkerPath();

        var options = new ToolWorkerOptions(exePath);
        var launcher = new ToolWorkerLauncher(options);

        var definition = Keji.Tools.Catalog.BuiltInToolCatalog.All.First(d => d.Name.Value == "calculator");
        var inputs = new Dictionary<string, object?> { ["expr"] = "2+2" };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await launcher.ExecuteAsync(definition, inputs, cts.Token);

        Assert.False(result.Success);
        Assert.Equal("CANCELLED", result.ErrorCode);
    }

    [Fact]
    public async Task Process_PerRequest_UniquePids()
    {
        var exePath = GetWorkerPath();

        var pid1 = await ExecuteAndGetPidAsync(exePath, "calculator", "{\"expr\":\"2+2\"}");
        var pid2 = await ExecuteAndGetPidAsync(exePath, "calculator", "{\"expr\":\"3+3\"}");

        Assert.NotEqual(pid1, pid2);
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
