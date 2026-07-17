using Keji.SmartQuery;
using Keji.Persistence;
using Microsoft.Data.Sqlite;

namespace Keji.SmartQuery.Tests;

public sealed class SmartQueryCatalogTests : IAsyncLifetime
{
    private readonly string _name = "catalog-" + Guid.NewGuid().ToString("N");
    private SqliteConnection _keeper = null!;
    private SqliteKejiSmartQueryDataSourceCatalog _catalog = null!;
    private TestFactory _factory = null!;

    public async ValueTask InitializeAsync()
    {
        var cs = $"Data Source={_name};Mode=Memory;Cache=Shared";
        _keeper = new SqliteConnection(cs);
        await _keeper.OpenAsync(TestContext.Current.CancellationToken);
        _factory = new(cs);
        await new KejiDatabaseInitializer(_factory, new Clock()).InitializeAsync(TestContext.Current.CancellationToken);
        _catalog = new(_factory);
    }

    [Fact]
    public async Task RoundTripPreservesSecurityMetadata()
    {
        var source = SmartQueryCompilerTests.JoinedSource(KejiSmartQueryDialect.PostgreSql);
        await _catalog.UpsertAsync(source, TestContext.Current.CancellationToken);
        var loaded = await _catalog.GetAccessibleAsync(
            source.Id, source.OwnerUserId, TestContext.Current.CancellationToken);
        Assert.NotNull(loaded);
        Assert.Equal(source.Id, loaded.Id);
        Assert.Equal(source.OwnerUserId, loaded.OwnerUserId);
        Assert.Equal(source.Dialect, loaded.Dialect);
        Assert.Equal(source.Host, loaded.Host);
        Assert.Equal(source.SecretReference.EnvironmentVariableName,
            loaded.SecretReference.EnvironmentVariableName);
        Assert.Equal(source.Tables.Select(static t => t.Name), loaded.Tables.Select(static t => t.Name));
        Assert.Equal(source.Tables.SelectMany(static t => t.Columns).Select(static c => (c.Name, c.Type, c.QueryEnabled, c.Sensitive)),
            loaded.Tables.SelectMany(static t => t.Columns).Select(static c => (c.Name, c.Type, c.QueryEnabled, c.Sensitive)));
        Assert.Equal(source.ForeignKeys.Select(static f => f.Name),
            loaded.ForeignKeys.Select(static f => f.Name));
        Assert.True(loaded!.Tables[0].Columns[0].QueryEnabled);
        Assert.Equal("fk_orders_customer", Assert.Single(loaded.ForeignKeys).Name);
    }

    [Fact]
    public async Task CrossUserLookupReturnsNothing()
    {
        var source = SmartQueryCompilerTests.Source(KejiSmartQueryDialect.MySql);
        await _catalog.UpsertAsync(source, TestContext.Current.CancellationToken);
        Assert.Null(await _catalog.GetAccessibleAsync(
            source.Id, "different_user", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SameIdentifierCanBeIsolatedPerOwner()
    {
        var first = SmartQueryCompilerTests.Source(KejiSmartQueryDialect.MySql);
        var second = first with { OwnerUserId = "other_user", Host = "other.example.test" };
        await _catalog.UpsertAsync(first, TestContext.Current.CancellationToken);
        await _catalog.UpsertAsync(second, TestContext.Current.CancellationToken);
        Assert.Equal(first.Host, (await _catalog.GetAccessibleAsync(
            first.Id, first.OwnerUserId, TestContext.Current.CancellationToken))!.Host);
        Assert.Equal(second.Host, (await _catalog.GetAccessibleAsync(
            second.Id, second.OwnerUserId, TestContext.Current.CancellationToken))!.Host);
    }

    [Fact]
    public async Task UpsertUpdatesMetadataWithoutDuplicatingRows()
    {
        var source = SmartQueryCompilerTests.Source(KejiSmartQueryDialect.PostgreSql);
        await _catalog.UpsertAsync(source, TestContext.Current.CancellationToken);
        await _catalog.UpsertAsync(source with { Database = "appdb2" }, TestContext.Current.CancellationToken);
        Assert.Equal("appdb2", (await _catalog.GetAccessibleAsync(
            source.Id, source.OwnerUserId, TestContext.Current.CancellationToken))!.Database);
    }

    [Fact]
    public async Task DisabledSourcePersistsButCannotBeResolved()
    {
        var source = SmartQueryCompilerTests.Source(KejiSmartQueryDialect.PostgreSql) with { Enabled = false };
        await _catalog.UpsertAsync(source, TestContext.Current.CancellationToken);
        Assert.Null(await _catalog.GetAccessibleAsync(
            source.Id, source.OwnerUserId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PlaintextPasswordIsNeverPersistedByContract()
    {
        var source = SmartQueryCompilerTests.Source(KejiSmartQueryDialect.PostgreSql);
        await _catalog.UpsertAsync(source, TestContext.Current.CancellationToken);
        await using var command = _keeper.CreateCommand();
        command.CommandText = "SELECT password_secret_reference FROM smart_query_data_sources";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal("env:DATABASE_PASSWORD", reader.GetString(0));
    }

    [Theory]
    [InlineData("bad id")]
    [InlineData("bad/id")]
    [InlineData("")]
    public async Task UnsafeSourceIdentifiersCannotBePersisted(string id)
    {
        var source = SmartQueryCompilerTests.Source(KejiSmartQueryDialect.MySql) with { Id = id };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _catalog.UpsertAsync(source, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void SecretReferenceOnlyAcceptsBoundedEnvironmentNames()
    {
        Assert.Equal("env:DATABASE_PASSWORD", new KejiSmartQuerySecretReference("env:DATABASE_PASSWORD").ToString());
        Assert.Throws<ArgumentException>(() => new KejiSmartQuerySecretReference("DATABASE_PASSWORD"));
        Assert.Throws<ArgumentException>(() => new KejiSmartQuerySecretReference("PASSWORD\nLEAK"));
    }

    public async ValueTask DisposeAsync() => await _keeper.DisposeAsync();
    private sealed class TestFactory(string connectionString) : ISqliteConnectionFactory
    {
        public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys=ON";
            await command.ExecuteNonQueryAsync(cancellationToken);
            return connection;
        }
    }
    private sealed class Clock : IUnixTimeProvider { public double Now => 1; }
}
