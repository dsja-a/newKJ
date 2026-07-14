using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Keji.FileSystem.Workspace;

public sealed class KejiWorkspaceFileSystem : IKejiWorkspaceFileSystem
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly IKejiWorkspaceAccessPolicy _policy;
    private readonly IKejiWindowsPathInspector _inspector;
    private readonly KejiWorkspaceFileSystemOptions _options;

    public KejiWorkspaceFileSystem(
        IKejiWorkspaceAccessPolicy policy,
        IKejiWindowsPathInspector inspector,
        KejiWorkspaceFileSystemOptions options)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _inspector = inspector ?? throw new ArgumentNullException(nameof(inspector));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<KejiWorkspaceOperationResult<bool>> ExistsAsync(
        KejiWorkspacePathRequest? path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (failure, inspection) = AuthorizeAndInspect(path, KejiFileSystemOperation.Read);
        using (inspection)
        {
            if (failure != KejiWorkspaceAccessFailureReason.None)
                return Task.FromResult(KejiWorkspaceOperationResult<bool>.Failure(failure));
            if (!Revalidate(inspection!))
                return Task.FromResult(RaceFailure<bool>());
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                KejiWorkspaceOperationResult<bool>.Success(inspection!.TargetExists));
        }
    }

    public async Task<KejiWorkspaceOperationResult<string>> ReadTextAsync(
        KejiWorkspacePathRequest? path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (failure, inspection) = AuthorizeAndInspect(path, KejiFileSystemOperation.Read);
        using (inspection)
        {
            if (failure != KejiWorkspaceAccessFailureReason.None)
                return KejiWorkspaceOperationResult<string>.Failure(failure);
            var targetFailure = RequireExistingFile(inspection!);
            if (targetFailure != KejiWorkspaceAccessFailureReason.None)
                return KejiWorkspaceOperationResult<string>.Failure(targetFailure);
            if (!Revalidate(inspection!))
                return RaceFailure<string>();
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var bytes = await ReadAllBytesAsync(
                    inspection!.Lease!.OperationHandle!,
                    cancellationToken).ConfigureAwait(false);
                return KejiWorkspaceOperationResult<string>.Success(
                    StrictUtf8.GetString(bytes));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (DecoderFallbackException)
            {
                return KejiWorkspaceOperationResult<string>.Failure(
                    KejiWorkspaceAccessFailureReason.InvalidContent);
            }
            catch (KejiContentTooLargeException)
            {
                return KejiWorkspaceOperationResult<string>.Failure(
                    KejiWorkspaceAccessFailureReason.ContentTooLarge);
            }
            catch (Exception exception) when (IsSafeFileSystemException(exception))
            {
                return KejiWorkspaceOperationResult<string>.Failure(
                    KejiWorkspaceAccessFailureReason.FileSystemError);
            }
        }
    }

    public async Task<KejiWorkspaceOperationResult<byte[]>> ReadBytesAsync(
        KejiWorkspacePathRequest? path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (failure, inspection) = AuthorizeAndInspect(path, KejiFileSystemOperation.Read);
        using (inspection)
        {
            if (failure != KejiWorkspaceAccessFailureReason.None)
                return KejiWorkspaceOperationResult<byte[]>.Failure(failure);
            var targetFailure = RequireExistingFile(inspection!);
            if (targetFailure != KejiWorkspaceAccessFailureReason.None)
                return KejiWorkspaceOperationResult<byte[]>.Failure(targetFailure);
            if (!Revalidate(inspection!))
                return RaceFailure<byte[]>();
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var bytes = await ReadAllBytesAsync(
                    inspection!.Lease!.OperationHandle!,
                    cancellationToken).ConfigureAwait(false);
                return KejiWorkspaceOperationResult<byte[]>.Success(bytes);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (KejiContentTooLargeException)
            {
                return KejiWorkspaceOperationResult<byte[]>.Failure(
                    KejiWorkspaceAccessFailureReason.ContentTooLarge);
            }
            catch (Exception exception) when (IsSafeFileSystemException(exception))
            {
                return KejiWorkspaceOperationResult<byte[]>.Failure(
                    KejiWorkspaceAccessFailureReason.FileSystemError);
            }
        }
    }

    public async Task<KejiWorkspaceOperationResult<bool>> WriteTextAsync(
        KejiWorkspacePathRequest? path,
        string? content,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (failure, inspection) = AuthorizeAndInspect(path, KejiFileSystemOperation.Write);
        using (inspection)
        {
            if (failure != KejiWorkspaceAccessFailureReason.None)
                return KejiWorkspaceOperationResult<bool>.Failure(failure);
            var targetFailure = RequireWritableTarget(inspection!);
            if (targetFailure != KejiWorkspaceAccessFailureReason.None)
                return KejiWorkspaceOperationResult<bool>.Failure(targetFailure);
            byte[]? bytes;
            try
            {
                bytes = content is null ? null : StrictUtf8.GetBytes(content);
            }
            catch (EncoderFallbackException)
            {
                return KejiWorkspaceOperationResult<bool>.Failure(
                    KejiWorkspaceAccessFailureReason.InvalidContent);
            }
            return await WriteToInspectedHandleAsync(
                inspection!,
                bytes,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<KejiWorkspaceOperationResult<bool>> WriteBytesAsync(
        KejiWorkspacePathRequest? path,
        byte[]? content,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return WriteBytesCoreAsync(path, content, cancellationToken);
    }

    public Task<KejiWorkspaceOperationResult<bool>> CreateDirectoryAsync(
        KejiWorkspacePathRequest? path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (failure, inspection) = AuthorizeAndInspect(path, KejiFileSystemOperation.Create);
        using (inspection)
        {
            if (failure != KejiWorkspaceAccessFailureReason.None)
                return Task.FromResult(KejiWorkspaceOperationResult<bool>.Failure(failure));
            if (inspection!.TargetExists)
                return Task.FromResult(KejiWorkspaceOperationResult<bool>.Failure(
                    KejiWorkspaceAccessFailureReason.TargetAlreadyExists));
            if (inspection.Lease!.MissingSegmentCount != 1)
                return Task.FromResult(KejiWorkspaceOperationResult<bool>.Failure(
                    KejiWorkspaceAccessFailureReason.TargetNotFound));
            if (!Revalidate(inspection))
                return Task.FromResult(RaceFailure<bool>());
            cancellationToken.ThrowIfCancellationRequested();

            SafeFileHandle? createdHandle = null;
            try
            {
                var targetName = Path.GetFileName(inspection.Lease.TargetPath);
                cancellationToken.ThrowIfCancellationRequested();
                createdHandle = KejiWindowsNativeMethods.CreateDirectoryRelative(
                    inspection.Lease.PinnedHandle,
                    targetName,
                    out var createStatus);
                if (createdHandle.IsInvalid)
                {
                    return Task.FromResult(KejiWorkspaceOperationResult<bool>.Failure(
                        createStatus == KejiWindowsNativeMethods.StatusObjectNameCollision
                            ? KejiWorkspaceAccessFailureReason.RaceDetected
                            : KejiWorkspaceAccessFailureReason.FileSystemError));
                }

                var createdInformation = KejiWindowsNativeMethods.GetInformation(createdHandle);
                var createdFinalPath = KejiWindowsNativeMethods.GetFinalPath(createdHandle);
                if (!createdInformation.Attributes.HasFlag(FileAttributes.Directory) ||
                    createdInformation.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                    !string.Equals(
                        createdFinalPath,
                        inspection.Lease.ExpectedTargetFinalPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _ = KejiWindowsNativeMethods.TryDeleteByHandle(createdHandle, out _);
                    return Task.FromResult(RaceFailure<bool>());
                }

                return Task.FromResult(KejiWorkspaceOperationResult<bool>.Success(true));
            }
            catch (OperationCanceledException)
            {
                if (createdHandle is { IsInvalid: false })
                    _ = KejiWindowsNativeMethods.TryDeleteByHandle(createdHandle, out _);
                throw;
            }
            catch (Exception exception) when (IsSafeFileSystemException(exception))
            {
                if (createdHandle is { IsInvalid: false })
                    _ = KejiWindowsNativeMethods.TryDeleteByHandle(createdHandle, out _);
                return Task.FromResult(KejiWorkspaceOperationResult<bool>.Failure(
                    KejiWorkspaceAccessFailureReason.FileSystemError));
            }
            finally
            {
                createdHandle?.Dispose();
            }
        }
    }

    public Task<KejiWorkspaceOperationResult<IReadOnlyList<string>>> EnumerateAsync(
        KejiWorkspacePathRequest? path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (failure, inspection) = AuthorizeAndInspect(path, KejiFileSystemOperation.Enumerate);
        using (inspection)
        {
            if (failure != KejiWorkspaceAccessFailureReason.None)
                return Task.FromResult(
                    KejiWorkspaceOperationResult<IReadOnlyList<string>>.Failure(failure));
            var targetFailure = RequireExistingDirectory(inspection!);
            if (targetFailure != KejiWorkspaceAccessFailureReason.None)
                return Task.FromResult(
                    KejiWorkspaceOperationResult<IReadOnlyList<string>>.Failure(targetFailure));
            if (!Revalidate(inspection!))
                return Task.FromResult(RaceFailure<IReadOnlyList<string>>());
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var entries = KejiWindowsNativeMethods.EnumerateDirectoryNames(
                    inspection!.Lease!.PinnedHandle,
                    _options.MaxEntries,
                    cancellationToken,
                    out var limitExceeded);
                if (limitExceeded)
                    return Task.FromResult(
                        KejiWorkspaceOperationResult<IReadOnlyList<string>>.Failure(
                            KejiWorkspaceAccessFailureReason.ContentTooLarge));

                if (!Revalidate(inspection))
                    return Task.FromResult(RaceFailure<IReadOnlyList<string>>());
                cancellationToken.ThrowIfCancellationRequested();

                var sortedEntries = entries
                    .OrderBy(static entry => entry, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static entry => entry, StringComparer.Ordinal)
                    .ToArray();
                return Task.FromResult(
                    KejiWorkspaceOperationResult<IReadOnlyList<string>>.Success(sortedEntries));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (IsSafeFileSystemException(exception))
            {
                return Task.FromResult(
                    KejiWorkspaceOperationResult<IReadOnlyList<string>>.Failure(
                        KejiWorkspaceAccessFailureReason.FileSystemError));
            }
        }
    }

    public Task<KejiWorkspaceOperationResult<bool>> DeleteFileAsync(
        KejiWorkspacePathRequest? path,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (failure, inspection) = AuthorizeAndInspect(path, KejiFileSystemOperation.Delete);
        using (inspection)
        {
            if (failure != KejiWorkspaceAccessFailureReason.None)
                return Task.FromResult(KejiWorkspaceOperationResult<bool>.Failure(failure));
            var targetFailure = RequireExistingFile(inspection!);
            if (targetFailure != KejiWorkspaceAccessFailureReason.None)
                return Task.FromResult(KejiWorkspaceOperationResult<bool>.Failure(targetFailure));
            if (!Revalidate(inspection!))
                return Task.FromResult(RaceFailure<bool>());
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(
                    KejiWindowsNativeMethods.TryDeleteByHandle(
                        inspection!.Lease!.OperationHandle!,
                        out _)
                        ? KejiWorkspaceOperationResult<bool>.Success(true)
                        : KejiWorkspaceOperationResult<bool>.Failure(
                            KejiWorkspaceAccessFailureReason.FileSystemError));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (IsSafeFileSystemException(exception))
            {
                return Task.FromResult(KejiWorkspaceOperationResult<bool>.Failure(
                    KejiWorkspaceAccessFailureReason.FileSystemError));
            }
        }
    }

    private async Task<KejiWorkspaceOperationResult<bool>> WriteBytesCoreAsync(
        KejiWorkspacePathRequest? path,
        byte[]? content,
        CancellationToken cancellationToken)
    {
        var (failure, inspection) = AuthorizeAndInspect(path, KejiFileSystemOperation.Write);
        using (inspection)
        {
            if (failure != KejiWorkspaceAccessFailureReason.None)
                return KejiWorkspaceOperationResult<bool>.Failure(failure);
            var targetFailure = RequireWritableTarget(inspection!);
            if (targetFailure != KejiWorkspaceAccessFailureReason.None)
                return KejiWorkspaceOperationResult<bool>.Failure(targetFailure);
            return await WriteToInspectedHandleAsync(
                inspection!,
                content,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<KejiWorkspaceOperationResult<bool>> WriteToInspectedHandleAsync(
        KejiPathInspectionResult inspection,
        byte[]? content,
        CancellationToken cancellationToken)
    {
        if (content is null)
            return KejiWorkspaceOperationResult<bool>.Failure(
                KejiWorkspaceAccessFailureReason.InvalidContent);
        if (content.Length > _options.MaxContentBytes)
            return KejiWorkspaceOperationResult<bool>.Failure(
                KejiWorkspaceAccessFailureReason.ContentTooLarge);
        if (!Revalidate(inspection))
            return RaceFailure<bool>();
        cancellationToken.ThrowIfCancellationRequested();

        SafeFileHandle? createdHandle = null;
        try
        {
            var lease = inspection.Lease!;
            var handle = lease.OperationHandle;
            if (!lease.TargetExists)
            {
                var targetName = Path.GetFileName(lease.TargetPath);
                cancellationToken.ThrowIfCancellationRequested();
                createdHandle = KejiWindowsNativeMethods.CreateFileRelative(
                    lease.PinnedHandle,
                    targetName,
                    out var createStatus);
                if (createdHandle.IsInvalid)
                {
                    return KejiWorkspaceOperationResult<bool>.Failure(
                        createStatus == KejiWindowsNativeMethods.StatusObjectNameCollision
                            ? KejiWorkspaceAccessFailureReason.RaceDetected
                            : KejiWorkspaceAccessFailureReason.FileSystemError);
                }

                var createdInformation = KejiWindowsNativeMethods.GetInformation(createdHandle);
                var createdFinalPath = KejiWindowsNativeMethods.GetFinalPath(createdHandle);
                if (createdInformation.Attributes.HasFlag(FileAttributes.Directory) ||
                    createdInformation.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                    createdInformation.NumberOfLinks != 1 ||
                    !string.Equals(
                        createdFinalPath,
                        lease.ExpectedTargetFinalPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _ = KejiWindowsNativeMethods.TryDeleteByHandle(createdHandle, out _);
                    return RaceFailure<bool>();
                }

                handle = createdHandle;
            }

            cancellationToken.ThrowIfCancellationRequested();
            RandomAccess.SetLength(handle!, content.Length);
            await RandomAccess.WriteAsync(
                handle!,
                content,
                0,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            RandomAccess.FlushToDisk(handle!);
            return KejiWorkspaceOperationResult<bool>.Success(true);
        }
        catch (OperationCanceledException)
        {
            if (createdHandle is { IsInvalid: false })
                _ = KejiWindowsNativeMethods.TryDeleteByHandle(createdHandle, out _);
            throw;
        }
        catch (Exception exception) when (IsSafeFileSystemException(exception))
        {
            if (createdHandle is { IsInvalid: false })
                _ = KejiWindowsNativeMethods.TryDeleteByHandle(createdHandle, out _);
            return KejiWorkspaceOperationResult<bool>.Failure(
                KejiWorkspaceAccessFailureReason.FileSystemError);
        }
        finally
        {
            createdHandle?.Dispose();
        }
    }

    private async Task<byte[]> ReadAllBytesAsync(
        SafeFileHandle handle,
        CancellationToken cancellationToken)
    {
        var length = RandomAccess.GetLength(handle);
        if (length > _options.MaxContentBytes || length > int.MaxValue)
            throw new KejiContentTooLargeException();

        var bytes = new byte[checked((int)length)];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = await RandomAccess.ReadAsync(
                handle,
                bytes.AsMemory(offset),
                offset,
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }

        return bytes;
    }

    private (KejiWorkspaceAccessFailureReason Failure, KejiPathInspectionResult? Inspection)
        AuthorizeAndInspect(
            KejiWorkspacePathRequest? path,
            KejiFileSystemOperation operation)
    {
        var decision = _policy.Authorize(new KejiWorkspaceAccessRequest(path, operation));
        if (!decision.IsAllowed)
            return (decision.FailureReason, null);
        if (decision.Candidate is null)
            return (KejiWorkspaceAccessFailureReason.PathInspectionFailed, null);

        KejiPathInspectionResult inspection;
        try
        {
            inspection = _inspector.Inspect(decision.Candidate, operation);
        }
        catch (Exception exception) when (IsSafeFileSystemException(exception))
        {
            return (KejiWorkspaceAccessFailureReason.PathInspectionFailed, null);
        }
        if (!inspection.IsSafe)
        {
            var reason = inspection.FailureReason;
            inspection.Dispose();
            return (reason, null);
        }
        if (inspection.Lease is null)
        {
            inspection.Dispose();
            return (KejiWorkspaceAccessFailureReason.PathInspectionFailed, null);
        }

        return (KejiWorkspaceAccessFailureReason.None, inspection);
    }

    private static KejiWorkspaceAccessFailureReason RequireExistingFile(
        KejiPathInspectionResult inspection)
    {
        if (!inspection.TargetExists)
            return KejiWorkspaceAccessFailureReason.TargetNotFound;
        return inspection.TargetIsDirectory
            ? KejiWorkspaceAccessFailureReason.TargetTypeMismatch
            : KejiWorkspaceAccessFailureReason.None;
    }

    private static KejiWorkspaceAccessFailureReason RequireExistingDirectory(
        KejiPathInspectionResult inspection)
    {
        if (!inspection.TargetExists)
            return KejiWorkspaceAccessFailureReason.TargetNotFound;
        return inspection.TargetIsDirectory
            ? KejiWorkspaceAccessFailureReason.None
            : KejiWorkspaceAccessFailureReason.TargetTypeMismatch;
    }

    private static KejiWorkspaceAccessFailureReason RequireWritableTarget(
        KejiPathInspectionResult inspection)
    {
        if (inspection.TargetExists)
            return inspection.TargetIsDirectory
                ? KejiWorkspaceAccessFailureReason.TargetTypeMismatch
                : KejiWorkspaceAccessFailureReason.None;
        return inspection.Lease!.MissingSegmentCount == 1
            ? KejiWorkspaceAccessFailureReason.None
            : KejiWorkspaceAccessFailureReason.TargetNotFound;
    }

    private bool Revalidate(KejiPathInspectionResult inspection)
    {
        try
        {
            return _inspector.Revalidate(inspection);
        }
        catch (Exception exception) when (IsSafeFileSystemException(exception))
        {
            return false;
        }
    }

    private static KejiWorkspaceOperationResult<T> RaceFailure<T>() =>
        KejiWorkspaceOperationResult<T>.Failure(KejiWorkspaceAccessFailureReason.RaceDetected);

    private static bool IsSafeFileSystemException(Exception exception) =>
        exception is UnauthorizedAccessException or IOException or
            System.ComponentModel.Win32Exception or ObjectDisposedException or
            ArgumentException or NotSupportedException or OverflowException or
            KejiContentTooLargeException;

    private sealed class KejiContentTooLargeException : IOException;
}
