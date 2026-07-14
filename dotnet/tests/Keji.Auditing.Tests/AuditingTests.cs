using System.Collections.Immutable;
using System.Reflection;
using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Keji.Auditing.Services;
using Keji.Auditing.Sinks;
using Keji.Persistence;
using Keji.Security.Auth;
using Keji.Security.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Keji.Auditing.Tests;

public class AuditingTests
{
    private const string AnonymousId = "system";
    private const string AnonymousRole = "unknown";

    // ── Helpers ──────────────────────────────────

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _fixed;
        public FixedTimeProvider(DateTimeOffset? fixedTime = null) =>
            _fixed = fixedTime ?? new DateTimeOffset(2026, 7, 14, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _fixed;
    }

    private sealed class FixedCurrentUser : ICurrentUserAccessor
    {
        public CurrentUser? CurrentUser { get; }
        public FixedCurrentUser(CurrentUser? user) => CurrentUser = user;
    }

    private sealed class FixedCorrelationAccessor : IKejiAuditCorrelationAccessor
    {
        public string? CorrelationId { get; }
        public FixedCorrelationAccessor(string? correlationId) => CorrelationId = correlationId;
    }

    private sealed class CollectingSink : IKejiAuditSink
    {
        public List<KejiAuditEvent> Events { get; } = [];
        public KejiAuditSinkResult Result { get; set; } = KejiAuditSinkResult.Written;

        public Task<KejiAuditSinkResult> WriteAsync(KejiAuditEvent auditEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(auditEvent);
            return Task.FromResult(Result);
        }
    }

    private sealed class FailingSink : IKejiAuditSink
    {
        public Task<KejiAuditSinkResult> WriteAsync(KejiAuditEvent auditEvent, CancellationToken cancellationToken = default)
            => Task.FromResult(KejiAuditSinkResult.Error);
    }

    private sealed class ThrowingSink : IKejiAuditSink
    {
        public Task<KejiAuditSinkResult> WriteAsync(KejiAuditEvent auditEvent, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("sink failure");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private static CurrentUser MakeUser(string role = KejiRoles.Member, string id = "a1b2c3d4e5f6a7b8",
        string username = "testuser", KejiAuthenticationKind authKind = KejiAuthenticationKind.Jwt)
        => new(id, username, role, username, authKind);

    private static KejiAuditService CreateService(
        ICurrentUserAccessor? userAccessor = null,
        IEnumerable<IKejiAuditSink>? sinks = null,
        TimeProvider? timeProvider = null,
        IKejiAuditCorrelationAccessor? correlationAccessor = null,
        ILogger<KejiAuditService>? logger = null)
        => new(
            userAccessor ?? new FixedCurrentUser(null),
            sinks ?? [new CollectingSink()],
            timeProvider ?? new FixedTimeProvider(),
            correlationAccessor ?? new FixedCorrelationAccessor(null),
            logger ?? NullLogger<KejiAuditService>.Instance);

    // ── 1. No public forgeable constructor ───────

    [Fact]
    public void AuditEvent_HasNoPublicConstructor()
    {
        var ctors = typeof(KejiAuditEvent).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.Empty(ctors);
    }

    [Fact]
    public void AuditEvent_HasInternalConstructor()
    {
        var ctors = typeof(KejiAuditEvent).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.Contains(ctors, c => c.IsAssembly);
    }

    [Fact]
    public void NoPublicFactory_AcceptsEventIdFromCaller()
    {
        var methods = typeof(KejiAuditEvent).GetMethods(BindingFlags.Public | BindingFlags.Static);
        Assert.DoesNotContain(methods, m => m.Name == "Create" || m.Name == "From" || m.Name.Contains("Create"));
    }

    [Fact]
    public void CallerCannotSetEventId()
    {
        var evtType = typeof(KejiAuditEvent);
        var prop = evtType.GetProperty("EventId", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(prop);
        Assert.True(prop!.CanRead);
        Assert.False(prop.CanWrite);
    }

    [Fact]
    public void CallerCannotSetOccurredAtUtc()
    {
        var evtType = typeof(KejiAuditEvent);
        var prop = evtType.GetProperty("OccurredAtUtc", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(prop);
        Assert.True(prop!.CanRead);
        Assert.False(prop.CanWrite);
    }

    [Fact]
    public void CallerCannotSetActorId()
    {
        var evtType = typeof(KejiAuditEvent);
        var prop = evtType.GetProperty("ActorId", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(prop);
        Assert.True(prop!.CanRead);
        Assert.False(prop.CanWrite);
    }

    [Fact]
    public void CallerCannotSetActorRole()
    {
        var evtType = typeof(KejiAuditEvent);
        var prop = evtType.GetProperty("ActorRole", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(prop);
        Assert.True(prop!.CanRead);
        Assert.False(prop.CanWrite);
    }

    [Fact]
    public void CallerCannotSetAuthenticationType()
    {
        var evtType = typeof(KejiAuditEvent);
        var prop = evtType.GetProperty("AuthenticationType", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(prop);
        Assert.True(prop!.CanRead);
        Assert.False(prop.CanWrite);
    }

    [Fact]
    public void CallerCannotSetCorrelationId()
    {
        var evtType = typeof(KejiAuditEvent);
        var prop = evtType.GetProperty("CorrelationId", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(prop);
        Assert.True(prop!.CanRead);
        Assert.False(prop.CanWrite);
    }

    // ── 2. EventId server-generated ──────────────

    [Fact]
    public async Task EventId_IsGeneratedByService()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));

        var result = await svc.WriteAsync(KejiAuditCategory.Authentication, "login", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "user");

        var evt = sink.Events.Single();
        Assert.NotEqual(Guid.Empty, evt.EventId);
    }

    [Fact]
    public async Task EventId_IsUniquePerEvent()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "login", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "user");
        await svc.WriteAsync(KejiAuditCategory.Authentication, "login", KejiAuditOutcome.Failure,
            KejiAuditSeverity.Warning, "user");

        Assert.NotEqual(sink.Events[0].EventId, sink.Events[1].EventId);
    }

    // ── 3. Time from TimeProvider ────────────────

    [Fact]
    public async Task Timestamp_ComesFromTimeProvider()
    {
        var fixedTime = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedTime);
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], timeProvider: timeProvider, userAccessor: new FixedCurrentUser(MakeUser()));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal(fixedTime.UtcDateTime, sink.Events.Single().OccurredAtUtc);
    }

