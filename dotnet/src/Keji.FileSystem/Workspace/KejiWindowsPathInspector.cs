using System.ComponentModel;
using Microsoft.Win32.SafeHandles;

namespace Keji.FileSystem.Workspace;

public sealed class KejiWindowsPathInspector : IKejiWindowsPathInspector
{
    private const uint ProbeShare =
        KejiWindowsNativeMethods.FileShareRead |
        KejiWindowsNativeMethods.FileShareWrite |
        KejiWindowsNativeMethods.FileShareDelete;

    private readonly KejiWorkspaceOptions _options;

    public KejiWindowsPathInspector(KejiWorkspaceOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public KejiPathInspectionResult Inspect(
        KejiWorkspacePathCandidateResult candidate,
        KejiFileSystemOperation operation)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!candidate.IsValid || candidate.RootPath is null || candidate.FullPath is null)
            return KejiPathInspectionResult.Unsafe(
                KejiWorkspaceAccessFailureReason.PathInspectionFailed);
        if (!Enum.IsDefined(operation) || operation == KejiFileSystemOperation.Unknown)
            return KejiPathInspectionResult.Unsafe(
                KejiWorkspaceAccessFailureReason.InvalidOperation);
        if (!OperatingSystem.IsWindows())
            return KejiPathInspectionResult.Unsafe(
                KejiWorkspaceAccessFailureReason.PlatformNotSupported);

