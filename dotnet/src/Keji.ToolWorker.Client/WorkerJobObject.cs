using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Keji.ToolWorker.Client;

public sealed class WorkerJobObject : IDisposable
{
    private readonly nint _jobHandle;
    private bool _disposed;

    public WorkerJobObject(string name)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _jobHandle = nint.Zero;
            return;
        }

        _jobHandle = CreateJobObjectW(nint.Zero, name);
        if (_jobHandle == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to create Job Object");
    }

    public void ApplyLimits(long? maxProcessMemoryBytes = null, int? maxActiveProcesses = null)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || _jobHandle == nint.Zero)
            return;

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = 0
            }
        };

        if (maxProcessMemoryBytes.HasValue)
        {
            info.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_JOB_MEMORY;
            info.JobMemoryLimit = new UIntPtr((ulong)maxProcessMemoryBytes.Value);
        }

        if (maxActiveProcesses.HasValue)
        {
            info.BasicLimitInformation.LimitFlags |= JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
            info.BasicLimitInformation.ActiveProcessLimit = maxActiveProcesses.Value;
        }

        if (info.BasicLimitInformation.LimitFlags != 0)
        {
            int size = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                if (!SetInformationJobObject(_jobHandle, JobObjectInfoType.JobObjectExtendedLimitInformation, ptr, (uint)size))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to set Job Object limits");
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
    }

    public void AssignProcess(nint processHandle)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || _jobHandle == nint.Zero)
            return;

        if (!AssignProcessToJobObject(_jobHandle, processHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to assign process to Job Object");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_jobHandle != nint.Zero)
                CloseHandle(_jobHandle);
            _disposed = true;
        }
    }

    private const int JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x00000008;
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
