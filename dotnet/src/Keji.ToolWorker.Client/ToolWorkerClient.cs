using System.Diagnostics;
using System.Text;
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
    private Process? _process;
    private WorkerFrameReader? _reader;
    private WorkerFrameWriter? _writer;
    private bool _disposed;

    public ToolWorkerClient(WorkerProcessOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _exePath = WorkerPathValidator.Validate(options.ExecutablePath);
    }

    public async Task<ToolWorkerResponse> ExecuteAsync(ToolWorkerRequest request, CancellationToken ct = default)
    {
        await EnsureProcessAsync(ct).ConfigureAwait(false);

        await _writer!.WriteRequestAsync(request, ct).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_options.Timeout);

        try
        {
            var response = await _reader!.ReadResponseAsync(cts.Token).ConfigureAwait(false);
            return response ?? throw new InvalidOperationException("Worker returned null response");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            KillProcess();
            return new ToolWorkerResponse
            {
                ProtocolVersion = Protocol.ProtocolVersion.String,
                RequestId = request.RequestId,
                ErrorCode = (int)ToolWorkerErrorCode.Timeout,
                ErrorMessage = $"Worker timed out after {_options.Timeout.TotalSeconds}s"
            };
        }
    }

    private async Task EnsureProcessAsync(CancellationToken ct)
    {
        if (_process is not null && !_process.HasExited)
            return;

        var psi = new ProcessStartInfo
        {
            FileName = _exePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _process = new Process { StartInfo = psi };
        _process.Start();

        _reader = new WorkerFrameReader(_process.StandardOutput.BaseStream, _options.MaxFrameSize);
        _writer = new WorkerFrameWriter(_process.StandardInput.BaseStream, _options.MaxFrameSize);

        using var job = new WorkerJobObject($"Keji_ToolWorker_{_process.Id}");
        job.AssignProcess(_process.Handle);
        job.ApplyLimits(_options.MaxProcessMemoryBytes, _options.MaxActiveProcesses);

        var stderrTask = ReadStderrAsync(ct);
    }

    private async Task ReadStderrAsync(CancellationToken ct)
    {
        if (_process is null) return;
        try
        {
            var buffer = new char[_options.MaxStderrLength];
            int read = await _process.StandardError.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read > 0)
            {
                var text = new string(buffer, 0, read);
                await Console.Error.WriteLineAsync(text.AsMemory(), ct).ConfigureAwait(false);
            }
        }
        catch { }
    }

    private void KillProcess()
    {
        try { _process?.Kill(entireProcessTree: true); } catch { }
        try { _process?.WaitForExit(5000); } catch { }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            KillProcess();
            _process?.Dispose();
            _reader?.Dispose();
            _writer?.Dispose();
            _disposed = true;
        }
    }
}
