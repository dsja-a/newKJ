using System.Data.Common;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using Keji.SmartQuery;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace Keji.SmartQuery.Tests;

public sealed class SmartQueryExecutorConfigurationTests
{
    [Theory]
    [InlineData(KejiSmartQueryTlsMode.Required, MySqlSslMode.Required)]
    [InlineData(KejiSmartQueryTlsMode.VerifyCertificate, MySqlSslMode.VerifyCA)]
    [InlineData(KejiSmartQueryTlsMode.VerifyFull, MySqlSslMode.VerifyFull)]
    public void MySqlDriverConfigurationEnforcesTlsNoPoolingAndTimeout(
        KejiSmartQueryTlsMode tls, MySqlSslMode expected)
    {
        var executor = new ExposedMySqlExecutor();
        using var connection = executor.Create(
            SmartQueryCompilerTests.Source(KejiSmartQueryDialect.MySql) with { TlsMode = tls }, "resolved");
        var builder = new MySqlConnectionStringBuilder(connection.ConnectionString);
        Assert.Equal(expected, builder.SslMode);
        Assert.False(builder.Pooling);
        Assert.Equal((uint)15, builder.ConnectionTimeout);
        Assert.Equal((uint)30, builder.DefaultCommandTimeout);
        Assert.Equal("resolved", builder.Password);
    }

    [Theory]
    [InlineData(KejiSmartQueryTlsMode.Required, SslMode.Require)]
    [InlineData(KejiSmartQueryTlsMode.VerifyCertificate, SslMode.VerifyCA)]
    [InlineData(KejiSmartQueryTlsMode.VerifyFull, SslMode.VerifyFull)]
    public void PostgreSqlDriverConfigurationEnforcesTlsNoPoolingAndTimeout(
        KejiSmartQueryTlsMode tls, SslMode expected)
    {
        var executor = new ExposedPostgreSqlExecutor();
        using var connection = executor.Create(
            SmartQueryCompilerTests.Source(KejiSmartQueryDialect.PostgreSql) with { TlsMode = tls }, "resolved");
        var builder = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
        Assert.Equal(expected, builder.SslMode);
        Assert.False(builder.Pooling);
        Assert.False(builder.Multiplexing);
        Assert.Equal(15, builder.Timeout);
        Assert.Equal(30, builder.CommandTimeout);
        Assert.Equal("resolved", builder.Password);
    }

    [Fact]
    public async Task MySqlReadOnlyDirectiveIsPreparedBeforeTransaction()
    {
        var connection = new RecordingConnection();
        await new ExposedMySqlExecutor().Prepare(connection, TestContext.Current.CancellationToken);
        Assert.Equal("SET TRANSACTION READ ONLY", connection.LastCommand!.CommandText);
        Assert.Null(connection.LastCommand.Transaction);
        Assert.True(connection.LastCommand.WasDisposed);
    }

    [Fact]
    public async Task PostgreSqlReadOnlyDirectiveIsBoundToActiveTransaction()
    {
        var connection = new RecordingConnection();
        var transaction = new RecordingTransaction(connection);
        await new ExposedPostgreSqlExecutor().Configure(
            connection, transaction, TestContext.Current.CancellationToken);
        Assert.Equal("SET TRANSACTION READ ONLY", connection.LastCommand!.CommandText);
        Assert.Same(transaction, connection.LastCommand.Transaction);
        Assert.True(connection.LastCommand.WasDisposed);
    }

    private sealed class Resolver : IKejiSmartQuerySecretResolver
    {
        public string? Resolve(KejiSmartQuerySecretReference reference) => "not-used";
    }
    private sealed class ExposedMySqlExecutor : MySqlSmartQueryExecutor
    {
        public ExposedMySqlExecutor() : base(new Resolver()) { }
        public DbConnection Create(KejiSmartQueryDataSource source, string password) =>
            base.CreateConnection(source, password);
        public Task Prepare(DbConnection connection, CancellationToken cancellationToken) =>
            base.PrepareReadOnlyAsync(connection, cancellationToken);
    }
    private sealed class ExposedPostgreSqlExecutor : PostgreSqlSmartQueryExecutor
    {
        public ExposedPostgreSqlExecutor() : base(new Resolver()) { }
        public DbConnection Create(KejiSmartQueryDataSource source, string password) =>
            base.CreateConnection(source, password);
        public Task Configure(DbConnection connection, DbTransaction transaction,
            CancellationToken cancellationToken) =>
            base.ConfigureReadOnlyAsync(connection, transaction, cancellationToken);
    }

    private sealed class RecordingConnection : DbConnection
    {
        public RecordingCommand? LastCommand { get; private set; }
        [AllowNull]
        public override string ConnectionString { get; set; } = "";
        public override string Database => "test";
        public override string DataSource => "test";
        public override string ServerVersion => "1";
        public override ConnectionState State => ConnectionState.Open;
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        public override void Close() { }
        public override void Open() { }
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            new RecordingTransaction(this);
        protected override DbCommand CreateDbCommand() => LastCommand = new RecordingCommand(this);
    }

    private sealed class RecordingTransaction(DbConnection connection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.RepeatableRead;
        protected override DbConnection DbConnection => connection;
        public override void Commit() { }
        public override void Rollback() { }
    }

    private sealed class RecordingCommand(DbConnection connection) : DbCommand
    {
        private readonly SqliteParameterCollection _parameters = new SqliteCommand().Parameters;
        public bool WasDisposed { get; private set; }
        [AllowNull]
        public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; } = CommandType.Text;
        public override UpdateRowSource UpdatedRowSource { get; set; }
        [AllowNull]
        protected override DbConnection DbConnection { get; set; } = connection;
        protected override DbParameterCollection DbParameterCollection => _parameters;
        protected override DbTransaction? DbTransaction { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override void Cancel() { }
        public override int ExecuteNonQuery() => 0;
        public override object? ExecuteScalar() => null;
        public override void Prepare() { }
        protected override DbParameter CreateDbParameter() => new SqliteParameter();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) =>
            throw new NotSupportedException();
        public override Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);
        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
