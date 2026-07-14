namespace Keji.FileSystem.Workspace;

public interface IKejiWorkspaceFileSystem
{
    Task<KejiWorkspaceOperationResult<bool>> ExistsAsync(
        KejiWorkspacePathRequest? path, CancellationToken cancellationToken = default);

    Task<KejiWorkspaceOperationResult<string>> ReadTextAsync(
        KejiWorkspacePathRequest? path, CancellationToken cancellationToken = default);

    Task<KejiWorkspaceOperationResult<byte[]>> ReadBytesAsync(
        KejiWorkspacePathRequest? path, CancellationToken cancellationToken = default);

    Task<KejiWorkspaceOperationResult<bool>> WriteTextAsync(
        KejiWorkspacePathRequest? path, string? content, CancellationToken cancellationToken = default);

    Task<KejiWorkspaceOperationResult<bool>> WriteBytesAsync(
        KejiWorkspacePathRequest? path, byte[]? content, CancellationToken cancellationToken = default);

    Task<KejiWorkspaceOperationResult<bool>> CreateDirectoryAsync(
        KejiWorkspacePathRequest? path, CancellationToken cancellationToken = default);

    Task<KejiWorkspaceOperationResult<IReadOnlyList<string>>> EnumerateAsync(
        KejiWorkspacePathRequest? path, CancellationToken cancellationToken = default);

    Task<KejiWorkspaceOperationResult<bool>> DeleteFileAsync(
        KejiWorkspacePathRequest? path, CancellationToken cancellationToken = default);
}
