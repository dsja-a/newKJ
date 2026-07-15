using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Keji.Tools.Definitions;

namespace Keji.Tools.Execution;

public sealed class ToolWorkerLauncher : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public string WorkerExecutablePath { get; }

    public ToolWorkerLauncher(string workerExecutablePath)
    {
        WorkerExecutablePath = workerExecutablePath ?? throw new ArgumentNullException(nameof(workerExecutablePath));
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        KejiToolDefinition definition,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        using var job = new JobObject();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(DefaultTimeout);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = WorkerExecutablePath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
            job.AddProcess(process);

            var request = new WorkerProtocolMessage
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Tool = definition.Name.Value,
                Args = new Dictionary<string, object?>(inputs, StringComparer.Ordinal),
                ContractVersion = definition.ContractVersion
            };

            var requestJson = JsonSerializer.Serialize(request, JsonOptions);
            await process.StandardInput.WriteLineAsync(requestJson);
            process.StandardInput.Close();

            var responseJson = await process.StandardOutput.ReadLineAsync(cts.Token);
            sw.Stop();

            if (responseJson is null)
                return ToolExecutionResult.Failed("Worker returned no response.", duration: sw.Elapsed);

            var response = JsonSerializer.Deserialize<WorkerProtocolMessage>(responseJson, JsonOptions);
            if (response is null)
                return ToolExecutionResult.Failed("Worker returned invalid response.", duration: sw.Elapsed);

            if (response.Success)
                return ToolExecutionResult.Successful(response.Result, sw.Elapsed);

            return ToolExecutionResult.Failed(response.Error ?? "Tool execution failed.", response.ErrorCode, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            KillProcess(process);

            if (cancellationToken.IsCancellationRequested)
                return ToolExecutionResult.Failed("Tool execution was cancelled.", "CANCELLED", sw.Elapsed);

            return ToolExecutionResult.Failed("Tool execution timed out.", "TIMEOUT", sw.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            KillProcess(process);
            return ToolExecutionResult.Failed($"Worker process error: {ex.Message}", "WORKER_ERROR", sw.Elapsed);
        }
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    public void Dispose()
    {
    }
}
