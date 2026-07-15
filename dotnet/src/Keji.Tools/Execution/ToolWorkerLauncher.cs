using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Keji.ToolWorker.Protocol;
using Keji.Tools.Definitions;

namespace Keji.Tools.Execution;

public sealed class ToolWorkerLauncher : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ToolWorkerOptions _options;

    public string WorkerExecutablePath => _options.ExecutablePath;

    public ToolWorkerLauncher(ToolWorkerOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public ToolWorkerLauncher(string executablePath)
    {
        if (executablePath is null)
            throw new ArgumentNullException(nameof(executablePath));
        _options = new ToolWorkerOptions(executablePath);
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        KejiToolDefinition definition,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _options.ExecutablePath,
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

            using var job = CreateJobObject(process);
            using var reader = new WorkerFrameReader(process.StandardOutput.BaseStream);
            using var writer = new WorkerFrameWriter(process.StandardInput.BaseStream);

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

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.DefaultTimeout);

            await writer.WriteRequestAsync(request, cts.Token).ConfigureAwait(false);
            process.StandardInput.Close();

            var response = await reader.ReadResponseAsync(cts.Token).ConfigureAwait(false);
            sw.Stop();

            try { process.WaitForExit(5000); } catch { }

            if (response is null)
                return ToolExecutionResult.Failed("Worker returned no response.", duration: sw.Elapsed);

            if (response.ProtocolVersion != ProtocolVersion.String)
                return ToolExecutionResult.Failed("Worker protocol version mismatch", "PROTOCOL_MISMATCH", sw.Elapsed);

            if (response.RequestId != requestId)
                return ToolExecutionResult.Failed("Worker request ID mismatch", "CORRELATION_MISMATCH", sw.Elapsed);

            if (response.ErrorCode == 0)
                return ToolExecutionResult.Successful(response.ResultJson, sw.Elapsed);

            if (response.ErrorCode == (int)ToolWorkerErrorCode.Timeout)
                return ToolExecutionResult.Failed("Tool execution timed out.", "TIMEOUT", sw.Elapsed);

            return ToolExecutionResult.Failed(
                "Worker execution failed.",
                $"WORKER_ERROR_{response.ErrorCode}",
                sw.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            KillProcess(process);
            return ToolExecutionResult.Failed("Tool execution was cancelled.", "CANCELLED", sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            KillProcess(process);
            return ToolExecutionResult.Failed("Tool execution timed out.", "TIMEOUT", sw.Elapsed);
        }
        catch (Exception)
        {
            sw.Stop();
            KillProcess(process);
            return ToolExecutionResult.Failed("Worker process error.", "WORKER_ERROR", sw.Elapsed);
        }
    }

    private static IDisposable CreateJobObject(Process process)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return NullDisposable.Instance;

        var name = $"Keji_ToolWorker_{process.Id}";
        var handle = CreateJobObjectW(nint.Zero, name);
        if (handle == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create Job Object");

        try
        {
            if (!AssignProcessToJobObject(handle, process.Handle))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to assign process to Job Object");

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                        | JOB_OBJECT_LIMIT_PROCESS_MEMORY
                        | JOB_OBJECT_LIMIT_JOB_MEMORY
                        | JOB_OBJECT_LIMIT_ACTIVE_PROCESS,
                    ActiveProcessLimit = 1
                },
                ProcessMemoryLimit = new UIntPtr(256 * 1024 * 1024),
                JobMemoryLimit = new UIntPtr(256 * 1024 * 1024)
            };

            int size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                if (!SetInformationJobObject(handle, JobObjectInfoType.JobObjectExtendedLimitInformation, ptr, (uint)size))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to set Job Object limits");
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            CloseHandle(handle);
            throw;
        }

        return new JobHandleDisposable(handle);
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

    private sealed class JobHandleDisposable(nint handle) : IDisposable
    {
        public void Dispose()
        {
            if (handle != nint.Zero)
                CloseHandle(handle);
        }
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();
        public void Dispose() { }
    }

    private const int JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    private const int JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008;
    private const int JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x00000100;
    private const int JOB_OBJECT_LIMIT_JOB_MEMORY = 0x00000200;

    private enum JobObjectInfoType
    {
        JobObjectExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public int LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public int ActiveProcessLimit;
        public long Affinity;
        public int PriorityClass;
        public int SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObjectW(nint lpJobAttributes, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(nint hJob, JobObjectInfoType infoType, nint lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);
}
