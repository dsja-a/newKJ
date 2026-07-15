using System.Collections.Immutable;
using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Keji.Security.Auth;
using Keji.Security.Authorization;
using Keji.Tools.Catalog;
using Keji.Tools.Definitions;
using Keji.Tools.Execution;
using Keji.Tools.Registry;
using Keji.Tools.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Keji.Tools.Tests;

public class ToolExecutionPipelineTests
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

    private sealed class FakeAuditService() : IKejiAuditService
    {
        public Task<KejiAuditResult> WriteAsync(KejiAuditCategory category, string action, KejiAuditOutcome outcome, KejiAuditSeverity severity, string targetType, string? targetId = null, IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
            => Task.FromResult(KejiAuditResult.Written);
    }

    private sealed class FakeCorrelationAccessor : IKejiAuditCorrelationAccessor
    {
        public string? CorrelationId => null;
    }

    private static readonly IKejiToolRegistry Registry = CreateRegistry();
    private static readonly CurrentUser TestUser = new("test-user", "test", "user", "Test User", KejiAuthenticationKind.Jwt);

    private static IKejiToolRegistry CreateRegistry()
    {
        var builder = BuiltInToolCatalog.CreateBuilder();
        return builder.Build();
    }

    [Fact]
    public async Task ExecuteAsync_InvalidToolName_ReturnsFailure()
    {
        var launcher = new ToolWorkerLauncher("nonexistent.exe");
        var pipeline = CreatePipeline(launcher, testUser: TestUser, authorized: true);

        var result = await pipeline.ExecuteAsync("", null);

        Assert.False(result.Success);
        Assert.Contains("Invalid tool name", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTool_ReturnsFailure()
    {
        var launcher = new ToolWorkerLauncher("nonexistent.exe");
        var pipeline = CreatePipeline(launcher, testUser: TestUser, authorized: true);

        var result = await pipeline.ExecuteAsync("nonexistent_tool", null);

        Assert.False(result.Success);
        Assert.Contains("not registered", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ContractOnlyTool_ReturnsFailure()
    {
        var launcher = new ToolWorkerLauncher("nonexistent.exe");
        var pipeline = CreatePipeline(launcher, testUser: TestUser, authorized: true);

        var result = await pipeline.ExecuteAsync("read_file", null);

        Assert.False(result.Success);
        Assert.Contains("not executable", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_UnauthorizedUser_ReturnsFailure()
    {
        var launcher = new ToolWorkerLauncher("nonexistent.exe");
        var pipeline = CreatePipeline(launcher, testUser: TestUser, authorized: false);

        var result = await pipeline.ExecuteAsync("calculator", new Dictionary<string, object?> { ["expr"] = "2+2" });

        Assert.False(result.Success);
        Assert.Contains("Permission denied", result.ErrorMessage);
    }

    [Fact]
    public void Pipeline_Launcher_PathRequired()
    {
        Assert.Throws<ArgumentNullException>(() => new ToolWorkerLauncher(null!));
    }

    [Fact]
    public async Task ExecuteAsync_NoUser_StillChecks()
    {
        var launcher = new ToolWorkerLauncher("nonexistent.exe");
        var pipeline = CreatePipeline(launcher, testUser: null, authorized: true);

        var result = await pipeline.ExecuteAsync("calculator", new Dictionary<string, object?> { ["expr"] = "2+2" });

        Assert.False(result.Success);
    }

    private static ToolExecutionPipeline CreatePipeline(ToolWorkerLauncher launcher, CurrentUser? testUser, bool authorized)
    {
        return new ToolExecutionPipeline(
            new FakeUserAccessor(testUser),
            Registry,
            new FakeAuthService(authorized),
            new FakeAuditService(),
            new FakeCorrelationAccessor(),
            launcher);
    }
}
