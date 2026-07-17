using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Keji.Providers;
using Keji.Security.Auth;
using Keji.Security.Authorization;
using Keji.SmartQuery;

namespace Keji.SmartQuery.Tests;

public sealed class SmartQueryServiceTests
{
    private static readonly JsonSerializerOptions Json = new()
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false) } };

    [Fact]
    public async Task StreamHasDeterministicSuccessfulTerminalProtocol()
    {
        var fixture = Fixture.Create();
        var events = await Collect(fixture.Service.RunStreamAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Equal([
            KejiSmartQueryEventType.RunStarted, KejiSmartQueryEventType.PlanAccepted,
            KejiSmartQueryEventType.QueryCompleted, KejiSmartQueryEventType.RunCompleted
        ], events.Select(static e => e.Type));
        Assert.Equal([1L, 2, 3, 4], events.Select(static e => e.Sequence));
        Assert.Equal(KejiSmartQueryStatus.Completed, events[^1].Status);
        Assert.NotNull(events[^1].Result);
    }

    [Fact]
    public async Task RunAsyncOnlyAggregatesStreamTerminalResult()
    {
        var fixture = Fixture.Create();
        var result = await fixture.Service.RunAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal(KejiSmartQueryStatus.Completed, result.Status);
        Assert.Equal("value", Assert.Single(result.Columns));
        Assert.Equal("alpha", Assert.Single(result.Rows).Values[0].Text);
    }

    public static TheoryData<string> InvalidRunIds() => new()
    {
        "", "abc", "0123456789abcdef0123456789abcde",
        "0123456789abcdef0123456789abcdef0", "0123456789ABCDEF0123456789ABCDEF",
        "g123456789abcdef0123456789abcdef", " 123456789abcdef0123456789abcdef",
        "0123456789abcdef0123456789abcde "
    };

    [Theory]
    [MemberData(nameof(InvalidRunIds))]
    public async Task InvalidRunIdIsRejectedWithoutRepair(string runId)
    {
        var fixture = Fixture.Create();
        var events = await Collect(fixture.Service.RunStreamAsync(
            Request() with { RunId = runId }, TestContext.Current.CancellationToken));
        Assert.Equal([KejiSmartQueryEventType.Error, KejiSmartQueryEventType.RunCompleted],
            events.Select(static e => e.Type));
        Assert.All(events, static e => Assert.Equal("", e.RunId));
        Assert.Empty(fixture.Provider.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task RequestedLimitOutsideSystemBoundaryIsRejected(int limit)
    {
        var fixture = Fixture.Create();
        var result = await fixture.Service.RunAsync(
            Request() with { RequestedLimit = limit }, TestContext.Current.CancellationToken);
        Assert.Equal(KejiSmartQueryStatus.InvalidRequest, result.Status);
        Assert.Empty(fixture.Provider.Requests);
    }

    [Fact]
    public async Task RequestUsesServerControlledProviderModelAndSummaryPolicy()
    {
        var fixture = Fixture.Create(includeSummary: true);
        fixture.Provider.SecondContent = "safe summary";
        var result = await fixture.Service.RunAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal("safe summary", result.Summary);
        Assert.Equal(2, fixture.Provider.Requests.Count);
        Assert.All(fixture.Provider.Requests, static r => Assert.Equal("server-model", r.Model));
    }

    [Fact]
    public async Task UnauthenticatedStopsBeforeCatalogAndProvider()
    {
        var fixture = Fixture.Create();
        fixture.Users.CurrentUser = null;
        var result = await fixture.Service.RunAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal(KejiSmartQueryStatus.Unauthenticated, result.Status);
        Assert.Equal(0, fixture.Catalog.Calls);
        Assert.Empty(fixture.Provider.Requests);
    }

    [Fact]
    public async Task BothPermissionsAreRequired()
    {
        var fixture = Fixture.Create();
        fixture.Authorization.Allowed = false;
        var result = await fixture.Service.RunAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal(KejiSmartQueryStatus.Forbidden, result.Status);
        Assert.Equal([KejiPermission.SmartQueryExecute, KejiPermission.DatabaseRead],
            fixture.Authorization.Last);
    }

    [Fact]
    public async Task CrossUserOrMissingDataSourceStopsBeforeProvider()
    {
        var fixture = Fixture.Create();
        fixture.Catalog.Source = null;
        var result = await fixture.Service.RunAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal(KejiSmartQueryStatus.DataSourceNotFound, result.Status);
        Assert.Empty(fixture.Provider.Requests);
        Assert.Equal(0, fixture.Executor.Calls);
    }

    public static TheoryData<string> RejectedModelOutputs() => new()
    {
        "SELECT * FROM items", "```json\n{}\n```", "{}", "[]", "null",
        "{\"From\":\"items\",\"From\":\"orders\"}", "{\"from\":\"items\"}",
        "{\"From\":\"missing\",\"Joins\":[],\"Select\":[],\"Where\":null,\"GroupBy\":[],\"OrderBy\":[],\"Limit\":1}"
    };

    [Theory]
    [MemberData(nameof(RejectedModelOutputs))]
    public async Task RawSqlMalformedAndDuplicatePlansAreRejected(string output)
    {
        var fixture = Fixture.Create();
        fixture.Provider.Content = output;
        var result = await fixture.Service.RunAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal(KejiSmartQueryStatus.PlanRejected, result.Status);
        Assert.Equal(0, fixture.Executor.Calls);
    }

    [Fact]
    public async Task SensitiveMetadataNeverEntersPrompt()
    {
        var fixture = Fixture.Create();
        fixture.Catalog.Source = fixture.Catalog.Source! with
        {
            Tables = [new("items",
                [new("value", KejiSmartQueryColumnType.String),
                 new("password_hash", KejiSmartQueryColumnType.String, Sensitive: true),
                 new("internal_note", KejiSmartQueryColumnType.String, QueryEnabled: false)])]
        };
        await fixture.Service.RunAsync(Request(), TestContext.Current.CancellationToken);
        var prompt = Assert.Single(fixture.Provider.Requests).Messages[0].Content;
        Assert.Contains("\"value\"", prompt);
        Assert.DoesNotContain("password_hash", prompt);
        Assert.DoesNotContain("internal_note", prompt);
    }

    [Fact]
    public async Task ForeignKeyUsingSensitiveColumnNeverEntersPrompt()
    {
        var fixture = Fixture.Create();
        fixture.Catalog.Source = fixture.Catalog.Source! with
        {
            Tables =
            [
                new("items",
                [
                    new("value", KejiSmartQueryColumnType.String),
                    new("secret_id", KejiSmartQueryColumnType.Integer, Sensitive: true)
                ]),
                new("orders", [new("item_secret_id", KejiSmartQueryColumnType.Integer)])
            ],
            ForeignKeys = [new("sensitive_fk", "items", "secret_id", "orders", "item_secret_id")]
        };
        await fixture.Service.RunAsync(Request(), TestContext.Current.CancellationToken);
        var prompt = Assert.Single(fixture.Provider.Requests).Messages[0].Content;
        Assert.DoesNotContain("sensitive_fk", prompt);
        Assert.DoesNotContain("\"name\":\"secret_id\"", prompt);
    }

    [Fact]
    public async Task AuditExcludesQuestionPlanRowsSummarySecretAndConnection()
    {
        var fixture = Fixture.Create(includeSummary: true);
        fixture.Provider.SecondContent = "summary-private";
        await fixture.Service.RunAsync(
            Request() with { Question = "question-private" }, TestContext.Current.CancellationToken);
        var wire = JsonSerializer.Serialize(fixture.Audit.Calls);
        Assert.DoesNotContain("question-private", wire);
        Assert.DoesNotContain("summary-private", wire);
        Assert.DoesNotContain("alpha", wire);
        Assert.DoesNotContain("DATABASE_PASSWORD", wire);
        Assert.DoesNotContain("db.example.test", wire);
        Assert.DoesNotContain("\"From\"", wire);
    }

    [Fact]
    public async Task ProviderFailureDoesNotExposeRawError()
    {
        var fixture = Fixture.Create();
        fixture.Provider.Success = false;
        fixture.Provider.Content = "provider stack and secret";
        var result = await fixture.Service.RunAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal("SMART_QUERY_PLAN_REJECTED", result.SafeCode);
        Assert.DoesNotContain("provider stack", JsonSerializer.Serialize(fixture.Audit.Calls));
    }

    [Fact]
    public async Task MissingServerProviderIsSafeTerminalFailure()
    {
        var fixture = Fixture.Create(registerProvider: false);
        var events = await Collect(fixture.Service.RunStreamAsync(Request(), TestContext.Current.CancellationToken));
        Assert.Equal(KejiSmartQueryStatus.ProviderNotFound, events[^1].Status);
        Assert.Equal([KejiSmartQueryEventType.RunStarted, KejiSmartQueryEventType.Error,
            KejiSmartQueryEventType.RunCompleted], events.Select(static e => e.Type));
    }

    [Fact]
    public async Task SecretFailureMapsToDedicatedSafeCode()
    {
        var fixture = Fixture.Create();
        fixture.Executor.ThrowSecret = true;
        var result = await fixture.Service.RunAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal(KejiSmartQueryStatus.SecretUnavailable, result.Status);
        Assert.Equal("SMART_QUERY_SECRET_UNAVAILABLE", result.SafeCode);
    }

    [Fact]
    public async Task ExecutionExceptionNeverLeaks()
    {
        var fixture = Fixture.Create();
        fixture.Executor.Exception = new InvalidOperationException("database-host password stack");
        var result = await fixture.Service.RunAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal("SMART_QUERY_EXECUTION_FAILED", result.SafeCode);
        Assert.DoesNotContain("database-host", JsonSerializer.Serialize(fixture.Audit.Calls));
    }

    [Fact]
    public async Task CallerCancellationHasNoTerminalEvent()
    {
        var fixture = Fixture.Create();
        fixture.Executor.WaitForCancellation = true;
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));
        var events = new List<KejiSmartQueryEvent>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in fixture.Service.RunStreamAsync(Request(), cts.Token))
                events.Add(item);
        });
        Assert.DoesNotContain(events, static e => e.Type is KejiSmartQueryEventType.Error or KejiSmartQueryEventType.RunCompleted);
    }

    [Fact]
    public async Task SummaryFailureDoesNotFailSuccessfulQuery()
    {
        var fixture = Fixture.Create(includeSummary: true);
        fixture.Provider.SecondSuccess = false;
        var result = await fixture.Service.RunAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal(KejiSmartQueryStatus.Completed, result.Status);
        Assert.Null(result.Summary);
    }

    private static async Task<List<KejiSmartQueryEvent>> Collect(IAsyncEnumerable<KejiSmartQueryEvent> stream)
    {
        var events = new List<KejiSmartQueryEvent>();
        await foreach (var item in stream) events.Add(item);
        return events;
    }
    private static KejiSmartQueryRequest Request() => new()
    {
        RunId = "0123456789abcdef0123456789abcdef",
        DataSourceId = "ds_1", Question = "list items", RequestedLimit = 100
    };
    private static string PlanJson() => JsonSerializer.Serialize(
        SmartQueryCompilerTests.Plan(), Json);

    private sealed record Fixture(
        KejiSmartQueryService Service, Users Users, Authorization Authorization, Catalog Catalog,
        Provider Provider, Executor Executor, Audit Audit)
    {
        public static Fixture Create(bool includeSummary = false, bool registerProvider = true)
        {
            var users = new Users
            {
                CurrentUser = new("0123456789abcdef", "user", "member", "User", KejiAuthenticationKind.Jwt)
            };
            var authorization = new Authorization();
            var catalog = new Catalog { Source = SmartQueryCompilerTests.Source(KejiSmartQueryDialect.PostgreSql) };
            var provider = new Provider { Content = PlanJson() };
            var executor = new Executor();
            var audit = new Audit();
            var providers = new ModelProviderRegistry(registerProvider
                ? [new KeyValuePair<string, IModelProvider>("openai", provider)] : []);
            var service = new KejiSmartQueryService(users, authorization, catalog, providers, audit,
                [new MySqlSmartQueryDialect(), new PostgreSqlSmartQueryDialect()], [executor],
                new("openai", "server-model", includeSummary));
            return new(service, users, authorization, catalog, provider, executor, audit);
        }
    }
    private sealed class Users : ICurrentUserAccessor { public CurrentUser? CurrentUser { get; set; } }
    private sealed class Authorization : IKejiAuthorizationService
    {
        public bool Allowed { get; set; } = true;
        public IReadOnlyList<KejiPermission> Last { get; private set; } = [];
        public KejiAuthorizationDecision Authorize(CurrentUser? user, KejiPermission permission) =>
            Allowed ? KejiAuthorizationDecision.Allow() :
                KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.PermissionDenied);
        public KejiAuthorizationDecision AuthorizeAll(CurrentUser? user, IReadOnlyList<KejiPermission>? permissions)
        {
            Last = permissions ?? [];
            return Allowed ? KejiAuthorizationDecision.Allow() :
                KejiAuthorizationDecision.Deny(KejiAuthorizationFailureReason.PermissionDenied);
        }
    }
    private sealed class Catalog : IKejiSmartQueryDataSourceCatalog
    {
        public int Calls { get; private set; }
        public KejiSmartQueryDataSource? Source { get; set; }
        public Task<KejiSmartQueryDataSource?> GetAccessibleAsync(
            string dataSourceId, string userId, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(Source); }
    }
    private sealed class Executor : IKejiSmartQueryExecutor
    {
        public KejiSmartQueryDialect Dialect => KejiSmartQueryDialect.PostgreSql;
        public int Calls { get; private set; }
        public bool ThrowSecret { get; set; }
        public bool WaitForCancellation { get; set; }
        public Exception? Exception { get; set; }
        public async Task<KejiSmartQueryResult> ExecuteAsync(string runId, KejiSmartQueryDataSource source,
            KejiCompiledQuery query, KejiSmartQueryOptions options, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (ThrowSecret) throw new KejiSmartQuerySecretUnavailableException();
            if (Exception is not null) throw Exception;
            if (WaitForCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new(runId, KejiSmartQueryStatus.Completed, query.OutputColumns,
                [new([new(KejiSmartQueryValueKind.String, Text: "alpha")])],
                false, null, "SMART_QUERY_COMPLETED");
        }
    }
    private sealed class Provider : IModelProvider
    {
        public string ProviderName => "openai";
        public string Content { get; set; } = "";
        public string SecondContent { get; set; } = "summary";
        public bool Success { get; set; } = true;
        public bool SecondSuccess { get; set; } = true;
        public List<ChatCompletionRequest> Requests { get; } = [];
        public Task<ChatCompletionResponse> CompleteAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var success = Requests.Count == 1 ? Success : SecondSuccess;
            var content = Requests.Count == 1 ? Content : SecondContent;
            return Task.FromResult(success ? ChatCompletionResponse.Succeeded(content) :
                ChatCompletionResponse.Failed(KejiProviderErrorCode.ServerError, content));
        }
        public IAsyncEnumerable<ChatCompletionStreamEvent> StreamAsync(
            ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
    private sealed class Audit : IKejiAuditService
    {
        public List<(string Action, string? Target, IReadOnlyDictionary<string, string>? Metadata)> Calls { get; } = [];
        public Task<KejiAuditResult> WriteAsync(KejiAuditCategory category, string action,
            KejiAuditOutcome outcome, KejiAuditSeverity severity, string targetType,
            string? targetId = null, IReadOnlyDictionary<string, string>? metadata = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((action, targetId, metadata));
            return Task.FromResult(KejiAuditResult.Written);
        }
    }
}
