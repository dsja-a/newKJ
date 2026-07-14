using System.Collections.Immutable;
using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Keji.Auditing.Services;
using Keji.Auditing.Sinks;
using Keji.Persistence;
using Keji.Security.Auth;
using Keji.Security.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

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

    private static CurrentUser MakeUser(string role = KejiRoles.Member, string id = "a1b2c3d4e5f6a7b8",
        string username = "testuser", KejiAuthenticationKind authKind = KejiAuthenticationKind.Jwt)
        => new(id, username, role, username, authKind);

    private static KejiAuditService CreateService(ICurrentUserAccessor? userAccessor = null, IKejiAuditSink? sink = null, TimeProvider? timeProvider = null)
        => new(
            userAccessor ?? new FixedCurrentUser(null),
            sink ?? new CollectingSink(),
            timeProvider ?? new FixedTimeProvider());

    // ── 1. Event model immutability ──────────────

    [Fact]
    public void AuditEvent_PropertiesAreImmutable()
    {
        var metadata = new Dictionary<string, string> { ["key"] = "value" }.ToImmutableDictionary();
        var evt = new KejiAuditEvent(
            Guid.NewGuid(), DateTime.UtcNow, KejiAuditCategory.Authentication, "login",
            KejiAuditOutcome.Success, KejiAuditSeverity.Information,
            "actor", "admin", "Jwt", "corr-id", "user", "target-id", metadata);

        var type = evt.GetType();
        foreach (var prop in type.GetProperties())
        {
            Assert.True(prop.CanRead);
            Assert.False(prop.CanWrite);
        }
    }

    // ── 2. EventId server-generated ──────────────

    [Fact]
    public async Task EventId_IsGeneratedByService()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(MakeUser()));

        var result = await svc.WriteAsync(KejiAuditCategory.Authentication, "login", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "user");

        var evt = sink.Events.Single();
        Assert.NotEqual(Guid.Empty, evt.EventId);
    }

    [Fact]
    public async Task EventId_IsUniquePerEvent()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(MakeUser()));

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
        var svc = CreateService(sink: sink, timeProvider: timeProvider, userAccessor: new FixedCurrentUser(MakeUser()));

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
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(user));

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
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(user));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal(role, sink.Events.Single().ActorRole);
    }

    [Fact]
    public async Task AuthenticationType_CapturesJwt()
    {
        var user = MakeUser(authKind: KejiAuthenticationKind.Jwt);
        var sink = new CollectingSink();
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(user));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal("Jwt", sink.Events.Single().AuthenticationType);
    }

    [Fact]
    public async Task AuthenticationType_CapturesApiKey()
    {
        var user = MakeUser(authKind: KejiAuthenticationKind.ApiKey);
        var sink = new CollectingSink();
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(user));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal("ApiKey", sink.Events.Single().AuthenticationType);
    }

    // ── 6. Anonymous/missing identity behavior ───

    [Fact]
    public async Task MissingUser_UsesSystemIdentity()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(null));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Failure,
            KejiAuditSeverity.Warning, "user");

        var evt = sink.Events.Single();
        Assert.Equal(AnonymousId, evt.ActorId);
        Assert.Equal(AnonymousRole, evt.ActorRole);
        Assert.Equal("none", evt.AuthenticationType);
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
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(MakeUser()));
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
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(MakeUser()));
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
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(MakeUser()));
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
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(MakeUser()));
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
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(MakeUser()));
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
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(MakeUser()));
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
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(MakeUser()));
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
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(MakeUser()));
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
    public async Task Metadata_NewlinesAndTabsPreserved()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(MakeUser()));
        var metadata = new Dictionary<string, string> { ["key"] = "line1\nline2\tindented" };

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test", metadata: metadata);

        var val = sink.Events.Single().Metadata["key"];
        Assert.Contains('\n', val);
        Assert.Contains('\t', val);
    }

    // ── 14. Caller modifying original dict ───────

    [Fact]
    public async Task CallerModifiesOriginalDict_AfterWrite_EventUnchanged()
    {
        var sink = new CollectingSink();
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(MakeUser()));
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
        var svc = CreateService(sink: new FailingSink(), userAccessor: new FixedCurrentUser(MakeUser()));

        var result = await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "test");

        Assert.Equal(KejiAuditResult.SinkError, result);
    }

    // ── 16. CancellationToken propagation ────────

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceled()
    {
        var svc = CreateService(sink: new CollectingSink(), userAccessor: new FixedCurrentUser(MakeUser()));
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
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(MakeUser()));

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
        var svc = CreateService(sink: new FailingSink(), userAccessor: new FixedCurrentUser(MakeUser()));

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

    [Fact]
    public void DI_AuditSink_IsSingleton()
    {
        var services = new ServiceCollection();
        services.AddKejiAuditingFoundation();
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IKejiAuditSink));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Singleton, descriptor!.Lifetime);
    }

    // ── 21. Audit failure is non-recursive ───────

    [Fact]
    public async Task AuditFailure_DoesNotTriggerAnotherAudit()
    {
        var svc = CreateService(sink: new FailingSink(), userAccessor: new FixedCurrentUser(MakeUser()));

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
    public void Sanitizer_NullKey_Excluded()
    {
        var input = new Dictionary<string, string> { { "valid", "ok" } };
        var result = KejiAuditMetadataSanitizer.Sanitize(input);
        Assert.Single(result);
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
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(MakeUser()));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "login", KejiAuditOutcome.Success,
            KejiAuditSeverity.Information, "user");

        Assert.Empty(sink.Events.Single().Metadata);
    }

    // ── 24. Database integration - write to audit_events table ──

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

    [Fact]
    public async Task DatabaseSink_WritesEvent()
    {
        using var ctx = new TestDbContext();
        await ctx.InitializeAsync();

        var sink = new KejiDatabaseAuditSink(ctx.Factory, ctx.TimeProvider);
        var evt = new KejiAuditEvent(
            Guid.NewGuid(), DateTime.UtcNow, KejiAuditCategory.Authentication, "login",
            KejiAuditOutcome.Success, KejiAuditSeverity.Information,
            "actor1", "admin", "Jwt", "session1", "user", "target1",
            new Dictionary<string, string> { ["key"] = "val" });

        var result = await sink.WriteAsync(evt);

        Assert.Equal(KejiAuditSinkResult.Written, result);

        using var conn = await ctx.Factory.OpenConnectionAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT event_type, actor, session_id, tool_name, path, action, status FROM audit_events";
        using var reader = await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Authentication", reader.GetString(0));
        Assert.Equal("actor1", reader.GetString(1));
        Assert.Equal("session1", reader.GetString(2));
        Assert.Equal("user", reader.GetString(3));
        Assert.Equal("target1", reader.GetString(4));
        Assert.Equal("login", reader.GetString(5));
        Assert.Equal("Success", reader.GetString(6));
    }

    [Fact]
    public async Task DatabaseSink_Cancelled_Throws()
    {
        using var ctx = new TestDbContext();
        await ctx.InitializeAsync();

        var sink = new KejiDatabaseAuditSink(ctx.Factory, ctx.TimeProvider);
        var evt = new KejiAuditEvent(
            Guid.NewGuid(), DateTime.UtcNow, KejiAuditCategory.Authentication, "test",
            KejiAuditOutcome.Success, KejiAuditSeverity.Information,
            "actor", "admin", "Jwt", null, "test", null,
            ImmutableDictionary<string, string>.Empty);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            sink.WriteAsync(evt, cts.Token));
    }

    // ── 25. Severity levels present ──────────────

    [Theory]
    [InlineData(KejiAuditSeverity.Information)]
    [InlineData(KejiAuditSeverity.Warning)]
    [InlineData(KejiAuditSeverity.Error)]
    [InlineData(KejiAuditSeverity.Critical)]
    public async Task Severity_IsRecorded(KejiAuditSeverity severity)
    {
        var sink = new CollectingSink();
        var svc = CreateService(sink: sink, userAccessor: new FixedCurrentUser(MakeUser()));

        await svc.WriteAsync(KejiAuditCategory.Authentication, "test", KejiAuditOutcome.Success,
            severity, "test");

        Assert.Equal(severity, sink.Events.Single().Severity);
    }
}
