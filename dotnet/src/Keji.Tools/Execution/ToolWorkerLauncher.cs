using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Keji.ToolWorker.Protocol;
using Keji.Tools.Definitions;

namespace Keji.Tools.Execution;

public sealed class ToolWorkerLauncher : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

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
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(DefaultTimeout);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = WorkerExecutablePath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();

            var reader = new WorkerFrameReader(process.StandardOutput.BaseStream);
            var writer = new WorkerFrameWriter(process.StandardInput.BaseStream);

            var requestId = Guid.NewGuid().ToString("N");
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            var inputJson = JsonSerializer.Serialize(inputs, JsonOptions);

            var request = new ToolWorkerRequest
            {
                ProtocolVersion = ProtocolVersion.String,
                RequestId = requestId,
                DeadlineUtc = deadline.ToString("O"),
                ToolName = definition.Name.Value,
                ContractVersion = definition.ContractVersion.ToString(),
                InputJson = inputJson
            };

            await writer.WriteRequestAsync(request, cts.Token).ConfigureAwait(false);
            process.StandardInput.Close();

            var response = await reader.ReadResponseAsync(cts.Token).ConfigureAwait(false);
            sw.Stop();

            if (response is null)
                return ToolExecutionResult.Failed("Worker returned no response.", duration: sw.Elapsed);

            if (response.ErrorCode == 0)
                return ToolExecutionResult.Successful(response.ResultJson, sw.Elapsed);

            return ToolExecutionResult.Failed(
                response.ErrorMessage ?? "Tool execution failed.",
                $"WORKER_ERROR_{response.ErrorCode}",
                sw.Elapsed);
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
