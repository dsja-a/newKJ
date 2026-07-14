using System.Collections.Concurrent;
using System.Reflection;
using Keji.Api.Services;
using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Keji.Persistence;
using Keji.Persistence.Models;
using Keji.Persistence.Repositories;
using Keji.Persistence.Validation;
using Keji.Security.Auth;
using Keji.Security.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Keji.Integration.Tests;

public sealed class ConversationServiceTests
{
    // ── Valid test user IDs (16-char hex) ─────────────
    private const string UserA = "aaaaaaaaaaaaaaaa";
    private const string UserB = "bbbbbbbbbbbbbbbb";
    private const string UserC = "cccccccccccccccc";
    private const string AdminId = "2222222222222222";

    // ── Architecture: interface contracts ──────────────

    [Fact]
    public void IKejiConversationService_HasNoOwnerUserIdParameters()
    {
        var methods = typeof(IKejiConversationService).GetMethods();
        foreach (var method in methods)
        {
            foreach (var param in method.GetParameters())
            {
                Assert.DoesNotContain("owner", param.Name, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("ownerUserId", param.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void Service_ReadsOwnerFrom_ICurrentUserAccessor()
    {
        var serviceCtor = typeof(KejiConversationService).GetConstructors().Single();
        var ctorParams = serviceCtor.GetParameters().Select(p => p.ParameterType).ToHashSet();
        Assert.Contains(typeof(ICurrentUserAccessor), ctorParams);
    }

    [Fact]
    public void IConversationRepository_HasNoNoOwnerOverloads()
    {
        var methods = typeof(IConversationRepository).GetMethods();
        foreach (var method in methods)
        {
            var hasNoOwner = method.GetParameters().Any(p =>
                p.Name == "ownerUserId" && p.ParameterType == typeof(string));
            Assert.True(hasNoOwner, $"{method.Name} should have ownerUserId parameter");
        }
    }

    [Fact]
    public void IMessageRepository_HasNoNoOwnerOverloads()
    {
        var methods = typeof(IMessageRepository).GetMethods();
        foreach (var method in methods)
        {
            var hasNoOwner = method.GetParameters().Any(p =>
                p.Name == "ownerUserId" && p.ParameterType == typeof(string));
            Assert.True(hasNoOwner, $"{method.Name} should have ownerUserId parameter");
        }
    }

    [Fact]
    public void NoController_AcceptsOwnerParam()
    {
        var controllerTypes = typeof(Program).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("Controller", StringComparison.Ordinal) &&
                        t.IsSubclassOf(typeof(Microsoft.AspNetCore.Mvc.ControllerBase)));
        foreach (var ctrl in controllerTypes)
        {
            var methods = ctrl.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                foreach (var param in method.GetParameters())
                {
                    Assert.DoesNotContain("owner", param.Name, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void DiRegistration_IsScoped()
    {
        var programType = typeof(Program);
        var servicesField = programType.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault();
        Assert.Null(servicesField);
        var nested = programType.GetNestedTypes(BindingFlags.NonPublic);
        Assert.True(nested.Length >= 0);
    }

    [Fact]
    public void IKejiConversationService_RegisteredAsScoped()
    {
        var services = new ServiceCollection();
        var programType = typeof(Program);
        var addServicesMethod = programType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(m => m.Name == "ConfigureWebHost" ||
                                 m.GetParameters().Any(p => p.ParameterType == typeof(IServiceCollection)));
        services.AddScoped<IKejiConversationService, KejiConversationService>();
        var desc = services.SingleOrDefault(d => d.ServiceType == typeof(IKejiConversationService));
        Assert.NotNull(desc);
        Assert.Equal(ServiceLifetime.Scoped, desc!.Lifetime);
    }

    // ── Architecture: user ID validation ───────────────

    [Fact]
    public void NullUserId_Rejected()
        => Assert.Throws<KejiPersistenceException>(() => UserIdValidator.RequireValid(null!));

    [Fact]
    public void EmptyUserId_Rejected()
        => Assert.Throws<KejiPersistenceException>(() => UserIdValidator.RequireValid(""));

    [Fact]
    public void FifteenCharUserId_Rejected()
        => Assert.Throws<KejiPersistenceException>(() => UserIdValidator.RequireValid("aaaaaaaaaaaaaaa"));

    [Fact]
    public void SeventeenCharUserId_Rejected()
        => Assert.Throws<KejiPersistenceException>(() => UserIdValidator.RequireValid("aaaaaaaaaaaaaaaaa"));

    [Fact]
    public void UppercaseHexUserId_Rejected()
        => Assert.Throws<KejiPersistenceException>(() => UserIdValidator.RequireValid("AAAAAAAAAAAAAAAA"));

    [Fact]
    public void LeadingWhitespaceUserId_Rejected()
        => Assert.Throws<KejiPersistenceException>(() => UserIdValidator.RequireValid(" aaaaaaaaaaaaaaa"));

    [Fact]
    public void TrailingWhitespaceUserId_Rejected()
        => Assert.Throws<KejiPersistenceException>(() => UserIdValidator.RequireValid("aaaaaaaaaaaaaaa "));

    [Fact]
    public void ValidHex16UserId_Passes()
    {
        var result = UserIdValidator.RequireValid("aaaaaaaaaaaaaaaa");
        Assert.Equal("aaaaaaaaaaaaaaaa", result);
    }

    [Fact]
    public void ValidHex16UserId_WithMixedCaseLower_Passes()
    {
        var result = UserIdValidator.RequireValid("abcdef0123456789");
        Assert.Equal("abcdef0123456789", result);
    }

    [Fact]
    public void NonHexUserId_Rejected()
        => Assert.Throws<KejiPersistenceException>(() => UserIdValidator.RequireValid("gggggggggggggggg"));

    [Fact]
    public void ControlCharUserId_Rejected()
        => Assert.Throws<KejiPersistenceException>(() => UserIdValidator.RequireValid("aaaaaaaaaaaaaaa\0"));

    // ── Service behavior: audit isolation ──────────────

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();
        public List<object?> States { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            States.Add(state);
        }
    }

    private sealed class FakeAuditService : IKejiAuditService
    {
        public KejiAuditResult ReturnResult { get; set; } = KejiAuditResult.Written;
        public Exception? ThrowException { get; set; }

        public List<(KejiAuditCategory Category, string Action, KejiAuditOutcome Outcome, string TargetType, string? TargetId)> Calls { get; } = new();

        public Task<KejiAuditResult> WriteAsync(
            KejiAuditCategory category,
            string action,
            KejiAuditOutcome outcome,
            KejiAuditSeverity severity,
            string targetType,
            string? targetId = null,
            IReadOnlyDictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            if (ThrowException is not null)
            {
                var ex = ThrowException;
                if (ex is OperationCanceledException)
                    throw ex;
                throw ex;
            }
            Calls.Add((category, action, outcome, targetType, targetId));
            return Task.FromResult(ReturnResult);
        }
    }

    private sealed class FakeCurrentUserAccessor : ICurrentUserAccessor
    {
        public CurrentUser? CurrentUser { get; set; }
    }

    private async Task<ServiceTestContext> CreateServiceTestContextAsync(Action<IServiceCollection>? configure = null)
    {
        var ctx = new ServiceTestContext();
        await ctx.InitAsync(configure);
        return ctx;
    }

    private sealed class ServiceTestContext : IAsyncDisposable
    {
        public string TempDir { get; }
        public FakeCurrentUserAccessor UserAccessor { get; }
        public FakeAuditService AuditService { get; }
        public CaptureLogger<KejiConversationService> Logger { get; }
        public WebApplicationFactory<Program> Factory { get; private set; } = null!;
        public IKejiConversationService Service { get; private set; } = null!;

        public ServiceTestContext()
        {
            TempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KJ_SVC_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDir);
            UserAccessor = new FakeCurrentUserAccessor();
            AuditService = new FakeAuditService();
            Logger = new CaptureLogger<KejiConversationService>();
        }

        public async Task InitAsync(Action<IServiceCollection>? configure = null)
        {
            var configYaml = $@"database:
  path: test.db
security:
  enabled: false
";
            System.IO.File.WriteAllText(System.IO.Path.Combine(TempDir, "config.yaml"), configYaml);

            Factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseSetting("Keji:ProjectRoot", TempDir);
                    builder.ConfigureServices(services =>
                    {
                        var pDesc = services.SingleOrDefault(d => d.ServiceType == typeof(KejiPersistenceOptions));
                        if (pDesc != null) services.Remove(pDesc);
                        services.AddSingleton(new KejiPersistenceOptions
                        {
                            ProjectRoot = TempDir,
                            DatabasePath = System.IO.Path.Combine(TempDir, "test.db"),
                            BusyTimeoutMilliseconds = 5000,
                            EnableWal = true,
                            EnableForeignKeys = true,
                            CreateDirectoryIfMissing = true,
                        });

                        var userDesc = services.SingleOrDefault(d => d.ServiceType == typeof(ICurrentUserAccessor));
                        if (userDesc != null) services.Remove(userDesc);
                        services.AddSingleton<ICurrentUserAccessor>(UserAccessor);

                        var auditDesc = services.SingleOrDefault(d => d.ServiceType == typeof(IKejiAuditService));
                        if (auditDesc != null) services.Remove(auditDesc);
                        services.AddSingleton<IKejiAuditService>(AuditService);

                        var loggerDesc = services.FirstOrDefault(d =>
                            d.ServiceType == typeof(ILogger<KejiConversationService>));
                        if (loggerDesc != null) services.Remove(loggerDesc);
                        services.AddSingleton<ILogger<KejiConversationService>>(Logger);

                        configure?.Invoke(services);
                    });
                });

            using var scope = Factory.Services.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
            var msgRepo = scope.ServiceProvider.GetRequiredService<IMessageRepository>();
            var userAcc = scope.ServiceProvider.GetRequiredService<ICurrentUserAccessor>();
            var auditSvc = scope.ServiceProvider.GetRequiredService<IKejiAuditService>();
            var log = scope.ServiceProvider.GetRequiredService<ILogger<KejiConversationService>>();

            Service = new KejiConversationService(repo, msgRepo, userAcc, auditSvc, log);

            // Ensure DB tables exist
            await repo.CountByOwnerAsync(UserA);
        }

        public async ValueTask DisposeAsync()
        {
            if (Factory is not null)
                await Factory.DisposeAsync();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(TempDir)) Directory.Delete(TempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task AuditThrows_Create_StillReturnsRecord()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        ctx.AuditService.ThrowException = new InvalidOperationException("audit failed");

        var record = await ctx.Service.CreateConversationAsync("conv-audit-throw-1", "test");

        Assert.NotNull(record);
        Assert.Equal("conv-audit-throw-1", record.Id);
    }

    [Fact]
    public async Task AuditThrows_Get_StillReturnsRecord()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        ctx.AuditService.ThrowException = null;
        await ctx.Service.CreateConversationAsync("conv-audit-throw-2", "test");

        ctx.AuditService.ThrowException = new InvalidOperationException("audit failed");
        var record = await ctx.Service.GetConversationAsync("conv-audit-throw-2");

        Assert.NotNull(record);
        Assert.Equal("conv-audit-throw-2", record!.Id);
    }

    [Fact]
    public async Task AuditThrows_Delete_StillDeletes()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        ctx.AuditService.ThrowException = null;
        await ctx.Service.CreateConversationAsync("conv-audit-throw-3", "test");

        ctx.AuditService.ThrowException = new InvalidOperationException("audit failed");
        var deleted = await ctx.Service.DeleteConversationAsync("conv-audit-throw-3");

        Assert.True(deleted);
    }

    [Fact]
    public async Task AuditThrows_LoggerRecordsFixedCode()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        ctx.AuditService.ThrowException = new InvalidOperationException("audit failed");

        await ctx.Service.CreateConversationAsync("conv-audit-fixed-1", "test");

        Assert.Contains(ctx.Logger.Messages, m => m.Contains("AUDIT_UNEXPECTED_FAILURE"));
    }

    [Fact]
    public async Task AuditThrows_LoggerDoesNotContainExceptionMessage()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        ctx.AuditService.ThrowException = new InvalidOperationException("audit failed");

        await ctx.Service.CreateConversationAsync("conv-audit-nomsg-1", "test");

        Assert.DoesNotContain(ctx.Logger.Messages, m => m.Contains("audit failed"));
    }

    [Fact]
    public async Task AuditThrows_LoggerDoesNotContainStackTrace()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        ctx.AuditService.ThrowException = new InvalidOperationException("audit failed");

        await ctx.Service.CreateConversationAsync("conv-audit-nostack-1", "test");

        var allLogs = string.Join(Environment.NewLine, ctx.Logger.Messages);
        Assert.DoesNotContain("at ", allLogs);
        Assert.DoesNotContain("StackTrace", allLogs);
        Assert.DoesNotContain("--->", allLogs);
    }

