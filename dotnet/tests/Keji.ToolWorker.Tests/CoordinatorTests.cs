using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Keji.Security.Auth;
using Keji.Security.Authorization;
using Keji.ToolWorker.Client;
using Keji.ToolWorker.Protocol;
using Keji.Tools.Catalog;
using Keji.Tools.Execution;
using Keji.Tools.Registry;

namespace Keji.ToolWorker.Tests;

public class CoordinatorTests
{
    private sealed class FakeUserAccessor(CurrentUser? user) : ICurrentUserAccessor
    {
        public CurrentUser? CurrentUser { get; } = user;
    }

    private sealed class FakeAuthService(bool allowed) : IKejiAuthorizationService
    {
        public KejiAuthorizationDecision Authorize(CurrentUser? user, KejiPermission permission) =>
            allowed ? KejiAuthorizationDecision.Allow() : KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.PermissionDenied);

        public KejiAuthorizationDecision AuthorizeAll(CurrentUser? user, IReadOnlyList<KejiPermission>? permissions) =>
            allowed ? KejiAuthorizationDecision.Allow() : KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.PermissionDenied);
    }

    private sealed class FakeAuditService : IKejiAuditService
    {
        public List<(KejiAuditCategory Category, string Action, KejiAuditOutcome Outcome)> Events { get; } = [];

        public Task<KejiAuditResult> WriteAsync(KejiAuditCategory category, string action, KejiAuditOutcome outcome, KejiAuditSeverity severity, string targetType, string? targetId = null, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
        {
            Events.Add((category, action, outcome));
            return Task.FromResult(KejiAuditResult.Written);
        }
    }

    private sealed class FakeWorkerClient(ToolWorkerResponse? cannedResponse) : IToolWorkerClient
    {
        public Task<ToolWorkerResponse> ExecuteAsync(ToolWorkerRequest request, CancellationToken ct = default)
        {
            var resp = cannedResponse ?? new ToolWorkerResponse
            {
                ProtocolVersion = "1.0",
                RequestId = request.RequestId,
                ErrorCode = (int)ToolWorkerErrorCode.ExecutionFailed,
                ErrorMessage = "Mock error"
            };
            // Always echo back the request's RequestId and ProtocolVersion for correlation validation
            return Task.FromResult(resp with { RequestId = request.RequestId, ProtocolVersion = "1.0" });
        }
    }

    private static readonly CurrentUser TestUser = new("test", "test", "user", "Test", KejiAuthenticationKind.Jwt);
    private static readonly IKejiToolRegistry Registry = BuildRegistry();

    private static IKejiToolRegistry BuildRegistry()
    {
        var builder = BuiltInToolCatalog.CreateBuilder();
        return builder.Build();
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTool_ReturnsError()
    {
        var coord = new ToolExecutionCoordinator(
            new FakeWorkerClient(null),
            new FakeUserAccessor(TestUser),
            Registry,
            new FakeAuthService(true),
            new FakeAuditService());

        var result = await coord.ExecuteAsync("nonexistent_tool", null);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_ContractOnlyTool_ReturnsError()
    {
        var coord = new ToolExecutionCoordinator(
            new FakeWorkerClient(null),
            new FakeUserAccessor(TestUser),
            Registry,
            new FakeAuthService(true),
            new FakeAuditService());

        var result = await coord.ExecuteAsync("read_file", null);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_Calculator_Success()
    {
        var coord = new ToolExecutionCoordinator(
            new FakeWorkerClient(new ToolWorkerResponse
            {
                ProtocolVersion = "1.0",
                RequestId = "",
                ErrorCode = 0,
                ResultJson = "{\"result\":4.0}"
            }),
            new FakeUserAccessor(TestUser),
            Registry,
            new FakeAuthService(true),
            new FakeAuditService());

        var result = await coord.ExecuteAsync("calculator", new Dictionary<string, object?> { ["expr"] = "2+2" });

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_UnauthorizedUser_ReturnsError()
    {
        var audit = new FakeAuditService();
        var coord = new ToolExecutionCoordinator(
            new FakeWorkerClient(null),
            new FakeUserAccessor(TestUser),
            Registry,
            new FakeAuthService(false),
            audit);

        var result = await coord.ExecuteAsync("calculator", new Dictionary<string, object?> { ["expr"] = "2+2" });

        Assert.False(result.Success);
        Assert.Contains("Denied", audit.Events[0].Outcome.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_WorkerError_LogsAudit()
    {
        var audit = new FakeAuditService();
        var coord = new ToolExecutionCoordinator(
            new FakeWorkerClient(new ToolWorkerResponse
            {
                ProtocolVersion = "1.0",
                RequestId = "",
                ErrorCode = (int)ToolWorkerErrorCode.ExecutionFailed,
                ErrorMessage = "Something went wrong"
            }),
            new FakeUserAccessor(TestUser),
            Registry,
            new FakeAuthService(true),
            audit);

        var result = await coord.ExecuteAsync("calculator", new Dictionary<string, object?> { ["expr"] = "2+2" });

        Assert.False(result.Success);
        var failureEvents = audit.Events.Where(e => e.Outcome == KejiAuditOutcome.Failure).ToList();
        Assert.NotEmpty(failureEvents);
    }

    [Fact]
    public async Task ExecuteAsync_NullUser_Rejected()
    {
        var coord = new ToolExecutionCoordinator(
            new FakeWorkerClient(new ToolWorkerResponse
            {
                ProtocolVersion = "1.0",
                RequestId = "",
                ErrorCode = 0,
                ResultJson = "{\"result\":4.0}"
            }),
            new FakeUserAccessor(null),
            Registry,
            new FakeAuthService(true),
            new FakeAuditService());

        var result = await coord.ExecuteAsync("calculator", new Dictionary<string, object?> { ["expr"] = "2+2" });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidToolName_ReturnsError()
    {
        var coord = new ToolExecutionCoordinator(
            new FakeWorkerClient(null),
            new FakeUserAccessor(TestUser),
            Registry,
            new FakeAuthService(true),
            new FakeAuditService());

        var result = await coord.ExecuteAsync("", null);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_GetTime_Success()
    {
        var coord = new ToolExecutionCoordinator(
            new FakeWorkerClient(new ToolWorkerResponse
            {
                ProtocolVersion = "1.0",
                RequestId = "",
                ErrorCode = 0,
                ResultJson = "{\"utc_iso8601\":\"2026-01-01T00:00:00Z\"}"
            }),
            new FakeUserAccessor(TestUser),
            Registry,
            new FakeAuthService(true),
            new FakeAuditService());

        var result = await coord.ExecuteAsync("get_time", null);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidInput_ReturnsError()
    {
        var coord = new ToolExecutionCoordinator(
            new FakeWorkerClient(null),
            new FakeUserAccessor(TestUser),
            Registry,
            new FakeAuthService(true),
            new FakeAuditService());

        // calculator expects string "expr", passing integer instead
        var result = await coord.ExecuteAsync("calculator", new Dictionary<string, object?> { ["expr"] = 42 });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_CorrelationMismatch_ReturnsError()
    {
        var badClient = new CorruptedWorkerClient();
        var coord = new ToolExecutionCoordinator(
            badClient,
            new FakeUserAccessor(TestUser),
            Registry,
            new FakeAuthService(true),
            new FakeAuditService());

        var result = await coord.ExecuteAsync("calculator", new Dictionary<string, object?> { ["expr"] = "2+2" });

        Assert.False(result.Success);
    }

    private sealed class CorruptedWorkerClient : IToolWorkerClient
    {
        public Task<ToolWorkerResponse> ExecuteAsync(ToolWorkerRequest request, CancellationToken ct = default)
        {
            // Return wrong ProtocolVersion to trigger correlation check
            return Task.FromResult(new ToolWorkerResponse
            {
                ProtocolVersion = "0.0",
                RequestId = request.RequestId,
                ErrorCode = 0,
                ResultJson = "{}"
            });
        }
    }
}
