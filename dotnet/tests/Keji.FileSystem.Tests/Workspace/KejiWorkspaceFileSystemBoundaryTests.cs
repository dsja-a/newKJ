using System.Reflection;
using Keji.FileSystem.Workspace;
using Keji.Security.Auth;

namespace Keji.FileSystem.Tests.Workspace;

public sealed class KejiWorkspaceFileSystemBoundaryTests : IDisposable
{
    private const string UserId = "0123456789abcdef";
    private const string OtherUserId = "fedcba9876543210";
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"keji-fs-boundary-{Guid.NewGuid():N}");

    [Fact]
    public async Task ExistsReturnsTrueForFileAndFalseForMissingTarget()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        File.WriteAllText(Path.Combine(_root, "shared", "present.txt"), "content");
        var service = CreateService();

        var present = await service.ExistsAsync(Shared("present.txt"));
        var missing = await service.ExistsAsync(Shared("missing.txt"));

        Assert.True(present.IsSuccess);
        Assert.Equal(KejiWorkspaceAccessFailureReason.None, present.FailureReason);
        Assert.True(present.Value);
        Assert.True(missing.IsSuccess);
        Assert.Equal(KejiWorkspaceAccessFailureReason.None, missing.FailureReason);
        Assert.False(missing.Value);
    }

    [Theory]
    [InlineData(KejiFileSystemOperation.Read)]
    [InlineData(KejiFileSystemOperation.Delete)]
    [InlineData(KejiFileSystemOperation.Enumerate)]
    public async Task MissingTargetIsRejectedForNonCreatingOperations(
        KejiFileSystemOperation operation)
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        var service = CreateService();

        var result = await Execute(service, operation, Shared("missing"));

        Assert.False(result.IsSuccess);
        Assert.Equal(KejiWorkspaceAccessFailureReason.TargetNotFound, result.FailureReason);
    }

    [Fact]
    public async Task CreateDirectoryDoesNotCreateMissingParentChain()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        var service = CreateService();

        var result = await service.CreateDirectoryAsync(Shared("missing/child"));

        Assert.False(result.IsSuccess);
        Assert.Equal(KejiWorkspaceAccessFailureReason.TargetNotFound, result.FailureReason);
        Assert.False(Directory.Exists(Path.Combine(_root, "shared", "missing")));
    }

    [Fact]
    public async Task CreateDirectoryRejectsExistingTarget()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared", "existing"));
        var service = CreateService();

        var result = await service.CreateDirectoryAsync(Shared("existing"));

        Assert.False(result.IsSuccess);
        Assert.Equal(KejiWorkspaceAccessFailureReason.TargetAlreadyExists, result.FailureReason);
        Assert.True(Directory.Exists(Path.Combine(_root, "shared", "existing")));
    }

    [Theory]
    [InlineData(KejiFileSystemOperation.Read)]
    [InlineData(KejiFileSystemOperation.Write)]
    [InlineData(KejiFileSystemOperation.Delete)]
    public async Task FileOperationsRejectDirectoryTargets(
        KejiFileSystemOperation operation)
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared", "folder"));
        var service = CreateService();

        var result = await Execute(service, operation, Shared("folder"));

        Assert.False(result.IsSuccess);
        Assert.Equal(KejiWorkspaceAccessFailureReason.TargetTypeMismatch, result.FailureReason);
        Assert.True(Directory.Exists(Path.Combine(_root, "shared", "folder")));
    }

    [Fact]
    public async Task EnumerateRejectsFileTarget()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        File.WriteAllText(Path.Combine(_root, "shared", "file.txt"), "content");
        var service = CreateService();

        var result = await service.EnumerateAsync(Shared("file.txt"));

        Assert.False(result.IsSuccess);
        Assert.Equal(KejiWorkspaceAccessFailureReason.TargetTypeMismatch, result.FailureReason);
    }

    [Fact]
    public async Task InvalidPathIsDeniedBeforeFileSystemInspection()
    {
        var options = new KejiWorkspaceOptions(_root);
        var recordingInspector = new RecordingInspector(new KejiWindowsPathInspector(options));
        var service = CreateService(inspector: recordingInspector);

        var result = await service.ReadTextAsync(Shared("../outside.txt"));

        Assert.False(result.IsSuccess);
        Assert.Equal(KejiWorkspaceAccessFailureReason.CandidateRejected, result.FailureReason);
        Assert.Empty(recordingInspector.Operations);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "outside.txt")));
    }

    [Fact]
    public async Task OtherUserWorkspaceIsDeniedBeforeFileSystemInspection()
    {
        var options = new KejiWorkspaceOptions(_root);
        var recordingInspector = new RecordingInspector(new KejiWindowsPathInspector(options));
        var service = CreateService(role: "member", inspector: recordingInspector);

        var result = await service.ExistsAsync(
            new(KejiWorkspaceScope.User, OtherUserId, "file.txt"));

        Assert.False(result.IsSuccess);
        Assert.Equal(
            KejiWorkspaceAccessFailureReason.OtherUserWorkspaceDenied,
            result.FailureReason);
        Assert.Empty(recordingInspector.Operations);
    }

    [Fact]
    public async Task AuthorizationFailurePrecedesInvalidContentFailure()
    {
        var service = CreateService(role: "readonly");

        var result = await service.WriteTextAsync(Shared("missing.txt"), null);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            KejiWorkspaceAccessFailureReason.ReadonlyWriteDenied,
            result.FailureReason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NullAndOversizedWriteContentAreRejectedWithoutMutation(bool useNull)
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        var target = Path.Combine(_root, "shared", "file.bin");
        File.WriteAllBytes(target, new byte[] { 7 });
        var service = CreateService(fileSystemOptions: new(maxContentBytes: 2));
        var content = useNull ? null : new byte[] { 1, 2, 3 };

        var result = await service.WriteBytesAsync(Shared("file.bin"), content);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            useNull
                ? KejiWorkspaceAccessFailureReason.InvalidContent
                : KejiWorkspaceAccessFailureReason.ContentTooLarge,
            result.FailureReason);
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(target));
    }

    [Fact]
    public async Task OversizedReadIsRejectedWithoutReturningContent()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        File.WriteAllBytes(
            Path.Combine(_root, "shared", "large.bin"),
            new byte[] { 1, 2, 3 });
        var service = CreateService(fileSystemOptions: new(maxContentBytes: 2));

        var result = await service.ReadBytesAsync(Shared("large.bin"));

        Assert.False(result.IsSuccess);
        Assert.Equal(KejiWorkspaceAccessFailureReason.ContentTooLarge, result.FailureReason);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task InvalidUtf8IsRejectedAsInvalidContent()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        File.WriteAllBytes(
            Path.Combine(_root, "shared", "invalid.txt"),
            new byte[] { 0xC3, 0x28 });
        var service = CreateService();

        var result = await service.ReadTextAsync(Shared("invalid.txt"));

        Assert.False(result.IsSuccess);
        Assert.Equal(KejiWorkspaceAccessFailureReason.InvalidContent, result.FailureReason);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task EnumerationLimitIsEnforcedWithoutPartialSuccess()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared", "folder"));
        File.WriteAllText(Path.Combine(_root, "shared", "folder", "one"), "1");
        File.WriteAllText(Path.Combine(_root, "shared", "folder", "two"), "2");
        var service = CreateService(fileSystemOptions: new(maxEntries: 1));

        var result = await service.EnumerateAsync(Shared("folder"));

        Assert.False(result.IsSuccess);
        Assert.Equal(KejiWorkspaceAccessFailureReason.ContentTooLarge, result.FailureReason);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task RevalidationFailureReturnsRaceDetectedAndDoesNotWrite()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        var target = Path.Combine(_root, "shared", "file.txt");
        File.WriteAllText(target, "before");
        var options = new KejiWorkspaceOptions(_root);
        var raceInspector = new RejectingRevalidationInspector(
            new KejiWindowsPathInspector(options));
        var service = CreateService(inspector: raceInspector);

        var result = await service.WriteTextAsync(Shared("file.txt"), "after");

        Assert.False(result.IsSuccess);
        Assert.Equal(KejiWorkspaceAccessFailureReason.RaceDetected, result.FailureReason);
        Assert.Equal("before", File.ReadAllText(target));
        Assert.Equal(1, raceInspector.RevalidationCount);
    }

    [Fact]
    public async Task LeaseFreeInspectorResultFailsClosed()
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        File.WriteAllText(Path.Combine(_root, "shared", "file.txt"), "content");
        var service = CreateService(inspector: new LeaseFreeInspector());

        var result = await service.ReadTextAsync(Shared("file.txt"));

        Assert.False(result.IsSuccess);
        Assert.Equal(
            KejiWorkspaceAccessFailureReason.PathInspectionFailed,
            result.FailureReason);
        Assert.Null(result.Value);
    }

    [Theory]
    [InlineData(KejiFileSystemOperation.Read)]
    [InlineData(KejiFileSystemOperation.Write)]
    [InlineData(KejiFileSystemOperation.Create)]
    [InlineData(KejiFileSystemOperation.Delete)]
    [InlineData(KejiFileSystemOperation.Enumerate)]
    public async Task FileSystemMapsEachMethodToExactOperation(
        KejiFileSystemOperation operation)
    {
        Directory.CreateDirectory(Path.Combine(_root, "shared"));
        File.WriteAllText(Path.Combine(_root, "shared", "file.txt"), "content");
        var options = new KejiWorkspaceOptions(_root);
        var recordingInspector = new RecordingInspector(new KejiWindowsPathInspector(options));
        var service = CreateService(inspector: recordingInspector);
        var request = operation == KejiFileSystemOperation.Create
            ? Shared("new-folder")
            : operation == KejiFileSystemOperation.Enumerate
                ? Shared(string.Empty)
                : Shared("file.txt");

        _ = await Execute(service, operation, request);

        Assert.NotEmpty(recordingInspector.Operations);
        Assert.All(recordingInspector.Operations, actual => Assert.Equal(operation, actual));
    }

    [Fact]
    public async Task EveryOperationPropagatesPreCanceledToken()
    {
        var service = CreateService();
        using var source = new CancellationTokenSource();
        source.Cancel();
        var request = Shared("file.txt");
        Func<Task>[] operations =
        {
            async () => _ = await service.ExistsAsync(request, source.Token),
            async () => _ = await service.ReadTextAsync(request, source.Token),
            async () => _ = await service.ReadBytesAsync(request, source.Token),
            async () => _ = await service.WriteTextAsync(request, "x", source.Token),
            async () => _ = await service.WriteBytesAsync(request, new byte[] { 1 }, source.Token),
            async () => _ = await service.CreateDirectoryAsync(request, source.Token),
            async () => _ = await service.EnumerateAsync(request, source.Token),
            async () => _ = await service.DeleteFileAsync(request, source.Token),
        };

        foreach (var operation in operations)
            await Assert.ThrowsAsync<OperationCanceledException>(operation);
    }

    [Fact]
    public void PublicContractsDoNotExposeCandidateAbsolutePaths()
    {
        const BindingFlags publicInstance = BindingFlags.Instance | BindingFlags.Public;
        const BindingFlags publicStatic = BindingFlags.Static | BindingFlags.Public;

        Assert.Null(typeof(KejiWorkspacePathCandidateResult).GetProperty("RootPath", publicInstance));
        Assert.Null(typeof(KejiWorkspacePathCandidateResult).GetProperty("FullPath", publicInstance));
        Assert.Null(typeof(KejiWorkspacePathCandidateResult).GetMethod("Success", publicStatic));
        Assert.Null(typeof(KejiWorkspaceAccessDecision).GetProperty("Candidate", publicInstance));
    }

    [Fact]
    public void ConstructorAndOptionsRejectInvalidDependenciesAndLimits()
    {
        var options = new KejiWorkspaceOptions(_root);
        var resolver = new KejiWorkspacePathCandidateResolver(options);
        var accessor = new Accessor(User("member"));
        var policy = new KejiWorkspaceAccessPolicy(resolver, accessor);
        var inspector = new KejiWindowsPathInspector(options);
        var fileSystemOptions = new KejiWorkspaceFileSystemOptions();

        Assert.Throws<ArgumentNullException>(() =>
            new KejiWorkspaceFileSystem(null!, inspector, fileSystemOptions));
        Assert.Throws<ArgumentNullException>(() =>
            new KejiWorkspaceFileSystem(policy, null!, fileSystemOptions));
        Assert.Throws<ArgumentNullException>(() =>
            new KejiWorkspaceFileSystem(policy, inspector, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new KejiWorkspaceFileSystemOptions(maxContentBytes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new KejiWorkspaceFileSystemOptions(maxEntries: 0));
    }

    [Fact]
    public void OperationResultFactoriesEnforceNonContradictoryState()
    {
        var success = KejiWorkspaceOperationResult<string>.Success("value");
        var failure = KejiWorkspaceOperationResult<string>.Failure(
            KejiWorkspaceAccessFailureReason.TargetNotFound);

        Assert.True(success.IsSuccess);
        Assert.Equal(KejiWorkspaceAccessFailureReason.None, success.FailureReason);
        Assert.Equal("value", success.Value);
        Assert.False(failure.IsSuccess);
        Assert.Equal(KejiWorkspaceAccessFailureReason.TargetNotFound, failure.FailureReason);
        Assert.Null(failure.Value);
        Assert.Throws<ArgumentNullException>(() =>
            KejiWorkspaceOperationResult<string>.Success(null!));
        Assert.Throws<ArgumentException>(() =>
            KejiWorkspaceOperationResult<string>.Failure(
                KejiWorkspaceAccessFailureReason.None));
    }

    private KejiWorkspaceFileSystem CreateService(
        string role = "member",
        IKejiWindowsPathInspector? inspector = null,
        KejiWorkspaceFileSystemOptions? fileSystemOptions = null)
    {
        var workspaceOptions = new KejiWorkspaceOptions(_root);
        var resolver = new KejiWorkspacePathCandidateResolver(workspaceOptions);
        var policy = new KejiWorkspaceAccessPolicy(resolver, new Accessor(User(role)));
        return new(
            policy,
            inspector ?? new KejiWindowsPathInspector(workspaceOptions),
            fileSystemOptions ?? new KejiWorkspaceFileSystemOptions());
    }

    private static async Task<KejiWorkspaceOperationResult<object>> Execute(
        KejiWorkspaceFileSystem service,
        KejiFileSystemOperation operation,
        KejiWorkspacePathRequest request)
    {
        return operation switch
        {
            KejiFileSystemOperation.Read => Convert(await service.ReadBytesAsync(request)),
            KejiFileSystemOperation.Write => Convert(
                await service.WriteBytesAsync(request, new byte[] { 1 })),
            KejiFileSystemOperation.Create => Convert(await service.CreateDirectoryAsync(request)),
            KejiFileSystemOperation.Delete => Convert(await service.DeleteFileAsync(request)),
            KejiFileSystemOperation.Enumerate => Convert(await service.EnumerateAsync(request)),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }

    private static KejiWorkspaceOperationResult<object> Convert<T>(
        KejiWorkspaceOperationResult<T> result)
    {
        return result.IsSuccess
            ? KejiWorkspaceOperationResult<object>.Success(result.Value!)
            : KejiWorkspaceOperationResult<object>.Failure(result.FailureReason);
    }

    private static KejiWorkspacePathRequest Shared(string relativePath) =>
        new(KejiWorkspaceScope.Shared, null, relativePath);

    private static CurrentUser User(string role) =>
        new(UserId, role, role, role, KejiAuthenticationKind.Jwt);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class Accessor(CurrentUser? user) : ICurrentUserAccessor
    {
        public CurrentUser? CurrentUser => user;
    }

    private sealed class RecordingInspector(IKejiWindowsPathInspector inner)
        : IKejiWindowsPathInspector
    {
        internal List<KejiFileSystemOperation> Operations { get; } = new();

        public KejiPathInspectionResult Inspect(
            KejiWorkspacePathCandidateResult candidate,
            KejiFileSystemOperation operation)
        {
            Operations.Add(operation);
            return inner.Inspect(candidate, operation);
        }

        public bool Revalidate(KejiPathInspectionResult inspection) =>
            inner.Revalidate(inspection);
    }

    private sealed class RejectingRevalidationInspector(IKejiWindowsPathInspector inner)
        : IKejiWindowsPathInspector
    {
        internal int RevalidationCount { get; private set; }

        public KejiPathInspectionResult Inspect(
            KejiWorkspacePathCandidateResult candidate,
            KejiFileSystemOperation operation) => inner.Inspect(candidate, operation);

        public bool Revalidate(KejiPathInspectionResult inspection)
        {
            RevalidationCount++;
            return false;
        }
    }

    private sealed class LeaseFreeInspector : IKejiWindowsPathInspector
    {
        public KejiPathInspectionResult Inspect(
            KejiWorkspacePathCandidateResult candidate,
            KejiFileSystemOperation operation) =>
            KejiPathInspectionResult.Safe(targetExists: true, targetIsDirectory: false);

        public bool Revalidate(KejiPathInspectionResult inspection) => true;
    }
}
