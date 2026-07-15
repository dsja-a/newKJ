using System.Diagnostics;
using Keji.ToolWorker.Protocol;

namespace Keji.ToolWorker.Client;

public interface IToolWorkerClient
{
    Task<ToolWorkerResponse> ExecuteAsync(ToolWorkerRequest request, CancellationToken ct = default);
}

public sealed class ToolWorkerClient : IToolWorkerClient, IDisposable
{
    private readonly WorkerProcessOptions _options;
    private readonly string _exePath;

    public ToolWorkerClient(WorkerProcessOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _exePath = WorkerPathValidator.Validate(options.ExecutablePath);
    }

    public async Task<ToolWorkerResponse> ExecuteAsync(ToolWorkerRequest request, CancellationToken ct = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _exePath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();

        using var job = new WorkerJobObject($"Keji_ToolWorker_{process.Id}");
        job.AssignProcess(process.Handle);
        job.ApplyLimits(_options.MaxProcessMemoryBytes, _options.MaxActiveProcesses);

        using var reader = new WorkerFrameReader(process.StandardOutput.BaseStream, _options.MaxFrameSize);
        using var writer = new WorkerFrameWriter(process.StandardInput.BaseStream, _options.MaxFrameSize);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_options.Timeout);

        try
        {
            await writer.WriteRequestAsync(request, cts.Token).ConfigureAwait(false);
            process.StandardInput.Close();

            var response = await reader.ReadResponseAsync(cts.Token).ConfigureAwait(false);

            try { process.WaitForExit(5000); } catch { }

            return response ?? throw new InvalidOperationException("Worker returned null response");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            KillProcess(process);
            try { process.WaitForExit(5000); } catch { }
            return new ToolWorkerResponse
            {
                ProtocolVersion = Protocol.ProtocolVersion.String,
                RequestId = request.RequestId,
                ErrorCode = (int)ToolWorkerErrorCode.Timeout,
                ErrorMessage = "Worker timed out"
            };
        }
    }

    private static void KillProcess(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
    }

    public void Dispose()
    {
    }
}
