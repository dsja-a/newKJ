using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Keji.FileSystem.Workspace;

internal static class KejiWindowsNativeMethods
{
    internal const int ErrorFileNotFound = 2;
    internal const int ErrorPathNotFound = 3;
    internal const int ErrorNoMoreFiles = 18;
    internal const int ErrorHandleEof = 38;
    internal const int StatusObjectNameCollision = unchecked((int)0xC0000035);

    internal const uint GenericRead = 0x80000000;
    internal const uint GenericWrite = 0x40000000;
    internal const uint DeleteAccess = 0x00010000;
    internal const uint FileReadAttributes = 0x00000080;
    internal const uint FileTraverse = 0x00000020;
    internal const uint FileListDirectory = 0x00000001;

    internal const uint FileShareRead = 0x00000001;
    internal const uint FileShareWrite = 0x00000002;
    internal const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const int FileDispositionInfo = 4;

    internal static SafeFileHandle OpenExistingPath(
        string path,
        uint desiredAccess,
        uint shareMode,
        out int error)
    {
        var handle = CreateFileW(
            ToExtendedPath(path),
            desiredAccess,
            shareMode,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            IntPtr.Zero);

        error = handle.IsInvalid ? Marshal.GetLastWin32Error() : 0;
        return handle;
    }

    internal static KejiWindowsFileInformation GetInformation(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var information))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return new(
            (FileAttributes)information.FileAttributes,
            information.VolumeSerialNumber,
            ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow,
            information.NumberOfLinks);
    }

    internal static string GetFinalPath(SafeFileHandle handle)
    {
        var capacity = 512;
        while (true)
        {
            var builder = new StringBuilder(capacity);
            var length = GetFinalPathNameByHandleW(handle, builder, (uint)builder.Capacity, 0);
            if (length == 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (length < builder.Capacity)
                return NormalizeFinalPath(builder.ToString());
            capacity = checked((int)length + 1);
        }
    }

    internal static SafeFileHandle CreateDirectoryRelative(
        SafeFileHandle parentDirectory,
        string name,
        out int status) =>
        CreateRelative(
            parentDirectory,
            name,
            FileReadAttributes | DeleteAccess | SynchronizeAccess,
            FileDirectoryFile,
            out status);

    internal static SafeFileHandle CreateFileRelative(
        SafeFileHandle parentDirectory,
        string name,
        out int status) =>
        CreateRelative(
            parentDirectory,
            name,
            GenericWrite | FileReadAttributes | DeleteAccess | SynchronizeAccess,
            FileNonDirectoryFile,
            out status);

    private static SafeFileHandle CreateRelative(
        SafeFileHandle parentDirectory,
        string name,
        uint desiredAccess,
        uint objectTypeOption,
        out int status)
    {
        ArgumentNullException.ThrowIfNull(parentDirectory);
        if (string.IsNullOrEmpty(name) ||
            name.IndexOfAny(new[] { '\\', '/' }) >= 0)
            throw new ArgumentException("A single directory name is required.", nameof(name));

        var nameBuffer = Marshal.StringToHGlobalUni(name);
        var unicodeStringPointer = IntPtr.Zero;
        var parentReferenceAdded = false;
        try
        {
            var nameByteLength = checked((ushort)(name.Length * sizeof(char)));
            var unicodeString = new UnicodeString
            {
                Length = nameByteLength,
                MaximumLength = checked((ushort)(nameByteLength + sizeof(char))),
                Buffer = nameBuffer,
            };
            unicodeStringPointer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
            Marshal.StructureToPtr(unicodeString, unicodeStringPointer, fDeleteOld: false);

            parentDirectory.DangerousAddRef(ref parentReferenceAdded);
            var attributes = new ObjectAttributes
            {
                Length = Marshal.SizeOf<ObjectAttributes>(),
                RootDirectory = parentDirectory.DangerousGetHandle(),
                ObjectName = unicodeStringPointer,
                Attributes = ObjectAttributeCaseInsensitive | ObjectAttributeDontReparse,
            };

            status = NtCreateFile(
                out var rawHandle,
                desiredAccess,
                ref attributes,
                out _,
                IntPtr.Zero,
                FileAttributeNormal,
                FileShareRead,
                FileCreate,
                objectTypeOption | FileSynchronousIoNonAlert | FileOpenReparsePoint,
                IntPtr.Zero,
                0);

            return status >= 0
                ? new SafeFileHandle(rawHandle, ownsHandle: true)
                : new SafeFileHandle(IntPtr.Zero, ownsHandle: true);
        }
        finally
        {
            if (parentReferenceAdded)
                parentDirectory.DangerousRelease();
            if (unicodeStringPointer != IntPtr.Zero)
                Marshal.FreeHGlobal(unicodeStringPointer);
            Marshal.FreeHGlobal(nameBuffer);
        }
    }

    internal static bool TryDeleteByHandle(SafeFileHandle handle, out int error)
    {
        var disposition = new FileDispositionInformation { DeleteFile = 1 };
        if (SetFileInformationByHandle(
            handle,
            FileDispositionInfo,
            ref disposition,
            (uint)Marshal.SizeOf<FileDispositionInformation>()))
        {
            error = 0;
            return true;
        }

        error = Marshal.GetLastWin32Error();
        return false;
    }

    internal static IReadOnlyList<string> EnumerateDirectoryNames(
        SafeFileHandle directoryHandle,
        int maxEntries,
        CancellationToken cancellationToken,
        out bool limitExceeded)
    {
        ArgumentNullException.ThrowIfNull(directoryHandle);
        ArgumentOutOfRangeException.ThrowIfNegative(maxEntries);

        const int bufferSize = 64 * 1024;
        const int fileNameLengthOffset = 60;
        const int fileNameOffset = 104;
        var buffer = Marshal.AllocHGlobal(bufferSize);
        var entries = new List<string>();
        var restart = true;
        limitExceeded = false;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var informationClass = restart
                    ? FileIdBothDirectoryRestartInfo
                    : FileIdBothDirectoryInfo;
                restart = false;

                if (!GetFileInformationByHandleEx(
                    directoryHandle,
                    informationClass,
                    buffer,
                    (uint)bufferSize))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error is ErrorNoMoreFiles or ErrorHandleEof)
                        break;
                    throw new Win32Exception(error);
                }

                var offset = 0;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (offset < 0 || offset > bufferSize - fileNameOffset)
                        throw new IOException("Invalid directory information returned by Windows.");

                    var nextOffset = unchecked((uint)Marshal.ReadInt32(buffer, offset));
                    var fileNameLength = Marshal.ReadInt32(
                        buffer,
                        offset + fileNameLengthOffset);
                    if (fileNameLength < 0 ||
                        (fileNameLength & 1) != 0 ||
                        fileNameLength > bufferSize - offset - fileNameOffset)
                        throw new IOException("Invalid directory information returned by Windows.");

                    var name = Marshal.PtrToStringUni(
                        IntPtr.Add(buffer, offset + fileNameOffset),
                        fileNameLength / sizeof(char));
                    if (!string.IsNullOrEmpty(name) && name is not "." and not "..")
                    {
                        if (entries.Count == maxEntries)
                        {
                            limitExceeded = true;
                            return entries;
                        }
                        entries.Add(name);
                    }

                    if (nextOffset == 0)
                        break;
                    if (nextOffset < fileNameOffset ||
                        (nextOffset & 7) != 0 ||
                        nextOffset > bufferSize - offset ||
                        fileNameOffset + fileNameLength > nextOffset)
                        throw new IOException("Invalid directory information returned by Windows.");
                    offset = checked(offset + (int)nextOffset);
                }
            }

            return entries;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string NormalizeFinalPath(string path)
    {
        const string devicePrefix = "\\\\?\\";
        const string uncPrefix = "\\\\?\\UNC\\";
        if (path.StartsWith(uncPrefix, StringComparison.OrdinalIgnoreCase))
            return "\\\\" + path[uncPrefix.Length..];
        return path.StartsWith(devicePrefix, StringComparison.Ordinal)
            ? path[devicePrefix.Length..]
            : path;
    }

    private static string ToExtendedPath(string path) =>
        path.StartsWith("\\\\?\\", StringComparison.Ordinal)
            ? path
            : "\\\\?\\" + path;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle file,
        out ByHandleFileInformation fileInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle file,
        int fileInformationClass,
        IntPtr fileInformation,
        uint bufferSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle file,
        StringBuilder filePath,
        uint filePathLength,
        uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle file,
        int fileInformationClass,
        ref FileDispositionInformation fileInformation,
        uint bufferSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        internal uint FileAttributes;
        internal FILETIME CreationTime;
        internal FILETIME LastAccessTime;
        internal FILETIME LastWriteTime;
        internal uint VolumeSerialNumber;
        internal uint FileSizeHigh;
        internal uint FileSizeLow;
        internal uint NumberOfLinks;
        internal uint FileIndexHigh;
        internal uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInformation
    {
        internal byte DeleteFile;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(
        out IntPtr fileHandle,
        uint desiredAccess,
        ref ObjectAttributes objectAttributes,
        out IoStatusBlock ioStatusBlock,
        IntPtr allocationSize,
        uint fileAttributes,
        uint shareAccess,
        uint createDisposition,
        uint createOptions,
        IntPtr eaBuffer,
        uint eaLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        internal ushort Length;
        internal ushort MaximumLength;
        internal IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        internal int Length;
        internal IntPtr RootDirectory;
        internal IntPtr ObjectName;
        internal uint Attributes;
        internal IntPtr SecurityDescriptor;
        internal IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatusBlock
    {
        internal IntPtr Status;
        internal UIntPtr Information;
    }

    private const uint SynchronizeAccess = 0x00100000;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileCreate = 2;
    private const uint FileDirectoryFile = 0x00000001;
    private const uint FileNonDirectoryFile = 0x00000040;
    private const uint FileSynchronousIoNonAlert = 0x00000020;
    private const uint FileOpenReparsePoint = 0x00200000;
    private const uint ObjectAttributeCaseInsensitive = 0x00000040;
    private const uint ObjectAttributeDontReparse = 0x00001000;
    private const int FileIdBothDirectoryInfo = 10;
    private const int FileIdBothDirectoryRestartInfo = 11;
}

internal readonly record struct KejiWindowsFileInformation(
    FileAttributes Attributes,
    uint VolumeSerialNumber,
    ulong FileIndex,
    uint NumberOfLinks);