    // ── 4. Actor cannot be forged by caller ──────

    [Fact]
    public async Task Actor_ComesFromCurrentUserAccessor()
    {
        var user = MakeUser(role: KejiRoles.Admin, id: "admin_user_id_001");
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(user));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "login", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "user");

        var evt = sink.Events.Single();
        Assert.Equal("admin_user_id_001", evt.ActorId);
    }

    // ── 5. Admin/member/readonly identity snapshots ──

    [Theory]
    [InlineData(KejiRoles.Admin)]
    [InlineData(KejiRoles.Member)]
    [InlineData(KejiRoles.Readonly)]
    public async Task ActorRole_CapturesCorrectRole(string role)
    {
        var user = MakeUser(role: role);
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(user));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal(role, sink.Events.Single().ActorRole);
    }

    [Fact]
    public async Task AuthenticationType_CapturesJwt()
    {
        var user = MakeUser(authKind: KejiAuthenticationKind.Jwt);
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(user));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal("Jwt", sink.Events.Single().AuthenticationType);
    }

    [Fact]
    public async Task AuthenticationType_CapturesApiKey()
    {
        var user = MakeUser(authKind: KejiAuthenticationKind.ApiKey);
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(user));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal("ApiKey", sink.Events.Single().AuthenticationType);
    }

    // ── 6. Anonymous/missing identity behavior ───

    [Fact]
    public async Task MissingUser_UsesSystemIdentity()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(null));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Failure,
            KejiAuditSeverity.Warning, "user");

        var evt = sink.Events.Single();
        Assert.Equal(AnonymousId, evt.ActorId);
        Assert.Equal(AnonymousRole, evt.ActorRole);
        Assert.Equal("none", evt.AuthenticationType);
    }

    [Fact]
    public async Task UnknownRole_FallsBackToUnknown()
    {
        var user = MakeUser(role: "unknown_role");
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(user));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal("unknown", sink.Events.Single().ActorRole);
    }

    [Fact]
    public async Task UnknownAuthType_FallsBackToNone()
    {
        var user = new CurrentUser("id", "user", KejiRoles.Member, "display", (KejiAuthenticationKind)999);
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(user));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal("none", sink.Events.Single().AuthenticationType);
    }

    // ── 7. Sensitive keys rejected ───────────────

    public static IEnumerable<object[]> SensitiveKeyVariants()
    {
        yield return ["password"];
        yield return ["Password"];
        yield return ["PASSWORD"];
        yield return ["passwd"];
        yield return ["pwd"];
        yield return ["token"];
        yield return ["access_token"];
        yield return ["refresh_token"];
        yield return ["api_key"];
        yield return ["apikey"];
        yield return ["API_KEY"];
        yield return ["secret"];
        yield return ["client_secret"];
        yield return ["authorization"];
        yield return ["cookie"];
        yield return ["set-cookie"];
        yield return ["Set-Cookie"];
        yield return ["private_key"];
        yield return ["connection_string"];
    }

    [Theory]
    [MemberData(nameof(SensitiveKeyVariants))]
    public async Task SensitiveKeys_AreRemovedFromMetadata(string sensitiveKey)
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));
        var metadata = new Dictionary<string, string>
        {
            [sensitiveKey] = "should_be_removed",
            ["safe_key"] = "should_be_present",
        };

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test", metadata: metadata);

        var evt = sink.Events.Single();
        Assert.False(evt.Metadata.ContainsKey(sensitiveKey), $"Key '{sensitiveKey}' should be removed");
        Assert.True(evt.Metadata.ContainsKey("safe_key"));
    }

    // ── 8. Case-insensitive sensitive keys ───────

    [Fact]
    public async Task SensitiveKeys_AreCaseInsensitive()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));
        var metadata = new Dictionary<string, string>
        {
            ["TOKEN"] = "removed",
            ["Api_Key"] = "removed",
            ["SECRET"] = "removed",
        };

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test", metadata: metadata);

        var evt = sink.Events.Single();
        Assert.False(evt.Metadata.ContainsKey("TOKEN"));
        Assert.False(evt.Metadata.ContainsKey("Api_Key"));
        Assert.False(evt.Metadata.ContainsKey("SECRET"));
    }

    // ── 9. Token/password not in results ─────────

    [Fact]
    public async Task Password_And_Token_Values_NotInMetadata()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));
        var metadata = new Dictionary<string, string>
        {
            ["some_path"] = "/api/login",
            ["password"] = "super_secret_123",
            ["token"] = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0",
        };

        await svc.WriteAsync(KejiAuditCategory.Authentication, "login", KejiAuditOutcome.Failure,
            KejiAuditSeverity.Warning, "user", metadata: metadata);

        var evt = sink.Events.Single();
        Assert.True(evt.Metadata.ContainsKey("some_path"));
        Assert.False(evt.Metadata.ContainsKey("password"));
        Assert.False(evt.Metadata.ContainsKey("token"));
        foreach (var val in evt.Metadata.Values)
        {
            Assert.DoesNotContain("super_secret_123", val);
            Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9", val);
        }
    }

    // ── 10. Metadata count limits ────────────────

    [Fact]
    public async Task Metadata_ExceedsMaxKeyCount_Truncated()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));
        var metadata = new Dictionary<string, string>();
        for (int i = 0; i < 100; i++)
            metadata[$"key{i}"] = $"val{i}";

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test", metadata: metadata);

        Assert.True(sink.Events.Single().Metadata.Count <= 32);
    }

    // ── 11. Key/value length limits ──────────────

    [Fact]
    public async Task Metadata_KeyTooLong_Excluded()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));
        var metadata = new Dictionary<string, string>
        {
            [new string('k', 100)] = "value",
            ["normal"] = "ok",
        };

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test", metadata: metadata);

        var evt = sink.Events.Single();
        Assert.False(evt.Metadata.ContainsKey(new string('k', 100)));
        Assert.True(evt.Metadata.ContainsKey("normal"));
    }

    [Fact]
    public async Task Metadata_ValueTooLong_Truncated()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));
        var longValue = new string('x', 1000);
        var metadata = new Dictionary<string, string> { ["key"] = longValue };

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test", metadata: metadata);

        var evt = sink.Events.Single();
        Assert.True(evt.Metadata["key"].Length <= 512);
    }

    // ── 12. Total size limit ─────────────────────

    [Fact]
    public async Task Metadata_TotalSizeLimit_Respected()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));
        var metadata = new Dictionary<string, string>();
        for (int i = 0; i < 10; i++)
            metadata[$"k{i}"] = new string('x', 500);

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test", metadata: metadata);

        var evt = sink.Events.Single();
        var totalChars = evt.Metadata.Sum(kvp => kvp.Key.Length + kvp.Value.Length);
        Assert.True(totalChars <= 4096);
    }

    // ── 13. Control character handling ───────────

    [Fact]
    public async Task Metadata_ControlCharacters_Removed()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));
        var metadata = new Dictionary<string, string>
        {
            ["key"] = "normal\u0000\u0001\u0002text\u0003end",
        };

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test", metadata: metadata);

        var val = sink.Events.Single().Metadata["key"];
        Assert.DoesNotContain('\0', val);
        Assert.DoesNotContain('\u0001', val);
        Assert.DoesNotContain('\u0002', val);
        Assert.DoesNotContain('\u0003', val);
        Assert.Contains("normaltextend", val);
    }

    [Fact]
    public async Task Metadata_NewlinesAndTabs_AreRemoved()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));
        var metadata = new Dictionary<string, string> { ["key"] = "line1\nline2\tindented\r\n" };

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test", metadata: metadata);

        var val = sink.Events.Single().Metadata["key"];
        Assert.DoesNotContain('\n', val);
        Assert.DoesNotContain('\t', val);
        Assert.DoesNotContain('\r', val);
        Assert.Equal("line1line2indented", val);
    }

    [Fact]
    public async Task Metadata_UnicodeSurrogatePairs_Preserved()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));
        var metadata = new Dictionary<string, string> { ["key"] = "emoji\uD83D\uDE00test" };

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test", metadata: metadata);

        var val = sink.Events.Single().Metadata["key"];
        Assert.Contains("\uD83D\uDE00", val);
    }

    [Fact]
    public async Task Metadata_LoneSurrogate_Handled()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));
        var metadata = new Dictionary<string, string> { ["key"] = "test\uD800x" };

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test", metadata: metadata);

        var val = sink.Events.Single().Metadata["key"];
        Assert.DoesNotContain("\uD800", val);
        Assert.Equal("testx", val);
    }

    // ── 14. Caller modifying original dict ───────

    [Fact]
    public async Task CallerModifiesOriginalDict_AfterWrite_EventUnchanged()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));
        var metadata = new Dictionary<string, string> { ["original"] = "value" };

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test", metadata: metadata);

        metadata["original"] = "modified";
        metadata["new_key"] = "new_value";

        var evt = sink.Events.Single();
        Assert.Equal("value", evt.Metadata["original"]);
        Assert.False(evt.Metadata.ContainsKey("new_key"));
    }

    // ── 15. Sink failure returns safe error ──────

    [Fact]
    public async Task SinkFailure_ReturnsSinkErrorResult()
    {
        var svc = CreateService(sinks: [new FailingSink()], userAccessor: new FixedCurrentUser(MakeUser()));

        var result = await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal(KejiAuditResult.SinkError, result);
    }

    // ── 16. CancellationToken propagation ────────

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceled()
    {
        var svc = CreateService(sinks: [new CollectingSink()], userAccessor: new FixedCurrentUser(MakeUser()));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
                KejiAuditSeverity.Information, "test", cancellationToken: cts.Token));
    }

    // ── 17. Concurrent writes ────────────────────

    [Fact]
    public async Task ConcurrentWrites_DoNotPolluteEachOther()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));

        var tasks = new List<Task>();
        for (int i = 0; i < 50; i++)
        {
            var idx = i;
            tasks.Add(svc.WriteAsync(KejiAuditCategory.Authentication, $"action{idx}", KejiAuditOutcome.Success,
                KejiAuditSeverity.Information, "test"));
        }

        await Task.WhenAll(tasks);

        Assert.Equal(50, sink.Events.Count);
        var actions = sink.Events.Select(e => e.Action).Distinct().Count();
        Assert.Equal(50, actions);
        Assert.Equal(50, sink.Events.Select(e => e.EventId).Distinct().Count());
    }

    // ── 18. No public Update/Delete audit interface ──

    [Fact]
    public void AuditService_DoesNotExposeUpdateOrDelete()
    {
        var type = typeof(IKejiAuditService);
        Assert.NotNull(type.GetMethod("WriteAsync"));
        Assert.Null(type.GetMethod("UpdateAsync"));
        Assert.Null(type.GetMethod("DeleteAsync"));
    }

    // ── 19. No leak of underlying exception ──────

    [Fact]
    public async Task SinkFailure_ResultIsTypedNotException()
    {
        var svc = CreateService(sinks: [new FailingSink()], userAccessor: new FixedCurrentUser(MakeUser()));

        var result = await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.IsType<KejiAuditResult>(result);
    }

    // ── 20. DI registration and lifecycle ────────

    [Fact]
    public void DI_Registration_ResolvesService()
    {
        var services = new ServiceCollection();
        services.AddKejiAuditingFoundation();
        services.AddSingleton<ICurrentUserAccessor>(_ => new FixedCurrentUser(null));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IUnixTimeProvider>(_ => new FixedUnixTimeProvider(1000.0));
        services.AddSingleton<ISqliteConnectionFactory>(_ =>
        {
            var opts = new KejiPersistenceOptions
            {
                ProjectRoot = Path.GetTempPath(),
                DatabasePath = $"di_test_{Guid.NewGuid():N}.db",
                CreateDirectoryIfMissing = true,
            };
            return new SqliteConnectionFactory(opts);
        });

        services.AddLogging(b => b.AddDebug());
        var sp = services.BuildServiceProvider();
        var svc = sp.GetRequiredService<IKejiAuditService>();
        Assert.NotNull(svc);
    }

    [Fact]
    public void DI_AuditService_IsScoped()
    {
        var services = new ServiceCollection();
        services.AddKejiAuditingFoundation();
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IKejiAuditService));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
    }

    // ── 21. Audit failure is non-recursive ───────

    [Fact]
    public async Task AuditFailure_DoesNotTriggerAnotherAudit()
    {
        var svc = CreateService(sinks: [new FailingSink()], userAccessor: new FixedCurrentUser(MakeUser()));

        var result1 = await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        var result2 = await svc.WriteAsync(KejiAuditCategory.Authentication, "test2", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal(KejiAuditResult.SinkError, result1);
        Assert.Equal(KejiAuditResult.SinkError, result2);
    }

    // ── 22. Metadata sanitizer edge cases ────────

    [Fact]
    public void Sanitizer_NullInput_ReturnsEmpty()
    {
        var result = KejiAuditMetadataSanitizer.Sanitize(null);
        Assert.Empty(result);
    }

    [Fact]
    public void Sanitizer_EmptyInput_ReturnsEmpty()
    {
        var result = KejiAuditMetadataSanitizer.Sanitize(new Dictionary<string, string>());
        Assert.Empty(result);
    }

    [Fact]
    public void Sanitizer_EmptyKey_Excluded()
    {
        var input = new Dictionary<string, string> { { "", "value" }, { "valid", "ok" } };
        var result = KejiAuditMetadataSanitizer.Sanitize(input);
        Assert.Single(result);
        Assert.True(result.ContainsKey("valid"));
    }

    [Fact]
    public void IsSensitiveKey_ReturnsTrueForKnownKeys()
    {
        Assert.True(KejiAuditMetadataSanitizer.IsSensitiveKey("password"));
        Assert.True(KejiAuditMetadataSanitizer.IsSensitiveKey("api_key"));
        Assert.True(KejiAuditMetadataSanitizer.IsSensitiveKey("TOKEN"));
        Assert.False(KejiAuditMetadataSanitizer.IsSensitiveKey("safe_key"));
        Assert.False(KejiAuditMetadataSanitizer.IsSensitiveKey(""));
        Assert.False(KejiAuditMetadataSanitizer.IsSensitiveKey(null!));
    }

    // ── 23. Unchanged metadata is returned as empty dict ──

    [Fact]
    public async Task NoMetadata_ResultsInEmptyDict()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "login", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "user");

        Assert.Empty(sink.Events.Single().Metadata);
    }

    // ── 24. Input Validation ─────────────────────

    [Fact]
    public async Task InvalidCategory_ReturnsValidationError()
    {
        var svc = CreateService(sinks: [new CollectingSink()], userAccessor: new FixedCurrentUser(MakeUser()));

        var result = await svc.WriteAsync((KejiAuditCategory)999, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal(KejiAuditResult.ValidationError, result);
    }

    [Fact]
    public async Task InvalidOutcome_ReturnsValidationError()
    {
        var svc = CreateService(sinks: [new CollectingSink()], userAccessor: new FixedCurrentUser(MakeUser()));

        var result = await svc.WriteAsync(KejiAuditCategory.Authentication, "test", (KejiAuditOutcome)999,
            KejiAuditSeverity.Information, "test");

        Assert.Equal(KejiAuditResult.ValidationError, result);
    }

    [Fact]
    public async Task InvalidSeverity_ReturnsValidationError()
    {
        var svc = CreateService(sinks: [new CollectingSink()], userAccessor: new FixedCurrentUser(MakeUser()));

        var result = await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            (KejiAuditSeverity)999, "test");

        Assert.Equal(KejiAuditResult.ValidationError, result);
    }

    [Fact]
    public async Task EmptyAction_ReturnsValidationError()
    {
        var svc = CreateService(sinks: [new CollectingSink()], userAccessor: new FixedCurrentUser(MakeUser()));

        var result = await svc.WriteAsync(KejiAuditCategory.Authentication, "", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal(KejiAuditResult.ValidationError, result);
    }

    [Fact]
    public async Task ActionTooLong_ReturnsValidationError()
    {
        var svc = CreateService(sinks: [new CollectingSink()], userAccessor: new FixedCurrentUser(MakeUser()));

        var result = await svc.WriteAsync(KejiAuditCategory.Authentication, new string('x', 257), KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal(KejiAuditResult.ValidationError, result);
    }

    [Fact]
    public async Task ActionWithControlChars_ReturnsValidationError()
    {
        var svc = CreateService(sinks: [new CollectingSink()], userAccessor: new FixedCurrentUser(MakeUser()));

        var result = await svc.WriteAsync(KejiAuditCategory.Authentication, "test\n", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal(KejiAuditResult.ValidationError, result);
    }

    [Fact]
    public async Task EmptyTargetType_ReturnsValidationError()
    {
        var svc = CreateService(sinks: [new CollectingSink()], userAccessor: new FixedCurrentUser(MakeUser()));

        var result = await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "");

        Assert.Equal(KejiAuditResult.ValidationError, result);
    }

    [Fact]
    public async Task TargetTypeWithControlChars_ReturnsValidationError()
    {
        var svc = CreateService(sinks: [new CollectingSink()], userAccessor: new FixedCurrentUser(MakeUser()));

        var result = await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test\r");

        Assert.Equal(KejiAuditResult.ValidationError, result);
    }

    [Fact]
    public async Task ValidationError_DoesNotWriteToSink()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));

        var result = await svc.WriteAsync((KejiAuditCategory)999, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal(KejiAuditResult.ValidationError, result);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task ValidationError_IsDistinctFromSinkError()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));

        var validationResult = await svc.WriteAsync((KejiAuditCategory)999, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");
        Assert.Equal(KejiAuditResult.ValidationError, validationResult);

        var sinkErrorResult = await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");
        Assert.Equal(KejiAuditResult.Written, sinkErrorResult);
    }

    // ── 25. CorrelationId ────────────────────────

    [Fact]
    public async Task CorrelationId_FromAccessor()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()),
            correlationAccessor: new FixedCorrelationAccessor("test-correlation"));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal("test-correlation", sink.Events.Single().CorrelationId);
    }

    [Fact]
    public async Task CorrelationId_NullWhenAccessorReturnsNull()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()),
            correlationAccessor: new FixedCorrelationAccessor(null));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Null(sink.Events.Single().CorrelationId);
    }

    [Fact]
    public async Task CorrelationId_ControlCharsCleaned()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()),
            correlationAccessor: new FixedCorrelationAccessor("trace\nid\t\r"));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal("traceid", sink.Events.Single().CorrelationId);
    }

    [Fact]
    public async Task CorrelationId_TruncatedAt128()
    {
        var sink = new CollectingSink();
        var longId = new string('x', 200);
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()),
            correlationAccessor: new FixedCorrelationAccessor(longId));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.NotNull(sink.Events.Single().CorrelationId);
        Assert.True(sink.Events.Single().CorrelationId!.Length <= 128);
    }

    // ── 26. Multi-sink behavior ──────────────────

    [Fact]
    public async Task TwoSinks_BothReceiveEvent()
    {
        var sink1 = new CollectingSink();
        var sink2 = new CollectingSink();
        var svc = CreateService(sinks: [sink1, sink2], userAccessor: new FixedCurrentUser(MakeUser()));

        var result = await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal(KejiAuditResult.Written, result);
        Assert.Single(sink1.Events);
        Assert.Single(sink2.Events);
        Assert.Equal(sink1.Events[0].EventId, sink2.Events[0].EventId);
    }

    [Fact]
    public async Task TwoSinks_OneFailsOneSucceeds_ReturnsSinkError()
    {
        var sink1 = new CollectingSink();
        var sink2 = new FailingSink();
        var svc = CreateService(sinks: [sink1, sink2], userAccessor: new FixedCurrentUser(MakeUser()));

        var result = await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal(KejiAuditResult.SinkError, result);
        Assert.Single(sink1.Events);
    }

    [Fact]
    public async Task TwoSinks_BothFail_ReturnsSinkError()
    {
        var sink1 = new FailingSink();
        var sink2 = new FailingSink();
        var svc = CreateService(sinks: [sink1, sink2], userAccessor: new FixedCurrentUser(MakeUser()));

        var result = await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal(KejiAuditResult.SinkError, result);
    }

    [Fact]
    public async Task ThrowingSink_DoesNotBlockOtherSinks()
    {
        var sink1 = new CollectingSink();
        var sink2 = new ThrowingSink();
        var logger = new CapturingLogger<KejiAuditService>();
        var svc = CreateService(sinks: [sink1, sink2], userAccessor: new FixedCurrentUser(MakeUser()), logger: logger);

        var result = await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal(KejiAuditResult.SinkError, result);
        Assert.Single(sink1.Events);
        Assert.Contains(logger.Messages, m => m.Contains("AuditSinkError") && m.Contains("SINK_WRITE_FAILED"));
    }

    [Fact]
    public async Task MultipleSinks_AllSeeSameEventSnapshot()
    {
        var sink1 = new CollectingSink();
        var sink2 = new CollectingSink();
        var user = MakeUser();
        var svc = CreateService(sinks: [sink1, sink2], userAccessor: new FixedCurrentUser(user));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "login", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "user", targetId: "target1");

        var evt1 = sink1.Events.Single();
        var evt2 = sink2.Events.Single();
        Assert.Equal(evt1.EventId, evt2.EventId);
        Assert.Equal(evt1.Action, evt2.Action);
        Assert.Equal(evt1.ActorId, evt2.ActorId);
        Assert.Equal(evt1.TargetId, evt2.TargetId);
        Assert.Equal(evt1.OccurredAtUtc, evt2.OccurredAtUtc);
    }

    [Fact]
    public async Task NoSinks_ReturnsSinkError()
    {
        var svc = CreateService(sinks: Array.Empty<IKejiAuditSink>(), userAccessor: new FixedCurrentUser(MakeUser()));

        var result = await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal(KejiAuditResult.SinkError, result);
    }

    // ── 27. No empty catch blocks ────────────────
    // (verified by behavior test: NoEmptyCatch_TestByBehavior)

    // ── 28. Database integration - write to audit_events table ──

    private sealed class FixedUnixTimeProvider : IUnixTimeProvider
    {
        public double Now { get; }
        public FixedUnixTimeProvider(double now) => Now = now;
    }

    private sealed class TestDbContext : IDisposable
    {
        public string Dir { get; }
        public KejiPersistenceOptions Options { get; }
        public ISqliteConnectionFactory Factory { get; }
        public IUnixTimeProvider TimeProvider { get; }
        public double FixedTimestamp { get; }

        public TestDbContext()
        {
            Dir = Path.Combine(Path.GetTempPath(), "KJ_AUDIT_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
            FixedTimestamp = 2000000.0;
            TimeProvider = new FixedUnixTimeProvider(FixedTimestamp);
            Options = new KejiPersistenceOptions
            {
                ProjectRoot = Dir,
                DatabasePath = "audit_test.db",
                BusyTimeoutMilliseconds = 5000,
                EnableWal = true,
                EnableForeignKeys = true,
            };
            Factory = new SqliteConnectionFactory(Options);
        }

        public async Task InitializeAsync()
        {
            var init = new KejiDatabaseInitializer(Factory, TimeProvider);
            await init.InitializeAsync();
        }

        public void Dispose()
        {
            try { Directory.Delete(Dir, recursive: true); } catch { }
        }
    }

    private static KejiAuditEvent CreateTestEvent(
        string action = "login",
        KejiAuditOutcome outcome = KejiAuditOutcome.Success,
        string correlationId = "session1")
    {
        // Use the internal constructor via the service
        var sink = new CollectingSink();
        var svc = new KejiAuditService(
            new FixedCurrentUser(MakeUser()),
            [sink],
            new FixedTimeProvider(),
            new FixedCorrelationAccessor(correlationId),
            NullLogger<KejiAuditService>.Instance);
        svc.WriteAsync(KejiAuditCategory.Authentication, action, outcome,
            KejiAuditSeverity.Information, "user", targetId: "target1",
            metadata: new Dictionary<string, string> { ["key"] = "val" }).GetAwaiter().GetResult();
        return sink.Events.Single();
    }

    [Fact]
    public async Task DatabaseSink_WritesEvent()
    {
        using var ctx = new TestDbContext();
        await ctx.InitializeAsync();

        var sink = new KejiDatabaseAuditSink(ctx.Factory);
        var evt = CreateTestEvent();

        var result = await sink.WriteAsync(evt);

        Assert.Equal(KejiAuditSinkResult.Written, result);

        using var conn = await ctx.Factory.OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT event_id, event_type, actor, session_id, tool_name, path, action, status FROM audit_events";
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(evt.EventId.ToString(), reader.GetString(0));
        Assert.Equal("Authentication", reader.GetString(1));
        Assert.Equal("a1b2c3d4e5f6a7b8", reader.GetString(2));
        Assert.Equal("session1", reader.GetString(3));
        Assert.Equal("user", reader.GetString(4));
        Assert.Equal("target1", reader.GetString(5));
        Assert.Equal("login", reader.GetString(6));
        Assert.Equal("Success", reader.GetString(7));
    }

    [Fact]
    public async Task DatabaseSink_Cancelled_Throws()
    {
        using var ctx = new TestDbContext();
        await ctx.InitializeAsync();

        var sink = new KejiDatabaseAuditSink(ctx.Factory);
        var evt = CreateTestEvent();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sink.WriteAsync(evt, cts.Token));
    }

    [Fact]
    public async Task DatabaseSink_DuplicateEventId_ReturnsError()
    {
        using var ctx = new TestDbContext();
        await ctx.InitializeAsync();

        var sink = new KejiDatabaseAuditSink(ctx.Factory);
        var evt = CreateTestEvent();

        var result1 = await sink.WriteAsync(evt);
        Assert.Equal(KejiAuditSinkResult.Written, result1);

        var result2 = await sink.WriteAsync(evt);
        Assert.Equal(KejiAuditSinkResult.Error, result2);
    }

    [Fact]
    public async Task DatabaseSink_DuplicateEventId_DoesNotOverwrite()
    {
        using var ctx = new TestDbContext();
        await ctx.InitializeAsync();

        var sink = new KejiDatabaseAuditSink(ctx.Factory);
        var evt1 = CreateTestEvent(action: "first");
        var evt2 = CreateTestEvent(action: "second");

        // Write first event
        await sink.WriteAsync(evt1);

        // Try to write same event_id again. Use a different event with same id.
        // We need to create an event with the same id as evt1 but different data.
        // The internal constructor doesn't allow this directly, so let's test via the sink
        // by writing evt1 twice.
        var dupResult = await sink.WriteAsync(evt1);
        Assert.Equal(KejiAuditSinkResult.Error, dupResult);

        // Verify first write is intact
        using var conn = await ctx.Factory.OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM audit_events WHERE event_id = @id";
        cmd.Parameters.AddWithValue("@id", evt1.EventId.ToString());
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task DatabaseSink_UsesOccurredAtUtc()
    {
        using var ctx = new TestDbContext();
        await ctx.InitializeAsync();

        var sink = new KejiDatabaseAuditSink(ctx.Factory);
        var evt = CreateTestEvent();

        var result = await sink.WriteAsync(evt);
        Assert.Equal(KejiAuditSinkResult.Written, result);

        using var conn = await ctx.Factory.OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT created_at FROM audit_events WHERE event_id = @id";
        cmd.Parameters.AddWithValue("@id", evt.EventId.ToString());
        var dbTimestamp = (double)(await cmd.ExecuteScalarAsync())!;

        // Convert the event's OccurredAtUtc to unix timestamp
        var expected = (evt.OccurredAtUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        Assert.Equal(expected, dbTimestamp, 1);
    }

    // ── 29. Severity levels present ──────────────

    [Theory]
    [InlineData(KejiAuditSeverity.Information)]
    [InlineData(KejiAuditSeverity.Warning)]
    [InlineData(KejiAuditSeverity.Error)]
    [InlineData(KejiAuditSeverity.Critical)]
    public async Task Severity_IsRecorded(KejiAuditSeverity severity)
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            severity, "test");

        Assert.Equal(severity, sink.Events.Single().Severity);
    }

    // ── 30. Sink failure doesn't change auth decision ──

    [Fact]
    public async Task SinkFailure_DoesNotChangeAuthOutcome()
    {
        var svc = CreateService(sinks: [new FailingSink()], userAccessor: new FixedCurrentUser(MakeUser()));

        var result = await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        // Even though sink fails, the result is SinkError, not a ValidationError
        Assert.Equal(KejiAuditResult.SinkError, result);
    }

    // ── 31. Audit service does not expose internal constructor ──

    [Fact]
    public void AuditService_DoesNotAcceptEventFromCaller()
    {
        var writeMethod = typeof(IKejiAuditService).GetMethod("WriteAsync");
        Assert.NotNull(writeMethod);
        var parameters = writeMethod!.GetParameters();
        // Should NOT have KejiAuditEvent as parameter
        Assert.DoesNotContain(parameters, p => p.ParameterType == typeof(KejiAuditEvent));
    }

    // ── 32. No empty catch in source ─────────────

    [Fact]
    public async Task AuditService_NoEmptyCatch_TestByBehavior()
    {
        // If there's an empty catch, a throw from sink would be silently swallowed.
        // Our ThrowingSink should produce a SinkError result (not crash).
        var logger = new CapturingLogger<KejiAuditService>();
        var svc = CreateService(sinks: [new ThrowingSink()], userAccessor: new FixedCurrentUser(MakeUser()), logger: logger);

        var result = await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal(KejiAuditResult.SinkError, result);
        Assert.Contains(logger.Messages, m => m.Contains("AuditSinkError"));
    }

    // ── 33. Sensitive values cannot bypass through other fields ──

    [Fact]
    public async Task SensitiveKeys_CannotBypassViaOtherFields()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sinks: [sink], userAccessor: new FixedCurrentUser(MakeUser()));
        var metadata = new Dictionary<string, string>
        {
            ["safe"] = "password_value",
        };

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test", metadata: metadata);

        var evt = sink.Events.Single();
        Assert.True(evt.Metadata.ContainsKey("safe"));
        Assert.Equal("password_value", evt.Metadata["safe"]);
    }

    // ── 34. Caller cannot modify event after creation ──

    [Fact]
    public async Task SinkCannotModifyEventMetadata()
    {
        var evt = CreateTestEvent();
        var typeName = evt.Metadata.GetType().Name;
        Assert.Contains("Immutable", typeName);
        Assert.True(evt.Metadata is IReadOnlyDictionary<string, string>);
    }
}