        var handles = new List<SafeFileHandle>();
        try
        {
            return InspectCore(candidate, operation, handles);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or ArgumentException or
            NotSupportedException or Win32Exception or OverflowException)
        {
            DisposeHandles(handles);
            return KejiPathInspectionResult.Unsafe(
                KejiWorkspaceAccessFailureReason.PathInspectionFailed);
        }
    }

    public bool Revalidate(KejiPathInspectionResult inspection)
    {
        ArgumentNullException.ThrowIfNull(inspection);
        return inspection.IsSafe &&
            inspection.Lease is not null &&
            inspection.Lease.Revalidate();
    }

    private KejiPathInspectionResult InspectCore(
        KejiWorkspacePathCandidateResult candidate,
        KejiFileSystemOperation operation,
        List<SafeFileHandle> handles)
    {
        var workspaceRoot = _options.WorkspaceRoot;
        if (!KejiWorkspaceRootLayout.IsAllowedScopeRoot(workspaceRoot, candidate.RootPath!) ||
            !KejiWorkspaceRootLayout.IsWithinRoot(candidate.RootPath!, candidate.FullPath!))
            return UnsafeAndDispose(
                handles,
                KejiWorkspaceAccessFailureReason.PathInspectionFailed);

        var rootProbe = OpenProbe(workspaceRoot, out var rootError);
        if (rootProbe.IsInvalid)
        {
            rootProbe.Dispose();
            return UnsafeAndDispose(
                handles,
                IsMissing(rootError)
                    ? KejiWorkspaceAccessFailureReason.WorkspaceRootMissing
                    : KejiWorkspaceAccessFailureReason.PathInspectionFailed);
        }

        using (rootProbe)
        {
            var rootProbeInformation = KejiWindowsNativeMethods.GetInformation(rootProbe);
            if (IsReparsePoint(rootProbeInformation))
                return UnsafeAndDispose(
                    handles,
                    KejiWorkspaceAccessFailureReason.ReparsePointDenied);
            if (!IsDirectory(rootProbeInformation))
                return UnsafeAndDispose(
                    handles,
                    KejiWorkspaceAccessFailureReason.ParentNotDirectory);

            var rootProbeFinalPath = KejiWindowsNativeMethods.GetFinalPath(rootProbe);
            if (!KejiWindowsPathRules.IsLocalAbsolutePath(rootProbeFinalPath))
                return UnsafeAndDispose(
                    handles,
                    KejiWorkspaceAccessFailureReason.FinalPathEscapesRoot);
            if (!string.Equals(
                rootProbeFinalPath,
                workspaceRoot,
                StringComparison.OrdinalIgnoreCase))
                return UnsafeAndDispose(
                    handles,
                    KejiWorkspaceAccessFailureReason.FinalPathEscapesRoot);

            var rootHandle = OpenPinned(
                workspaceRoot,
                rootProbeInformation,
                rootProbeFinalPath,
                isDirectory: true,
                isFinal: false,
                operation,
                out var rootInformation,
                out var finalWorkspaceRoot);
            if (rootHandle is null)
                return UnsafeAndDispose(
                    handles,
                    KejiWorkspaceAccessFailureReason.RaceDetected);
            handles.Add(rootHandle);

            return InspectBelowRoot(
                candidate,
                operation,
                handles,
                rootHandle,
                rootInformation,
                finalWorkspaceRoot);
        }
    }

    private KejiPathInspectionResult InspectBelowRoot(
        KejiWorkspacePathCandidateResult candidate,
        KejiFileSystemOperation operation,
        List<SafeFileHandle> handles,
        SafeFileHandle rootHandle,
        KejiWindowsFileInformation rootInformation,
        string finalWorkspaceRoot)
    {
        var workspaceRoot = _options.WorkspaceRoot;
        var relative = Path.GetRelativePath(workspaceRoot, candidate.FullPath!);
        var segments = relative == "."
            ? Array.Empty<string>()
            : relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.None);
        var scopeRelative = Path.GetRelativePath(workspaceRoot, candidate.RootPath!);
        var scopeSegments = scopeRelative.Split(
            Path.DirectorySeparatorChar,
            StringSplitOptions.None);
        var scopeEndIndex = scopeSegments.Length - 1;

        var currentPath = workspaceRoot;
        var lastHandle = rootHandle;
        var lastInformation = rootInformation;
        var lastFinalPath = finalWorkspaceRoot;
        string? finalScopeRoot = null;

        if (segments.Length == 0)
            return UnsafeAndDispose(
                handles,
                KejiWorkspaceAccessFailureReason.PathInspectionFailed);

        for (var index = 0; index < segments.Length; index++)
        {
            currentPath = Path.Combine(currentPath, segments[index]);
            var probe = OpenProbe(currentPath, out var error);
            if (probe.IsInvalid)
            {
                probe.Dispose();
                if (!IsMissing(error))
                    return UnsafeAndDispose(
                        handles,
                        KejiWorkspaceAccessFailureReason.PathInspectionFailed);
                if (!IsDirectory(lastInformation))
                    return UnsafeAndDispose(
                        handles,
                        KejiWorkspaceAccessFailureReason.ParentNotDirectory);

                var remainingSegments = segments[index..];
                var finalMissingPath = Path.Combine(
                    lastFinalPath,
                    Path.Combine(remainingSegments));
                if (finalScopeRoot is null && scopeEndIndex >= index)
                {
                    var remainingScopeSegments = segments[index..(scopeEndIndex + 1)];
                    finalScopeRoot = Path.Combine(
                        lastFinalPath,
                        Path.Combine(remainingScopeSegments));
                }
                if (finalScopeRoot is null ||
                    !KejiWorkspaceRootLayout.IsWithinRoot(finalScopeRoot, finalMissingPath))
                    return UnsafeAndDispose(
                        handles,
                        KejiWorkspaceAccessFailureReason.FinalPathEscapesRoot);

                var identity = new KejiPathInspectionIdentity(
                    lastInformation.VolumeSerialNumber,
                    lastInformation.FileIndex,
                    false,
                    false,
                    lastFinalPath,
                    finalMissingPath);
                var missingLease = new KejiWindowsPathLease(
                    handles,
                    null,
                    lastHandle,
                    identity,
                    candidate.FullPath!,
                    false,
                    false,
                    remainingSegments.Length);
                return KejiPathInspectionResult.Safe(missingLease);
            }

            using (probe)
            {
                var probeInformation = KejiWindowsNativeMethods.GetInformation(probe);
                if (IsReparsePoint(probeInformation))
                    return UnsafeAndDispose(
                        handles,
                        KejiWorkspaceAccessFailureReason.ReparsePointDenied);
                if (!IsDirectory(probeInformation) && probeInformation.NumberOfLinks > 1)
                    return UnsafeAndDispose(
                        handles,
                        KejiWorkspaceAccessFailureReason.HardLinkDenied);

                var probeFinalPath = KejiWindowsNativeMethods.GetFinalPath(probe);
                if (!KejiWorkspaceRootLayout.IsWithinRoot(finalWorkspaceRoot, probeFinalPath))
                    return UnsafeAndDispose(
                        handles,
                        KejiWorkspaceAccessFailureReason.FinalPathEscapesRoot);
                if (index == scopeEndIndex)
                    finalScopeRoot = probeFinalPath;
                if (finalScopeRoot is not null &&
                    !KejiWorkspaceRootLayout.IsWithinRoot(finalScopeRoot, probeFinalPath))
                    return UnsafeAndDispose(
                        handles,
                        KejiWorkspaceAccessFailureReason.FinalPathEscapesRoot);

                var isFinal = index == segments.Length - 1;
                if (!isFinal && !IsDirectory(probeInformation))
                    return UnsafeAndDispose(
                        handles,
                        KejiWorkspaceAccessFailureReason.ParentNotDirectory);

                var pinnedHandle = OpenPinned(
                    currentPath,
                    probeInformation,
                    probeFinalPath,
                    IsDirectory(probeInformation),
                    isFinal,
                    operation,
                    out var pinnedInformation,
                    out var pinnedFinalPath);
                if (pinnedHandle is null)
                    return UnsafeAndDispose(
                        handles,
                        KejiWorkspaceAccessFailureReason.RaceDetected);

                handles.Add(pinnedHandle);
                lastHandle = pinnedHandle;
                lastInformation = pinnedInformation;
                lastFinalPath = pinnedFinalPath;

                if (isFinal)
                    return CreateExistingResult(
                        handles,
                        operation,
                        pinnedHandle,
                        pinnedInformation,
                        pinnedFinalPath,
                        currentPath);
            }
        }

        return UnsafeAndDispose(
            handles,
            KejiWorkspaceAccessFailureReason.PathInspectionFailed);
    }

    private static SafeFileHandle? OpenPinned(
        string path,
        KejiWindowsFileInformation expectedInformation,
        string expectedFinalPath,
        bool isDirectory,
        bool isFinal,
        KejiFileSystemOperation operation,
        out KejiWindowsFileInformation pinnedInformation,
        out string pinnedFinalPath)
    {
        var desiredAccess = KejiWindowsNativeMethods.FileReadAttributes;
        if (isDirectory)
        {
            desiredAccess |= KejiWindowsNativeMethods.FileTraverse;
            if (isFinal && operation == KejiFileSystemOperation.Enumerate)
                desiredAccess |= KejiWindowsNativeMethods.FileListDirectory;
        }
        else if (isFinal)
            desiredAccess |= AccessFor(operation);

        var handle = KejiWindowsNativeMethods.OpenExistingPath(
            path,
            desiredAccess,
            KejiWindowsNativeMethods.FileShareRead,
            out _);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            pinnedInformation = default;
            pinnedFinalPath = string.Empty;
            return null;
        }

        try
        {
            pinnedInformation = KejiWindowsNativeMethods.GetInformation(handle);
            pinnedFinalPath = KejiWindowsNativeMethods.GetFinalPath(handle);
            if (IsReparsePoint(pinnedInformation) ||
                pinnedInformation.VolumeSerialNumber != expectedInformation.VolumeSerialNumber ||
                pinnedInformation.FileIndex != expectedInformation.FileIndex ||
                pinnedInformation.Attributes != expectedInformation.Attributes ||
                pinnedInformation.NumberOfLinks != expectedInformation.NumberOfLinks ||
                !string.Equals(
                    pinnedFinalPath,
                    expectedFinalPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                handle.Dispose();
                return null;
            }

            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static KejiPathInspectionResult CreateExistingResult(
        List<SafeFileHandle> handles,
        KejiFileSystemOperation operation,
        SafeFileHandle pinnedHandle,
        KejiWindowsFileInformation information,
        string finalPath,
        string targetPath)
    {
        var isDirectory = IsDirectory(information);
        var identity = new KejiPathInspectionIdentity(
            information.VolumeSerialNumber,
            information.FileIndex,
            true,
            isDirectory,
            finalPath,
            finalPath);
        var operationHandle = !isDirectory && AccessFor(operation) != 0
            ? pinnedHandle
            : null;
        var lease = new KejiWindowsPathLease(
            handles,
            operationHandle,
            pinnedHandle,
            identity,
            targetPath,
            true,
            isDirectory,
            0);
        return KejiPathInspectionResult.Safe(lease);
    }

    private static SafeFileHandle OpenProbe(string path, out int error) =>
        KejiWindowsNativeMethods.OpenExistingPath(
            path,
            KejiWindowsNativeMethods.FileReadAttributes,
            ProbeShare,
            out error);

    private static uint AccessFor(KejiFileSystemOperation operation) => operation switch
    {
        KejiFileSystemOperation.Read => KejiWindowsNativeMethods.GenericRead,
        KejiFileSystemOperation.Write => KejiWindowsNativeMethods.GenericWrite,
        KejiFileSystemOperation.Delete => KejiWindowsNativeMethods.DeleteAccess,
        KejiFileSystemOperation.Enumerate => KejiWindowsNativeMethods.GenericRead,
        KejiFileSystemOperation.Create => 0,
        _ => 0,
    };

    private static bool IsMissing(int error) =>
        error is KejiWindowsNativeMethods.ErrorFileNotFound or
            KejiWindowsNativeMethods.ErrorPathNotFound;

    private static bool IsDirectory(KejiWindowsFileInformation information) =>
        information.Attributes.HasFlag(FileAttributes.Directory);

    private static bool IsReparsePoint(KejiWindowsFileInformation information) =>
        information.Attributes.HasFlag(FileAttributes.ReparsePoint);

    private static KejiPathInspectionResult UnsafeAndDispose(
        List<SafeFileHandle> handles,
        KejiWorkspaceAccessFailureReason reason)
    {
        DisposeHandles(handles);
        return KejiPathInspectionResult.Unsafe(reason);
    }

    private static void DisposeHandles(List<SafeFileHandle> handles)
    {
        for (var index = handles.Count - 1; index >= 0; index--)
            handles[index].Dispose();
    }
}