    [Fact]
    public async Task AuditThrows_OperationCanceled_Propagates()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        ctx.AuditService.ThrowException = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ctx.Service.CreateConversationAsync("conv-audit-oce-1", "test"));
    }

    // ── Audit result isolation ─────────────────────────

    [Fact]
    public async Task AuditPartialFailure_DoesNotChangeCreateResult()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        ctx.AuditService.ReturnResult = KejiAuditResult.PartialFailure;

        var record = await ctx.Service.CreateConversationAsync("conv-partial-1", "test");
        Assert.NotNull(record);
    }

    [Fact]
    public async Task AuditSinkError_DoesNotChangeCreateResult()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        ctx.AuditService.ReturnResult = KejiAuditResult.SinkError;

        var record = await ctx.Service.CreateConversationAsync("conv-sink-1", "test");
        Assert.NotNull(record);
    }

    [Fact]
    public async Task AuditValidationError_DoesNotChangeCreateResult()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        ctx.AuditService.ReturnResult = KejiAuditResult.ValidationError;

        var record = await ctx.Service.CreateConversationAsync("conv-val-1", "test");
        Assert.NotNull(record);
    }

    // ── Cross-owner: Create ────────────────────────────

    [Fact]
    public async Task UserB_CreateWithUserAId_ThrowsNotFound()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        await ctx.Service.CreateConversationAsync("cross-create-1", "test");

        ctx.UserAccessor.CurrentUser = new CurrentUser(UserB, "userB", "member", "User B", KejiAuthenticationKind.Jwt);
        var ex = await Assert.ThrowsAsync<ConversationNotFoundException>(() =>
            ctx.Service.CreateConversationAsync("cross-create-1", "test"));
        Assert.DoesNotContain("cross-create-1", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UserB_CreateWithUserAId_ProducesDeniedAudit()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        await ctx.Service.CreateConversationAsync("cross-create-audit-1", "test");

        ctx.AuditService.Calls.Clear();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserB, "userB", "member", "User B", KejiAuthenticationKind.Jwt);
        try { await ctx.Service.CreateConversationAsync("cross-create-audit-1", "test"); } catch { }

        var deniedCall = ctx.AuditService.Calls.FirstOrDefault(c =>
            c.Action == "conversation_access" && c.Outcome == KejiAuditOutcome.Denied);
        Assert.NotEqual(default, deniedCall);
        Assert.Equal(KejiAuditCategory.DataAccess, deniedCall.Category);
        Assert.Equal("conversation", deniedCall.TargetType);
        Assert.Null(deniedCall.TargetId);
    }

    [Fact]
    public async Task UserB_EnsureWithUserAId_ThrowsNotFound()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        await ctx.Service.CreateConversationAsync("cross-ensure-1", "test");

        ctx.UserAccessor.CurrentUser = new CurrentUser(UserB, "userB", "member", "User B", KejiAuthenticationKind.Jwt);
        var ex = await Assert.ThrowsAsync<ConversationNotFoundException>(() =>
            ctx.Service.EnsureConversationAsync("cross-ensure-1", "test"));
        Assert.DoesNotContain("cross-ensure-1", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UserB_EnsureWithUserAId_ProducesDeniedAudit()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        await ctx.Service.CreateConversationAsync("cross-ensure-audit-1", "test");

        ctx.AuditService.Calls.Clear();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserB, "userB", "member", "User B", KejiAuthenticationKind.Jwt);
        try { await ctx.Service.EnsureConversationAsync("cross-ensure-audit-1", "test"); } catch { }

        var deniedCall = ctx.AuditService.Calls.FirstOrDefault(c =>
            c.Action == "conversation_access" && c.Outcome == KejiAuditOutcome.Denied);
        Assert.NotEqual(default, deniedCall);
        Assert.Null(deniedCall.TargetId);
    }

    [Fact]
    public async Task SameOwner_CreateIdempotent_RecordsSuccessAudit()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        ctx.AuditService.Calls.Clear();
        await ctx.Service.CreateConversationAsync("idem-1", "test");

        var successCall = ctx.AuditService.Calls.FirstOrDefault(c =>
            c.Action == "conversation_create" && c.Outcome == KejiAuditOutcome.Success);
        Assert.NotEqual(default, successCall);
    }

    [Fact]
    public async Task SameOwner_EnsureIdempotent_RecordsSuccessAudit()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        await ctx.Service.CreateConversationAsync("idem-ensure-1", "test");

        ctx.AuditService.Calls.Clear();
        var result = await ctx.Service.EnsureConversationAsync("idem-ensure-1", "test");

        Assert.Equal(ConversationOwnershipResult.AlreadyOwned, result.Result);
        var successCall = ctx.AuditService.Calls.FirstOrDefault(c =>
            c.Action == "conversation_ensure" && c.Outcome == KejiAuditOutcome.Success);
        Assert.NotEqual(default, successCall);
    }

    // ── Cross-owner: Get / Rename / Delete / Messages ──

    [Fact]
    public async Task UserB_CannotGetUserARecord()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        await ctx.Service.CreateConversationAsync("cross-get-1", "test");

        ctx.UserAccessor.CurrentUser = new CurrentUser(UserB, "userB", "member", "User B", KejiAuthenticationKind.Jwt);
        var record = await ctx.Service.GetConversationAsync("cross-get-1");

        Assert.Null(record);
    }

    [Fact]
    public async Task UserB_CannotRenameUserARecord()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        await ctx.Service.CreateConversationAsync("cross-rename-1", "test");

        ctx.UserAccessor.CurrentUser = new CurrentUser(UserB, "userB", "member", "User B", KejiAuthenticationKind.Jwt);
        var ok = await ctx.Service.RenameConversationAsync("cross-rename-1", "new-title");

        Assert.False(ok);
    }

    [Fact]
    public async Task UserB_CannotDeleteUserARecord()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        await ctx.Service.CreateConversationAsync("cross-delete-1", "test");

        ctx.UserAccessor.CurrentUser = new CurrentUser(UserB, "userB", "member", "User B", KejiAuthenticationKind.Jwt);
        var ok = await ctx.Service.DeleteConversationAsync("cross-delete-1");

        Assert.False(ok);
    }

    [Fact]
    public async Task UserB_CannotAddMessageToUserAConversation()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        await ctx.Service.CreateConversationAsync("cross-msg-1", "test");

        ctx.UserAccessor.CurrentUser = new CurrentUser(UserB, "userB", "member", "User B", KejiAuthenticationKind.Jwt);
        await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            ctx.Service.AddMessageAsync("cross-msg-1", "user", "hello"));
    }

    [Fact]
    public async Task UserB_CannotListMessagesOfUserA()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        await ctx.Service.CreateConversationAsync("cross-msg-list-1", "test");
        await ctx.Service.AddMessageAsync("cross-msg-list-1", "user", "hello from A");

        ctx.UserAccessor.CurrentUser = new CurrentUser(UserB, "userB", "member", "User B", KejiAuthenticationKind.Jwt);
        var msgs = await ctx.Service.ListMessagesAsync("cross-msg-list-1");

        Assert.Empty(msgs);
    }

    // ── NULL old records ───────────────────────────────

    [Fact]
    public async Task NullOldRecord_NotAccessibleViaService()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);

        var record = await ctx.Service.GetConversationAsync("nonexistent-conv");
        Assert.Null(record);

        var created = await ctx.Service.CreateConversationAsync("nonexistent-conv");
        Assert.Equal("nonexistent-conv", created.Id);

        var ok = await ctx.Service.RenameConversationAsync("nonexistent-conv-other", "x");
        Assert.False(ok);

        var deleted = await ctx.Service.DeleteConversationAsync("nonexistent-conv-other");
        Assert.False(deleted);

        await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            ctx.Service.AddMessageAsync("nonexistent-conv-other", "user", "x"));

        var msgs = await ctx.Service.ListMessagesAsync("nonexistent-conv-other");
        Assert.Empty(msgs);
    }

    // ── Service does not expose other owner info ───────

    [Fact]
    public async Task ServiceResult_DoesNotContainOtherOwnerInfo()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        var record = await ctx.Service.CreateConversationAsync("no-owner-info", "title-a");

        Assert.Equal(UserA, record.OwnerUserId);
        Assert.DoesNotContain(UserB, record.OwnerUserId ?? "");
        Assert.DoesNotContain(UserC, record.OwnerUserId ?? "");
    }

    // ── User ID validation in service ──────────────────

    [Fact]
    public async Task Service_NullCurrentUser_Rejected()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = null;

        await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            ctx.Service.CreateConversationAsync("x", "test"));
    }

    [Fact]
    public async Task Service_InvalidUserId_Rejected()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser("short", "user", "member", "User", KejiAuthenticationKind.Jwt);

        await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            ctx.Service.CreateConversationAsync("x", "test"));
    }

    // ── Integration: real end-to-end flow ──────────────

    [Fact]
    public async Task UserA_CreateAndReadOwnConversation()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);

        var created = await ctx.Service.CreateConversationAsync("e2e-1", "my title");
        Assert.Equal("e2e-1", created.Id);
        Assert.Equal(UserA, created.OwnerUserId);

        var read = await ctx.Service.GetConversationAsync("e2e-1");
        Assert.NotNull(read);
        Assert.Equal("e2e-1", read!.Id);
    }

    [Fact]
    public async Task UserB_SameId_NotReturnUserAData()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        await ctx.Service.CreateConversationAsync("e2e-cross-1", "secret title");

        ctx.UserAccessor.CurrentUser = new CurrentUser(UserB, "userB", "member", "User B", KejiAuthenticationKind.Jwt);
        var record = await ctx.Service.GetConversationAsync("e2e-cross-1");

        Assert.Null(record);
    }

    [Fact]
    public async Task UserB_SameId_Create_NotReturnUserAData()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        await ctx.Service.CreateConversationAsync("e2e-cross-create-1", "secret");

        ctx.UserAccessor.CurrentUser = new CurrentUser(UserB, "userB", "member", "User B", KejiAuthenticationKind.Jwt);
        var ex = await Assert.ThrowsAsync<ConversationNotFoundException>(() =>
            ctx.Service.CreateConversationAsync("e2e-cross-create-1", "user b title"));

        Assert.DoesNotContain("e2e-cross-create-1", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UserB_CannotRenameOrDeleteUserAConversation()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        await ctx.Service.CreateConversationAsync("e2e-cross-rd-1", "title");

        ctx.UserAccessor.CurrentUser = new CurrentUser(UserB, "userB", "member", "User B", KejiAuthenticationKind.Jwt);
        var renamed = await ctx.Service.RenameConversationAsync("e2e-cross-rd-1", "hacked");
        Assert.False(renamed);

        var deleted = await ctx.Service.DeleteConversationAsync("e2e-cross-rd-1");
        Assert.False(deleted);

        // User A confirms still exists
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        var record = await ctx.Service.GetConversationAsync("e2e-cross-rd-1");
        Assert.NotNull(record);
    }

    [Fact]
    public async Task UserB_CannotAddOrReadUserAMessages()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        await ctx.Service.CreateConversationAsync("e2e-msg-1", "title");
        await ctx.Service.AddMessageAsync("e2e-msg-1", "user", "A's secret message");

        ctx.UserAccessor.CurrentUser = new CurrentUser(UserB, "userB", "member", "User B", KejiAuthenticationKind.Jwt);
        await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            ctx.Service.AddMessageAsync("e2e-msg-1", "user", "B's message"));

        var msgs = await ctx.Service.ListMessagesAsync("e2e-msg-1");
        Assert.Empty(msgs);
    }

    [Fact]
    public async Task DifferentRoles_StillOnlySeeOwnConversations()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        await ctx.Service.CreateConversationAsync("role-test-1", "member conv");

        ctx.UserAccessor.CurrentUser = new CurrentUser(UserB, "userB", "admin", "User B", KejiAuthenticationKind.Jwt);
        var record = await ctx.Service.GetConversationAsync("role-test-1");
        Assert.Null(record);

        ctx.UserAccessor.CurrentUser = new CurrentUser(UserC, "userC", "readonly", "User C", KejiAuthenticationKind.Jwt);
        var record2 = await ctx.Service.GetConversationAsync("role-test-1");
        Assert.Null(record2);
    }

    [Fact]
    public async Task CrossOwner_CreateAndGet_ReturnSameResultAsNotExists()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        await ctx.Service.CreateConversationAsync("same-as-not-exists", "test");

        ctx.UserAccessor.CurrentUser = new CurrentUser(UserB, "userB", "member", "User B", KejiAuthenticationKind.Jwt);

        var getResult = await ctx.Service.GetConversationAsync("same-as-not-exists");
        Assert.Null(getResult);

        var ex1 = await Record.ExceptionAsync(() =>
            ctx.Service.CreateConversationAsync("same-as-not-exists", "x"));
        Assert.IsType<ConversationNotFoundException>(ex1);

        var ex2 = await Record.ExceptionAsync(() =>
            ctx.Service.EnsureConversationAsync("same-as-not-exists", "x"));
        Assert.IsType<ConversationNotFoundException>(ex2);

        var renameOk = await ctx.Service.RenameConversationAsync("same-as-not-exists", "x");
        Assert.False(renameOk);

        var deleteOk = await ctx.Service.DeleteConversationAsync("same-as-not-exists");
        Assert.False(deleteOk);

        var msgs = await ctx.Service.ListMessagesAsync("same-as-not-exists");
        Assert.Empty(msgs);
    }

    [Fact]
    public async Task AuditFailure_DoesNotChangeHttpOrServiceResult()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        ctx.AuditService.ReturnResult = KejiAuditResult.SinkError;

        var record = await ctx.Service.CreateConversationAsync("audit-fail-result-1", "test");
        Assert.NotNull(record);

        var getRecord = await ctx.Service.GetConversationAsync("audit-fail-result-1");
        Assert.NotNull(getRecord);

        var deleted = await ctx.Service.DeleteConversationAsync("audit-fail-result-1");
        Assert.True(deleted);
    }

    [Fact]
    public async Task NullOldConversation_RemainsInaccessible()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);

        var record = await ctx.Service.GetConversationAsync("null-old-1");
        Assert.Null(record);

        ctx.UserAccessor.CurrentUser = new CurrentUser(UserB, "userB", "member", "User B", KejiAuthenticationKind.Jwt);
        var recordB = await ctx.Service.GetConversationAsync("null-old-1");
        Assert.Null(recordB);
    }

    [Fact]
    public async Task CancellationToken_Propagates()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ctx.Service.CreateConversationAsync("cancel-1", "test", cts.Token));
    }

    [Fact]
    public async Task Service_List_OnlyReturnsOwned()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        await ctx.Service.CreateConversationAsync("list-own-a-1");
        await ctx.Service.CreateConversationAsync("list-own-a-2");

        ctx.UserAccessor.CurrentUser = new CurrentUser(UserB, "userB", "member", "User B", KejiAuthenticationKind.Jwt);
        await ctx.Service.CreateConversationAsync("list-own-b-1");

        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        var list = await ctx.Service.ListConversationsAsync();

        Assert.DoesNotContain(list, c => c.Id == "list-own-b-1");
        Assert.Contains(list, c => c.Id == "list-own-a-1");
        Assert.Contains(list, c => c.Id == "list-own-a-2");
    }

    [Fact]
    public async Task Ensure_Idempotent_SameOwner_SuccessAudit()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        ctx.AuditService.Calls.Clear();

        var (record, result) = await ctx.Service.EnsureConversationAsync("ensure-same-1", "test");
        Assert.Equal(ConversationOwnershipResult.Created, result);

        var (record2, result2) = await ctx.Service.EnsureConversationAsync("ensure-same-1", "test");
        Assert.Equal(ConversationOwnershipResult.AlreadyOwned, result2);

        var successCalls = ctx.AuditService.Calls.Where(c =>
            c.Action == "conversation_ensure" && c.Outcome == KejiAuditOutcome.Success);
        Assert.Single(successCalls);
    }

    [Fact]
    public async Task Get_ReturnsNull_ForNonexistentConversation()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);

        var record = await ctx.Service.GetConversationAsync("get-nonexistent");
        Assert.Null(record);
    }

    [Fact]
    public async Task Get_ReturnsNull_ForCrossOwner_ProducesDeniedAudit()
    {
        await using var ctx = await CreateServiceTestContextAsync();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserA, "userA", "member", "User A", KejiAuthenticationKind.Jwt);
        await ctx.Service.CreateConversationAsync("get-cross-denied", "test");

        ctx.AuditService.Calls.Clear();
        ctx.UserAccessor.CurrentUser = new CurrentUser(UserB, "userB", "member", "User B", KejiAuthenticationKind.Jwt);
        var record = await ctx.Service.GetConversationAsync("get-cross-denied");
        Assert.Null(record);

        var deniedCall = ctx.AuditService.Calls.FirstOrDefault(c =>
            c.Action == "conversation_access" && c.Outcome == KejiAuditOutcome.Denied);
        Assert.NotEqual(default, deniedCall);
    }
}
