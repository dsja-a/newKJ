using Keji.FileSystem.Workspace;
using Keji.Security.Auth;

namespace Keji.FileSystem.Tests.Workspace;

public sealed class KejiWorkspaceFileSystemTests : IDisposable
{
    private const string UserId = "0123456789abcdef";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"keji-fs-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReadsAndWritesExistingTextFile()
    {
        var service = CreateService("member");
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        File.WriteAllText(Path.Combine(_root, "shared", "file.txt"), "before");
        var request = new KejiWorkspacePathRequest(KejiWorkspaceScope.Shared, null, "file.txt");
        var write = await service.WriteTextAsync(request, "after");
        var read = await service.ReadTextAsync(request);
        Assert.True(write.IsSuccess);
        Assert.True(read.IsSuccess);
        Assert.Equal("after", read.Value);
    }

    [Fact]
    public async Task ReadsAndWritesExistingBytesFile()
    {
        var service = CreateService("member");
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        File.WriteAllBytes(Path.Combine(_root, "shared", "file.bin"), new byte[] { 1 });
        var request = new KejiWorkspacePathRequest(KejiWorkspaceScope.Shared, null, "file.bin");
        var write = await service.WriteBytesAsync(request, new byte[] { 2, 3 });
        var read = await service.ReadBytesAsync(request);
        Assert.True(write.IsSuccess);
        Assert.Equal(new byte[] { 2, 3 }, read.Value);
    }

    [Fact]
    public async Task CreatesOnlyRequestedDirectory()
    {
        var service = CreateService("member");
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        var request = new KejiWorkspacePathRequest(KejiWorkspaceScope.Shared, null, "new-folder");
        var result = await service.CreateDirectoryAsync(request);
        Assert.True(result.IsSuccess);
        Assert.True(Directory.Exists(Path.Combine(_root, "shared", "new-folder")));
    }

    [Fact]
    public async Task EnumeratesOnlyDirectChildNames()
    {
        var service = CreateService("member");
        Directory.CreateDirectory(Path.Combine(_root, "shared", "folder", "nested"));
        File.WriteAllText(Path.Combine(_root, "shared", "folder", "direct.txt"), "x");
        File.WriteAllText(Path.Combine(_root, "shared", "folder", "nested", "deep.txt"), "x");
        var request = new KejiWorkspacePathRequest(KejiWorkspaceScope.Shared, null, "folder");
        var result = await service.EnumerateAsync(request);
        Assert.True(result.IsSuccess);
        var entries = Assert.IsAssignableFrom<IReadOnlyList<string>>(result.Value);
        Assert.Equal(new[] { "direct.txt", "nested" }, entries.OrderBy(x => x));
        Assert.DoesNotContain("deep.txt", entries);
    }

    [Fact]
    public async Task DeletesFileButNeverDirectory()
    {
        var service = CreateService("member");
        Directory.CreateDirectory(Path.Combine(_root, "shared", "folder"));
        File.WriteAllText(Path.Combine(_root, "shared", "file.txt"), "x");
        var file = await service.DeleteFileAsync(new(KejiWorkspaceScope.Shared, null, "file.txt"));
        var directory = await service.DeleteFileAsync(new(KejiWorkspaceScope.Shared, null, "folder"));
        Assert.True(file.IsSuccess);
        Assert.False(File.Exists(Path.Combine(_root, "shared", "file.txt")));
        Assert.False(directory.IsSuccess);
        Assert.Equal(KejiWorkspaceAccessFailureReason.TargetTypeMismatch, directory.FailureReason);
        Assert.True(Directory.Exists(Path.Combine(_root, "shared", "folder")));
    }

    [Fact]
    public async Task ReadonlyCannotWrite()
    {
        var service = CreateService("readonly");
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        File.WriteAllText(Path.Combine(_root, "shared", "file.txt"), "before");
        var result = await service.WriteTextAsync(
            new(KejiWorkspaceScope.Shared, null, "file.txt"), "after");
        Assert.False(result.IsSuccess);
        Assert.Equal(KejiWorkspaceAccessFailureReason.ReadonlyWriteDenied, result.FailureReason);
        Assert.Equal("before", File.ReadAllText(Path.Combine(_root, "shared", "file.txt")));
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        var service = CreateService("member");
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExistsAsync(new(KejiWorkspaceScope.Shared, null, "file.txt"), source.Token));
    }

    private KejiWorkspaceFileSystem CreateService(string role)
    {
        var options = new KejiWorkspaceOptions(_root);
        var resolver = new KejiWorkspacePathCandidateResolver(options);
        var accessor = new Accessor(new CurrentUser(UserId, role, role, role, KejiAuthenticationKind.Jwt));
        var policy = new KejiWorkspaceAccessPolicy(resolver, accessor);
        var inspector = new KejiWindowsPathInspector(options);
        return new(policy, inspector, new KejiWorkspaceFileSystemOptions());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private sealed class Accessor(CurrentUser user) : ICurrentUserAccessor
    {
        public CurrentUser? CurrentUser => user;
    }
}
