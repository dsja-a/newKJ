using Microsoft.Win32.SafeHandles;

namespace Keji.FileSystem.Workspace;

internal sealed class KejiWindowsPathLease : IDisposable
{
    private readonly List<SafeFileHandle> _handles;
    private bool _disposed;

    internal KejiWindowsPathLease(
        List<SafeFileHandle> handles,
        SafeFileHandle? operationHandle,
        SafeFileHandle identityHandle,
        KejiPathInspectionIdentity identity,
        string targetPath,
        bool targetExists,
        bool targetIsDirectory,
        int missingSegmentCount)
    {
        _handles = handles;
        OperationHandle = operationHandle;
        IdentityHandle = identityHandle;
        Identity = identity;
        TargetPath = targetPath;
        TargetExists = targetExists;
        TargetIsDirectory = targetIsDirectory;
        MissingSegmentCount = missingSegmentCount;
    }

    internal SafeFileHandle? OperationHandle { get; }
    private SafeFileHandle IdentityHandle { get; }
    internal SafeFileHandle PinnedHandle => IdentityHandle;
    internal KejiPathInspectionIdentity Identity { get; }
    internal string TargetPath { get; }
    internal bool TargetExists { get; }
    internal bool TargetIsDirectory { get; }
    internal int MissingSegmentCount { get; }
    internal string ExpectedTargetFinalPath => Identity.TargetFinalPath;

    internal bool Revalidate()
    {
        if (_disposed)
            return false;

        try
        {
            var information = KejiWindowsNativeMethods.GetInformation(IdentityHandle);
            var finalPath = KejiWindowsNativeMethods.GetFinalPath(IdentityHandle);
            if (information.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                information.VolumeSerialNumber != Identity.VolumeSerialNumber ||
                information.FileIndex != Identity.FileIndex ||
                !string.Equals(
                    finalPath,
                    Identity.PinnedObjectFinalPath,
                    StringComparison.OrdinalIgnoreCase))
                return false;

            if (!TargetExists)
            {
                using var unexpectedTarget = KejiWindowsNativeMethods.OpenExistingPath(
                    TargetPath,
                    KejiWindowsNativeMethods.FileReadAttributes,
                    KejiWindowsNativeMethods.FileShareRead |
                        KejiWindowsNativeMethods.FileShareWrite |
                        KejiWindowsNativeMethods.FileShareDelete,
                    out var error);
                return unexpectedTarget.IsInvalid &&
                    (error is KejiWindowsNativeMethods.ErrorFileNotFound or
                        KejiWindowsNativeMethods.ErrorPathNotFound);
            }

            return TargetIsDirectory || information.NumberOfLinks == 1;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                System.ComponentModel.Win32Exception or ObjectDisposedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        for (var index = _handles.Count - 1; index >= 0; index--)
            _handles[index].Dispose();
    }
}

internal readonly record struct KejiPathInspectionIdentity(
    uint VolumeSerialNumber,
    ulong FileIndex,
    bool TargetExists,
    bool TargetIsDirectory,
    string PinnedObjectFinalPath,
    string TargetFinalPath);
