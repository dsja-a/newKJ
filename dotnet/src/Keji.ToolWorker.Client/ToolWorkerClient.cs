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
        var process = new Process
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

        WorkerJobObject? job = null;
        WorkerFrameReader? reader = null;
        WorkerFrameWriter? writer = null;

        try
        {
            process.Start();

            job = new WorkerJobObject($"Keji_ToolWorker_{process.Id}");
            job.AssignProcess(process.Handle);
            job.ApplyLimits(_options.MaxProcessMemoryBytes, _options.MaxActiveProcesses);

            reader = new WorkerFrameReader(process.StandardOutput.BaseStream, _options.MaxFrameSize);
            writer = new WorkerFrameWriter(process.StandardInput.BaseStream, _options.MaxFrameSize);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_options.Timeout);

            try
            {
                await writer.WriteRequestAsync(request, cts.Token).ConfigureAwait(false);
                process.StandardInput.Close();

                var response = await reader.ReadResponseAsync(cts.Token).ConfigureAwait(false);
                if (response is null)
                {
                    CleanupProcess(process);
                    return MakeError(request, ToolWorkerErrorCode.WorkerUnavailable, "Worker returned null response");
                }

                if (WaitForExitAndCheck(process))
                {
                    return response;
                }

                return MakeError(request, ToolWorkerErrorCode.WorkerUnavailable, "Worker did not exit cleanly");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                CleanupProcess(process);
                return MakeError(request, ToolWorkerErrorCode.Cancelled, "Worker cancelled");
            }
            catch (OperationCanceledException)
            {
                CleanupProcess(process);
                return MakeError(request, ToolWorkerErrorCode.Timeout, "Worker timed out");
            }
        }
        catch (Exception)
        {
            CleanupProcess(process);
            return MakeError(request, ToolWorkerErrorCode.WorkerUnavailable, "Worker unavailable");
        }
        finally
        {
            writer?.Dispose();
            reader?.Dispose();
            job?.Dispose();
            process.Dispose();
        }
    }

    private static bool WaitForExitAndCheck(Process process)
    {
        try
        {
            if (process.WaitForExit(5000) && process.HasExited)
            {
                return process.ExitCode == 0;
            }
        }
        catch
        {
        }
        return false;
    }

    private static void CleanupProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }

        try
        {
            process.WaitForExit(5000);
        }
        catch
        {
        }
    }

    private static ToolWorkerResponse MakeError(ToolWorkerRequest request, ToolWorkerErrorCode code, string message)
    {
        return new ToolWorkerResponse
        {
            ProtocolVersion = Protocol.ProtocolVersion.String,
            RequestId = request.RequestId,
            ToolName = request.ToolName,
            ContractVersion = request.ContractVersion,
            ErrorCode = (int)code,
            ErrorMessage = message
        };
    }

    public void Dispose()
    {
    }
}
