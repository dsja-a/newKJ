using Keji.FileSystem.Workspace;
using Keji.Security.Auth;

namespace Keji.FileSystem.Tests.Workspace;

public sealed class KejiWorkspaceFileSystemRegressionTests : IDisposable
{
    private const string UserId = "0123456789abcdef";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"keji-fs-regression-{Guid.NewGuid():N}");

    [Fact]
    public async Task WriteTextCreatesMissingFileUnderExistingParentWithExactContent()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared", "existing"));
        var service = CreateService();

        var result = await service.WriteTextAsync(
            Shared("existing/new.txt"),
            "new text 内容");

        Assert.True(result.IsSuccess);
        Assert.Equal(KejiWorkspaceAccessFailureReason.None, result.FailureReason);
        Assert.True(result.Value);
        Assert.Equal(
            "new text 内容",
            File.ReadAllText(Path.Combine(_root, "shared", "existing", "new.txt")));
    }

    [Fact]
    public async Task WriteBytesCreatesMissingFileUnderExistingParentWithExactContent()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared", "existing"));
        var service = CreateService();
        byte[] content = [0x00, 0x01, 0x7F, 0x80, 0xFF];

        var result = await service.WriteBytesAsync(
            Shared("existing/new.bin"),
            content);

        Assert.True(result.IsSuccess);
        Assert.Equal(KejiWorkspaceAccessFailureReason.None, result.FailureReason);
        Assert.True(result.Value);
        Assert.Equal(
            content,
            File.ReadAllBytes(Path.Combine(_root, "shared", "existing", "new.bin")));
    }

    [Fact]
    public async Task WriteTextRejectsMissingParentWithoutCreatingAParentChain()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        var service = CreateService();

        var result = await service.WriteTextAsync(
            Shared("missing/new.txt"),
            "content");

        Assert.False(result.IsSuccess);
        Assert.Equal(
            KejiWorkspaceAccessFailureReason.TargetNotFound,
            result.FailureReason);
        Assert.False(Directory.Exists(Path.Combine(_root, "shared", "missing")));
        Assert.False(File.Exists(Path.Combine(_root, "shared", "missing", "new.txt")));
    }

    [Fact]
    public async Task WriteBytesRejectsMissingParentWithoutCreatingAParentChain()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        var service = CreateService();

        var result = await service.WriteBytesAsync(
            Shared("missing/new.bin"),
            [0x01, 0x02]);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            KejiWorkspaceAccessFailureReason.TargetNotFound,
            result.FailureReason);
        Assert.False(Directory.Exists(Path.Combine(_root, "shared", "missing")));
        Assert.False(File.Exists(Path.Combine(_root, "shared", "missing", "new.bin")));
    }

    [Fact]
    public async Task WriteTextRejectsUnpairedSurrogateWithoutCreatingAFileOrThrowing()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        var service = CreateService();
        var target = Path.Combine(_root, "shared", "invalid.txt");

        var result = await service.WriteTextAsync(
            Shared("invalid.txt"),
            "invalid-\uD800-content");

        Assert.False(result.IsSuccess);
        Assert.Equal(
            KejiWorkspaceAccessFailureReason.InvalidContent,
            result.FailureReason);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task OversizedWriteToMissingTargetDoesNotCreateAFile()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        var service = CreateService(fileSystemOptions: new(maxContentBytes: 2));
        var target = Path.Combine(_root, "shared", "too-large.bin");

        var result = await service.WriteBytesAsync(
            Shared("too-large.bin"),
            [0x01, 0x02, 0x03]);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            KejiWorkspaceAccessFailureReason.ContentTooLarge,
            result.FailureReason);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task TargetCreatedAfterRevalidationIsNotOverwritten()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        var workspaceOptions = new KejiWorkspaceOptions(_root);
        var target = Path.Combine(_root, "shared", "collision.txt");
        var inspector = new CreatingRevalidationInspector(
            new KejiWindowsPathInspector(workspaceOptions),
            target,
            "racing content");
        var service = CreateService(inspector: inspector);

        var result = await service.WriteTextAsync(
            Shared("collision.txt"),
            "new content");

        Assert.False(result.IsSuccess);
        Assert.Equal(
            KejiWorkspaceAccessFailureReason.RaceDetected,
            result.FailureReason);
        Assert.Equal("racing content", File.ReadAllText(target));
    }

    [Fact]
    public async Task CancellationDuringRevalidationPreventsDirectoryCreation()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        using var source = new CancellationTokenSource();
        var service = CreateService(source);
        var target = Path.Combine(_root, "shared", "new-directory");

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.CreateDirectoryAsync(Shared("new-directory"), source.Token));

        Assert.True(source.IsCancellationRequested);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public async Task CancellationDuringRevalidationPreventsFileCreation()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        using var source = new CancellationTokenSource();
        var service = CreateService(cancelDuringRevalidation: source);
        var target = Path.Combine(_root, "shared", "new-file.txt");

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.WriteTextAsync(Shared("new-file.txt"), "content", source.Token));

        Assert.True(source.IsCancellationRequested);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task CancellationDuringRevalidationPreventsFileDeletion()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        var target = Path.Combine(_root, "shared", "keep.txt");
        File.WriteAllText(target, "keep");
        using var source = new CancellationTokenSource();
        var service = CreateService(source);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.DeleteFileAsync(Shared("keep.txt"), source.Token));

        Assert.True(source.IsCancellationRequested);
        Assert.True(File.Exists(target));
        Assert.Equal("keep", File.ReadAllText(target));
    }

    private KejiWorkspaceFileSystem CreateService(
        CancellationTokenSource? cancelDuringRevalidation = null,
        IKejiWindowsPathInspector? inspector = null,
        KejiWorkspaceFileSystemOptions? fileSystemOptions = null)
    {
        var workspaceOptions = new KejiWorkspaceOptions(_root);
        var resolver = new KejiWorkspacePathCandidateResolver(workspaceOptions);
        var policy = new KejiWorkspaceAccessPolicy(
            resolver,
            new CurrentUserAccessor(new CurrentUser(
                UserId,
                "member",
                "member",
                "member",
                KejiAuthenticationKind.Jwt)));
        inspector ??= new KejiWindowsPathInspector(workspaceOptions);
        if (cancelDuringRevalidation is not null)
        {
            inspector = new CancelingRevalidationInspector(
                inspector,
                cancelDuringRevalidation);
        }

        return new KejiWorkspaceFileSystem(
            policy,
            inspector,
            fileSystemOptions ?? new KejiWorkspaceFileSystemOptions());
    }

    private static KejiWorkspacePathRequest Shared(string relativePath) =>
        new(KejiWorkspaceScope.Shared, null, relativePath);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class CurrentUserAccessor(CurrentUser currentUser)
        : ICurrentUserAccessor
    {
        public CurrentUser? CurrentUser => currentUser;
    }

    private sealed class CancelingRevalidationInspector(
        IKejiWindowsPathInspector inner,
        CancellationTokenSource source) : IKejiWindowsPathInspector
    {
        public KejiPathInspectionResult Inspect(
            KejiWorkspacePathCandidateResult candidate,
            KejiFileSystemOperation operation) => inner.Inspect(candidate, operation);

        public bool Revalidate(KejiPathInspectionResult inspection)
        {
            var isValid = inner.Revalidate(inspection);
            source.Cancel();
            return isValid;
        }
    }

    private sealed class CreatingRevalidationInspector(
        IKejiWindowsPathInspector inner,
        string targetPath,
        string content) : IKejiWindowsPathInspector
    {
        public KejiPathInspectionResult Inspect(
            KejiWorkspacePathCandidateResult candidate,
            KejiFileSystemOperation operation) => inner.Inspect(candidate, operation);

        public bool Revalidate(KejiPathInspectionResult inspection)
        {
            var isValid = inner.Revalidate(inspection);
            File.WriteAllText(targetPath, content);
            return isValid;
        }
    }
}
