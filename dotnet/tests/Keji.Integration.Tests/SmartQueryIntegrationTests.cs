using System.Data.Common;
using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Keji.Providers;
using Keji.Security.Auth;
using Keji.Security.Authorization;
using Keji.SmartQuery;
using Microsoft.Data.Sqlite;

namespace Keji.Integration.Tests;

public sealed class SmartQueryIntegrationTests : IAsyncLifetime
{
    private SqliteConnection _keeper = null!;
    private Harness _harness = null!;

    public async Task InitializeAsync()
    {
        const string cs = "Data Source=smartquery-integration;Mode=Memory;Cache=Shared";
        _keeper = new SqliteConnection(cs);
        await _keeper.OpenAsync();
        await using var command = _keeper.CreateCommand();
        command.CommandText = "CREATE TABLE sales(id INTEGER, region TEXT, amount REAL);" +
            "INSERT INTO sales VALUES(1,'east',10.5),(2,'west',20.0),(3,'east',30.0);";
        await command.ExecuteNonQueryAsync();
        _harness = Harness.Create(cs);
    }

    [Fact]
    public async Task EndToEnd_ModelPlanCompilesAndReadsOnlyEnabledTable()
    {
        _harness.Provider.Content = Plan("sales", ["id", "region"], 10);
        var result = await _harness.Service.ExecuteAsync(Request());
        Assert.Equal(KejiSmartQueryStatus.Completed, result.Status);
        Assert.Equal(3, result.Rows.Length);
        Assert.Equal(1, _harness.Connections.Calls);
    }

    [Fact]
    public async Task CrossUserDataSourceDenialStopsBeforeModelAndDatabase()
    {
        _harness.Catalog.Allow = false;
        var result = await _harness.Service.ExecuteAsync(Request());
        Assert.Equal(KejiSmartQueryStatus.DataSourceNotFound, result.Status);
        Assert.Empty(_harness.Provider.Requests);
        Assert.Equal(0, _harness.Connections.Calls);
    }

    [Fact]
    public async Task SqlTextFromModelIsRejectedBeforeDatabase()
    {
        _harness.Provider.Content = "SELECT * FROM sales";
        var result = await _harness.Service.ExecuteAsync(Request());
        Assert.Equal(KejiSmartQueryStatus.PlanRejected, result.Status);
        Assert.Equal(0, _harness.Connections.Calls);
    }

    [Fact]
    public async Task InjectedFilterRemainsAParameter()
    {
        _harness.Provider.Content = """
            {"Table":"sales","Columns":["id"],"Filters":[{"Column":"region","Operator":"equal","Value":"east' OR 1=1 --"}],"OrderBy":[],"Limit":10}
            """;
        var result = await _harness.Service.ExecuteAsync(Request());
        Assert.Empty(result.Rows);
        Assert.Equal(3, await CountRows());
    }

    [Fact]
    public async Task RowLimitAndSafeAuditApplyEndToEnd()
    {
        _harness.Provider.Content = Plan("sales", ["id"], 2);
        var result = await _harness.Service.ExecuteAsync(Request() with { MaxRows = 2, Question = "secret question" });
        Assert.Equal(2, result.Rows.Length);
        Assert.True(result.Truncated);
        Assert.DoesNotContain("secret question", System.Text.Json.JsonSerializer.Serialize(_harness.Audit.Calls));
    }

    public async Task DisposeAsync() => await _keeper.DisposeAsync();
    private async Task<long> CountRows()
    {
        await using var command = _keeper.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sales";
        return (long)(await command.ExecuteScalarAsync())!;
    }
    private static KejiSmartQueryRequest Request() => new()
    { DataSourceId = "sales_ds", ProviderName = "openai", Model = "model", Question = "sales" };
    private static string Plan(string table, string[] columns, int limit) =>
        System.Text.Json.JsonSerializer.Serialize(new { Table = table, Columns = columns,
            Filters = Array.Empty<object>(), OrderBy = Array.Empty<object>(), Limit = limit });

    private sealed record Harness(KejiSmartQueryService Service, Catalog Catalog, Connections Connections,
        Provider Provider, Audit Audit)
    {
        public static Harness Create(string cs)
        {
            var catalog = new Catalog();
            var connections = new Connections(cs);
            var provider = new Provider();
            var audit = new Audit();
            var registry = new ModelProviderRegistry([new KeyValuePair<string, IModelProvider>("openai", provider)]);
            var service = new KejiSmartQueryService(new Users(), new Authorization(), catalog, connections,
                registry, audit, new KejiSmartQueryCompiler(), new KejiSmartQueryOptions(maxRows: 50));
            return new(service, catalog, connections, provider, audit);
        }
    }
    private sealed class Users : ICurrentUserAccessor
    {
        public CurrentUser? CurrentUser => new("0123456789abcdef", "user", "member", "User", KejiAuthenticationKind.Jwt);
    }
    private sealed class Authorization : IKejiAuthorizationService
    {
        public KejiAuthorizationDecision Authorize(CurrentUser? user, KejiPermission permission) => KejiAuthorizationDecision.Allow();
        public KejiAuthorizationDecision AuthorizeAll(CurrentUser? user, IReadOnlyList<KejiPermission>? permissions) => KejiAuthorizationDecision.Allow();
    }
    private sealed class Catalog : IKejiSmartQueryDataSourceCatalog
    {
        public bool Allow { get; set; } = true;
        public Task<KejiSmartQueryDataSource?> GetAccessibleAsync(string id, string user, CancellationToken ct = default) =>
            Task.FromResult<KejiSmartQueryDataSource?>(Allow ? new("sales_ds", "sales_connection",
                [new("sales", [new("id", "integer"), new("region", "text"), new("amount", "real")])]) : null);
    }
    private sealed class Connections(string cs) : IKejiSmartQueryConnectionFactory
    {
        public int Calls { get; private set; }
        public async Task<DbConnection> OpenReadOnlyAsync(KejiSmartQueryDataSource source, CancellationToken ct = default)
        { Calls++; var connection = new SqliteConnection(cs); await connection.OpenAsync(ct); return connection; }
    }
    private sealed class Provider : IModelProvider
    {
        public string ProviderName => "openai";
        public string Content { get; set; } = "";
        public List<ChatCompletionRequest> Requests { get; } = [];
        public Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default)
        { Requests.Add(request); return Task.FromResult(ChatCompletionResponse.Succeeded(Content)); }
        public IAsyncEnumerable<ChatCompletionStreamEvent> StreamAsync(ChatCompletionRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }
    private sealed class Audit : IKejiAuditService
    {
        public List<IReadOnlyDictionary<string, string>?> Calls { get; } = [];
        public Task<KejiAuditResult> WriteAsync(KejiAuditCategory category, string action, KejiAuditOutcome outcome,
            KejiAuditSeverity severity, string targetType, string? targetId = null,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
        { Calls.Add(metadata); return Task.FromResult(KejiAuditResult.Written); }
    }
}
