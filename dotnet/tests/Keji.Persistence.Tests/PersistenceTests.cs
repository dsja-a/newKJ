using System.Data.Common;
using Keji.Persistence.Models;
using Keji.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Keji.Persistence.Tests;

public class PersistenceTests
{
    private const string TestRoleAdmin = "admin";
    private const string TestRoleMember = "member";
    private const string TestRoleReadonly = "readonly";

    // ──────────────────────────────────────────────
    // Helper: create a temp directory + options
    // ──────────────────────────────────────────────

    private sealed class TestContext : IDisposable
    {
        public string Dir { get; }
        public string DbPath => System.IO.Path.Combine(Dir, "test.db");
        public KejiPersistenceOptions Options { get; }
        public IUnixTimeProvider FixedTime { get; }
        public double FixedTimestamp { get; }

        public TestContext(double? fixedTimestamp = null)
        {
            Dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "KJ_PERSIST_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
            FixedTimestamp = fixedTimestamp ?? 1000000.0;
            FixedTime = new FixedTimeProvider(FixedTimestamp);
            Options = new()
            {
                ProjectRoot = Dir,
                DatabasePath = "test.db",
                BusyTimeoutMilliseconds = 5000,
                EnableWal = true,
                EnableForeignKeys = true,
            };
        }

        public void Dispose()
        {
            try { Directory.Delete(Dir, recursive: true); } catch { }
        }
    }

    private sealed class FixedTimeProvider : IUnixTimeProvider
    {
        public double Now { get; }
        public FixedTimeProvider(double now) { Now = now; }
    }

    private static ISqliteConnectionFactory CreateFactory(KejiPersistenceOptions options)
        => new SqliteConnectionFactory(options);

    private static async Task<IKejiDatabaseInitializer> CreateInitializerAsync(ISqliteConnectionFactory factory, IUnixTimeProvider time)
    {
        var initializer = new KejiDatabaseInitializer(factory, time);
        await initializer.InitializeAsync();
        return initializer;
    }

    private static async Task<SqliteConnection> GetOpenConnectionAsync(ISqliteConnectionFactory factory)
        => await factory.OpenConnectionAsync();

    // ──────────────────────────────────────────────
    // CreateDirectoryIfMissing
    // ──────────────────────────────────────────────

    [Fact]
    public async Task CreateDirectory_DefaultDataPath_CreatesDirectoryOnInit()
    {
        using var ctx = new TestContext();
        var dataDir = System.IO.Path.Combine(ctx.Dir, "data");
        var dbPath = System.IO.Path.Combine(dataDir, "keji.db");
        var opts = new KejiPersistenceOptions
        {
            ProjectRoot = ctx.Dir,
            DatabasePath = "data/keji.db",
            CreateDirectoryIfMissing = true,
        };
        var factory = CreateFactory(opts);
        Assert.False(Directory.Exists(dataDir));
        await CreateInitializerAsync(factory, ctx.FixedTime);
        Assert.True(Directory.Exists(dataDir));
        Assert.True(File.Exists(dbPath));
    }

    [Fact]
    public async Task CreateDirectory_MissingDirWithoutFlag_Throws()
    {
        using var ctx = new TestContext();
        var dataDir = System.IO.Path.Combine(ctx.Dir, "missing_data");
        var opts = new KejiPersistenceOptions
        {
            ProjectRoot = ctx.Dir,
            DatabasePath = "missing_data/test.db",
            CreateDirectoryIfMissing = false,
        };
        var factory = CreateFactory(opts);
        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() => factory.OpenConnectionAsync());
    }

    [Fact]
    public void DI_Registration_DoesNotCreateDirectory()
    {
        using var ctx = new TestContext();
        var dataDir = System.IO.Path.Combine(ctx.Dir, "data");
        var services = new ServiceCollection();
        services.AddKejiPersistenceFoundation(o =>
        {
            o.ProjectRoot = ctx.Dir;
            o.DatabasePath = "data/test.db";
        });
        Assert.False(Directory.Exists(dataDir));
        services.BuildServiceProvider();
        Assert.False(Directory.Exists(dataDir));
    }

    [Fact]
    public void DI_ResolveServices_DoesNotCreateDirectory()
    {
        using var ctx = new TestContext();
        var dataDir = System.IO.Path.Combine(ctx.Dir, "data");
        var services = new ServiceCollection();
        services.AddKejiPersistenceFoundation(o =>
        {
            o.ProjectRoot = ctx.Dir;
            o.DatabasePath = "data/test.db";
        });
        var sp = services.BuildServiceProvider();
        sp.GetRequiredService<IUserRepository>();
        sp.GetRequiredService<IConversationRepository>();
        sp.GetRequiredService<IMessageRepository>();
        sp.GetRequiredService<ISettingsRepository>();
        Assert.False(Directory.Exists(dataDir));
    }

    [Fact]
    public async Task InitializeAsync_CreatesDirectoryAndDbFile()
    {
        using var ctx = new TestContext();
        var dataDir = System.IO.Path.Combine(ctx.Dir, "data");
        var dbPath = System.IO.Path.Combine(dataDir, "test.db");
        var opts = new KejiPersistenceOptions
        {
            ProjectRoot = ctx.Dir,
            DatabasePath = "data/test.db",
            CreateDirectoryIfMissing = true,
        };
        var factory = CreateFactory(opts);
        Assert.False(Directory.Exists(dataDir));
        await CreateInitializerAsync(factory, ctx.FixedTime);
        Assert.True(Directory.Exists(dataDir));
        Assert.True(File.Exists(dbPath));
    }

    // ──────────────────────────────────────────────
    // Connection Factory
    // ──────────────────────────────────────────────

    [Fact]
    public async Task ConnectionFactory_OpensConnection()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        using var conn = await GetOpenConnectionAsync(factory);
        Assert.Equal(System.Data.ConnectionState.Open, conn.State);
    }

    [Fact]
    public async Task ConnectionFactory_DatabaseFileCreated()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        using var conn = await GetOpenConnectionAsync(factory);
        Assert.True(File.Exists(ctx.DbPath));
    }

    [Fact]
    public async Task ConnectionFactory_ForeignKeysEnabled()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        using var conn = await GetOpenConnectionAsync(factory);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys";
        var result = await cmd.ExecuteScalarAsync();
        Assert.Equal(1L, result);
    }

    [Fact]
    public async Task ConnectionFactory_BusyTimeoutSet()
    {
        using var ctx = new TestContext();
        ctx.Options.BusyTimeoutMilliseconds = 3000;
        var factory = CreateFactory(ctx.Options);
        using var conn = await GetOpenConnectionAsync(factory);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA busy_timeout";
        var result = await cmd.ExecuteScalarAsync();
        Assert.Equal(3000L, result);
    }

    [Fact]
    public async Task ConnectionFactory_WalEnabled()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        using var conn = await GetOpenConnectionAsync(factory);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode";
        var result = (await cmd.ExecuteScalarAsync())!.ToString();
        Assert.Equal("wal", result, ignoreCase: true);
    }

    [Fact]
    public async Task ConnectionFactory_ConcurrentWalInit_Succeeds()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        const int count = 10;
        var tasks = new Task<SqliteConnection>[count];
        for (int i = 0; i < count; i++)
            tasks[i] = factory.OpenConnectionAsync();
        var conns = await Task.WhenAll(tasks);
        foreach (var c in conns)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode";
            var result = (await cmd.ExecuteScalarAsync())!.ToString();
            Assert.Equal("wal", result, ignoreCase: true);
            c.Dispose();
        }
    }

    [Fact]
    public void DatabasePathResolver_RelativePath()
    {
        var opts = new KejiPersistenceOptions { ProjectRoot = @"C:\root", DatabasePath = "data/test.db" };
        var path = DatabasePathResolver.Resolve(opts);
        Assert.Equal(@"C:\root\data\test.db", path, ignoreCase: true);
    }

    [Fact]
    public void DatabasePathResolver_AbsolutePath()
    {
        var opts = new KejiPersistenceOptions { ProjectRoot = @"C:\root", DatabasePath = @"D:\abs\test.db" };
        var path = DatabasePathResolver.Resolve(opts);
        Assert.Equal(@"D:\abs\test.db", path, ignoreCase: true);
    }

    [Fact]
    public void DatabasePathResolver_Empty_Throws()
    {
        var opts = new KejiPersistenceOptions { DatabasePath = "" };
        Assert.Throws<KejiPersistenceException>(() => DatabasePathResolver.Resolve(opts));
    }

    [Fact]
    public void DatabasePathResolver_NullChar_Throws()
    {
        var opts = new KejiPersistenceOptions { DatabasePath = "bad\0.db" };
        Assert.Throws<KejiPersistenceException>(() => DatabasePathResolver.Resolve(opts));
    }

    // ──────────────────────────────────────────────
    // Database Initializer – Schema
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Initialize_CreatesAllTables()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);

        using var conn = await GetOpenConnectionAsync(factory);
        var tables = await GetTableNamesAsync(conn);
        Assert.Contains("schema_migrations", tables);
        Assert.Contains("conversations", tables);
        Assert.Contains("messages", tables);
        Assert.Contains("documents", tables);
        Assert.Contains("settings", tables);
        Assert.Contains("database_configs", tables);
        Assert.Contains("table_metadata", tables);
        Assert.Contains("tool_usage_log", tables);
        Assert.Contains("audit_events", tables);
        Assert.Contains("users", tables);
    }

    [Fact]
    public async Task Initialize_CreatesAllIndexes()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);

        using var conn = await GetOpenConnectionAsync(factory);
        var indexes = await GetIndexNamesAsync(conn);
        Assert.Contains("idx_messages_conv", indexes);
        Assert.Contains("idx_documents_path", indexes);
        Assert.Contains("idx_documents_type", indexes);
        Assert.Contains("idx_tool_usage_session", indexes);
        Assert.Contains("idx_tool_usage_name", indexes);
        Assert.Contains("idx_audit_created", indexes);
        Assert.Contains("idx_audit_type", indexes);
        Assert.Contains("idx_audit_path", indexes);
        Assert.Contains("idx_users_username", indexes);
    }

    [Fact]
    public async Task Initialize_SchemaMigrationsTableExists()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);

        using var conn = await GetOpenConnectionAsync(factory);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM schema_migrations";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.True(count >= 0);
    }

    [Fact]
    public async Task Initialize_IsIdempotent()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        var init = new KejiDatabaseInitializer(factory, ctx.FixedTime);
        await init.InitializeAsync();
        await init.InitializeAsync();
        await init.InitializeAsync();

        using var conn = await GetOpenConnectionAsync(factory);
        var tables = await GetTableNamesAsync(conn);
        Assert.Contains("users", tables);
    }

    [Fact]
    public async Task Initialize_DbFileExistsAfterInit()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        Assert.False(File.Exists(ctx.DbPath));
        await CreateInitializerAsync(factory, ctx.FixedTime);
        Assert.True(File.Exists(ctx.DbPath));
    }

    [Fact]
    public async Task Initialize_RecordsMigrationVersion()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);

        using var conn = await GetOpenConnectionAsync(factory);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM schema_migrations WHERE version = '001_add_owner_user_id'";
        var result = await cmd.ExecuteScalarAsync();
        Assert.Equal("001_add_owner_user_id", result);
    }

    // ──────────────────────────────────────────────
    // Legacy Migration
    // ──────────────────────────────────────────────

    [Fact]
    public async Task LegacyMigration_AddsOwnerUserIdColumn()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);

        using (var conn = await GetOpenConnectionAsync(factory))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE conversations (
                    id TEXT PRIMARY KEY,
                    title TEXT DEFAULT '新对话',
                    created_at REAL NOT NULL,
                    updated_at REAL NOT NULL,
                    message_count INTEGER DEFAULT 0
                );
                INSERT INTO conversations (id, title, created_at, updated_at)
                VALUES ('conv_legacy', '旧对话', 100.0, 200.0);
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var init = new KejiDatabaseInitializer(factory, ctx.FixedTime);
        await init.InitializeAsync();

        using var conn2 = await GetOpenConnectionAsync(factory);
        var cols = await GetColumnNamesAsync(conn2, "conversations");
        Assert.Contains("owner_user_id", cols);

        using var verifyCmd = conn2.CreateCommand();
        verifyCmd.CommandText = "SELECT title, owner_user_id FROM conversations WHERE id = 'conv_legacy'";
        using var reader = await verifyCmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("旧对话", reader.GetString(0));
        Assert.True(reader.IsDBNull(1));
    }

    [Fact]
    public async Task LegacyMigration_PreservesMessages()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);

        using (var conn = await GetOpenConnectionAsync(factory))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE conversations (
                    id TEXT PRIMARY KEY,
                    title TEXT DEFAULT '新对话',
                    created_at REAL NOT NULL,
                    updated_at REAL NOT NULL,
                    message_count INTEGER DEFAULT 0
                );
                CREATE TABLE messages (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    conversation_id TEXT NOT NULL,
                    role TEXT NOT NULL,
                    content TEXT NOT NULL,
                    created_at REAL NOT NULL,
                    FOREIGN KEY (conversation_id) REFERENCES conversations(id) ON DELETE CASCADE
                );
                INSERT INTO conversations (id, title, created_at, updated_at, message_count)
                VALUES ('conv_1', '测试对话', 100.0, 200.0, 2);
                INSERT INTO messages (conversation_id, role, content, created_at)
                VALUES ('conv_1', 'user', '你好', 110.0);
                INSERT INTO messages (conversation_id, role, content, created_at)
                VALUES ('conv_1', 'assistant', '你好！', 120.0);
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        await CreateInitializerAsync(factory, ctx.FixedTime);

        using var conn2 = await GetOpenConnectionAsync(factory);
        using var checkConv = conn2.CreateCommand();
        checkConv.CommandText = "SELECT title, message_count FROM conversations WHERE id = 'conv_1'";
        using var r1 = await checkConv.ExecuteReaderAsync();
        Assert.True(await r1.ReadAsync());
        Assert.Equal("测试对话", r1.GetString(0));
        Assert.Equal(2, r1.GetInt32(1));

        using var checkMsg = conn2.CreateCommand();
        checkMsg.CommandText = "SELECT COUNT(*) FROM messages WHERE conversation_id = 'conv_1'";
        var count = Convert.ToInt32(await checkMsg.ExecuteScalarAsync());
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task LegacyMigration_DoesNotRepeatAlter()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);

        using (var conn = await GetOpenConnectionAsync(factory))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE conversations (
                    id TEXT PRIMARY KEY,
                    title TEXT DEFAULT '新对话',
                    created_at REAL NOT NULL,
                    updated_at REAL NOT NULL,
                    message_count INTEGER DEFAULT 0
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var init = new KejiDatabaseInitializer(factory, ctx.FixedTime);
        await init.InitializeAsync();
        await init.InitializeAsync();

        using var conn2 = await GetOpenConnectionAsync(factory);
        var cols = await GetColumnNamesAsync(conn2, "conversations");
        Assert.Contains("owner_user_id", cols);
        var ownerCount = cols.Count(c => c == "owner_user_id");
        Assert.Equal(1, ownerCount);
    }

    [Fact]
    public async Task LegacyMigration_OwnerUserIdNullAfterMigration()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);

        using (var conn = await GetOpenConnectionAsync(factory))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE conversations (
                    id TEXT PRIMARY KEY,
                    title TEXT DEFAULT '新对话',
                    created_at REAL NOT NULL,
                    updated_at REAL NOT NULL,
                    message_count INTEGER DEFAULT 0
                );
                INSERT INTO conversations (id, title, created_at, updated_at)
                VALUES ('conv_legacy', '旧对话', 100.0, 200.0);
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        await CreateInitializerAsync(factory, ctx.FixedTime);

        using var conn2 = await GetOpenConnectionAsync(factory);
        using var cmd2 = conn2.CreateCommand();
        cmd2.CommandText = "SELECT owner_user_id FROM conversations WHERE id = 'conv_legacy'";
        var result = await cmd2.ExecuteScalarAsync();
        Assert.Equal(DBNull.Value, result);
    }

    [Fact]
    public async Task LegacyMigration_IdxConvOwnerCreated()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);

        using (var conn = await GetOpenConnectionAsync(factory))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE conversations (id TEXT PRIMARY KEY, title TEXT, created_at REAL, updated_at REAL, message_count INTEGER DEFAULT 0)";
            await cmd.ExecuteNonQueryAsync();
        }

        await CreateInitializerAsync(factory, ctx.FixedTime);

        using var conn2 = await GetOpenConnectionAsync(factory);
        var indexes = await GetIndexNamesAsync(conn2);
        Assert.Contains("idx_conv_owner", indexes);
    }

    [Fact]
    public async Task LegacyMigration_ExistingOwnerColumnWithoutIndex()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);

        using (var conn = await GetOpenConnectionAsync(factory))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE conversations (
                    id TEXT PRIMARY KEY,
                    title TEXT DEFAULT '新对话',
                    created_at REAL NOT NULL,
                    updated_at REAL NOT NULL,
                    message_count INTEGER DEFAULT 0,
                    owner_user_id TEXT
                );
                INSERT INTO conversations (id, title, created_at, updated_at, owner_user_id)
                VALUES ('existing_conv', '已有列', 100.0, 200.0, 'user_a');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var init = new KejiDatabaseInitializer(factory, ctx.FixedTime);
        await init.InitializeAsync();

        using var conn2 = await GetOpenConnectionAsync(factory);
        var cols = await GetColumnNamesAsync(conn2, "conversations");
        var ownerCount = cols.Count(c => c == "owner_user_id");
        Assert.Equal(1, ownerCount);

        var indexes = await GetIndexNamesAsync(conn2);
        Assert.Contains("idx_conv_owner", indexes);

        using var checkMigration = conn2.CreateCommand();
        checkMigration.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = '001_add_owner_user_id'";
        var migrationCount = Convert.ToInt32(await checkMigration.ExecuteScalarAsync());
        Assert.Equal(1, migrationCount);

        using var verifyData = conn2.CreateCommand();
        verifyData.CommandText = "SELECT owner_user_id FROM conversations WHERE id = 'existing_conv'";
        var owner = await verifyData.ExecuteScalarAsync();
        Assert.Equal("user_a", owner);
    }

    [Fact]
    public async Task LegacyMigration_UnrelatedMigrationDoesNotReplace()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);

        using (var conn = await GetOpenConnectionAsync(factory))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE schema_migrations (
                    version TEXT PRIMARY KEY,
                    applied_at REAL NOT NULL
                );
                INSERT INTO schema_migrations (version, applied_at) VALUES ('000_initial', 100.0);
                CREATE TABLE conversations (
                    id TEXT PRIMARY KEY,
                    title TEXT DEFAULT '新对话',
                    created_at REAL NOT NULL,
                    updated_at REAL NOT NULL,
                    message_count INTEGER DEFAULT 0
                );
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var init = new KejiDatabaseInitializer(factory, ctx.FixedTime);
        await init.InitializeAsync();

        using var conn2 = await GetOpenConnectionAsync(factory);
        using var cmd2 = conn2.CreateCommand();
        cmd2.CommandText = "SELECT version FROM schema_migrations ORDER BY version";
        var versions = new List<string>();
        using var reader = await cmd2.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            versions.Add(reader.GetString(0));

        Assert.Contains("000_initial", versions);
        Assert.Contains("001_add_owner_user_id", versions);
    }

    // ──────────────────────────────────────────────
    // User Repository
    // ──────────────────────────────────────────────

    [Fact]
    public async Task User_Create_Returns16CharHexId()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        var id = await repo.CreateAsync("testuser", "hash123", TestRoleMember);
        Assert.Equal(16, id.Length);
        Assert.Matches("^[0-9a-f]{16}$", id);
    }

    [Fact]
    public async Task User_Create_DisplayNameDefaultsToUsername()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        var id = await repo.CreateAsync("alice", "hash_alice", TestRoleMember);
        var user = await repo.GetByIdAsync(id);
        Assert.NotNull(user);
        Assert.Equal("alice", user.DisplayName);
    }

    [Fact]
    public async Task User_Create_AllowsThreeRoles()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        var id1 = await repo.CreateAsync("admin1", "h1", TestRoleAdmin);
        var id2 = await repo.CreateAsync("member1", "h2", TestRoleMember);
        var id3 = await repo.CreateAsync("ro1", "h3", TestRoleReadonly);

        Assert.Equal(TestRoleAdmin, (await repo.GetByIdAsync(id1))!.Role);
        Assert.Equal(TestRoleMember, (await repo.GetByIdAsync(id2))!.Role);
        Assert.Equal(TestRoleReadonly, (await repo.GetByIdAsync(id3))!.Role);
    }

    [Fact]
    public async Task User_Create_InvalidRole_Throws()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.CreateAsync("bad", "h", "superadmin"));
    }

    [Fact]
    public async Task User_Create_DuplicateUsername_ThrowsDuplicate()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        await repo.CreateAsync("dupuser", "hash1", TestRoleMember);
        var ex = await Assert.ThrowsAsync<DuplicateUsernameException>(() =>
            repo.CreateAsync("dupuser", "hash2", TestRoleMember));
        Assert.Contains("dupuser", ex.Username);
    }

    [Fact]
    public async Task User_Create_EmptyUsername_Throws()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.CreateAsync("", "h", TestRoleMember));
    }

    [Fact]
    public async Task User_Create_EmptyPasswordHash_Throws()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.CreateAsync("u", "", TestRoleMember));
    }

    [Fact]
    public async Task User_SqlInjection_HandledAsValue()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        var id = await repo.CreateAsync("malicious' OR '1'='1", "hash", TestRoleMember);
        var user = await repo.GetByIdAsync(id);
        Assert.NotNull(user);
        Assert.Equal("malicious' OR '1'='1", user.Username);
    }

    [Fact]
    public async Task User_GetByUsername_ReturnsPasswordHash()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        var id = await repo.CreateAsync("pwduser", "bcrypt_hash_12345", TestRoleMember);
        var user = await repo.GetByUsernameAsync("pwduser");
        Assert.NotNull(user);
        Assert.Equal("bcrypt_hash_12345", user.PasswordHash);
    }

    [Fact]
    public async Task User_List_DoesNotReturnPasswordHash()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        await repo.CreateAsync("u1", "hash1", TestRoleMember);
        var list = await repo.ListAsync();
        var summary = list.FirstOrDefault(u => u.Username == "u1");
        Assert.NotNull(summary);
        Assert.False(summary.GetType().GetProperty("PasswordHash")?.CanRead == true);
    }

    [Fact]
    public async Task User_Count_ReturnsCorrect()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        Assert.Equal(0, await repo.CountAsync());
        await repo.CreateAsync("u1", "h", TestRoleMember);
        Assert.Equal(1, await repo.CountAsync());
        await repo.CreateAsync("u2", "h", TestRoleMember);
        Assert.Equal(2, await repo.CountAsync());
    }

    [Fact]
    public async Task User_Update_DisplayName()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        var id = await repo.CreateAsync("updateuser", "h", TestRoleMember);
        await repo.UpdateAsync(id, new UpdateUserCommand { DisplayName = "新名称" });
        var user = await repo.GetByIdAsync(id);
        Assert.Equal("新名称", user!.DisplayName);
    }

    [Fact]
    public async Task User_Update_Role()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        var id = await repo.CreateAsync("roleuser", "h", TestRoleMember);
        await repo.UpdateAsync(id, new UpdateUserCommand { Role = TestRoleAdmin });
        var user = await repo.GetByIdAsync(id);
        Assert.Equal(TestRoleAdmin, user!.Role);
    }

    [Fact]
    public async Task User_Update_InvalidRole_Throws()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        var id = await repo.CreateAsync("badrole", "h", TestRoleMember);
        await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.UpdateAsync(id, new UpdateUserCommand { Role = "nobody" }));
    }

    [Fact]
    public async Task User_Update_IsActive()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        var id = await repo.CreateAsync("activeuser", "h", TestRoleMember);
        var user = await repo.GetByIdAsync(id);
        Assert.True(user!.IsActive);

        await repo.UpdateAsync(id, new UpdateUserCommand { IsActive = false });
        user = await repo.GetByIdAsync(id);
        Assert.False(user!.IsActive);
    }

    [Fact]
    public async Task User_Update_PasswordHash()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        var id = await repo.CreateAsync("pwduser2", "old_hash", TestRoleMember);
        await repo.UpdateAsync(id, new UpdateUserCommand { PasswordHash = "new_hash" });
        var user = await repo.GetByIdAsync(id);
        Assert.Equal("new_hash", user!.PasswordHash);
    }

    [Fact]
    public async Task User_Update_EmptyCommand_ReturnsTrueForExisting()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        var id = await repo.CreateAsync("nochange", "h", TestRoleMember);
        var result = await repo.UpdateAsync(id, new UpdateUserCommand());
        Assert.True(result);
    }

    [Fact]
    public async Task User_Update_EmptyCommand_ReturnsFalseForMissing()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        var result = await repo.UpdateAsync("nonexistent", new UpdateUserCommand());
        Assert.False(result);
    }

    [Fact]
    public async Task User_TouchLogin_UsesFixedTime()
    {
        using var ctx = new TestContext(fixedTimestamp: 1234567.0);
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        var id = await repo.CreateAsync("touchuser", "h", TestRoleMember);
        Assert.Null((await repo.GetByIdAsync(id))!.LastLoginAt);

        await repo.TouchLoginAsync(id);
        var user = await repo.GetByIdAsync(id);
        Assert.Equal(1234567.0, user!.LastLoginAt);
    }

    [Fact]
    public async Task User_Delete_RemovesUserAndConversationsAndMessages()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var userRepo = new SqliteUserRepository(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        var uid = await userRepo.CreateAsync("deluser", "h", TestRoleMember);
        await convRepo.CreateOwnedAsync("conv_a", uid, "对话A");
        await convRepo.CreateOwnedAsync("conv_b", uid, "对话B");
        await msgRepo.AddOwnedAsync("conv_a", uid, "user", "hello");

        var deleted = await userRepo.DeleteAsync(uid);
        Assert.True(deleted);

        Assert.Null(await userRepo.GetByIdAsync(uid));
        Assert.Null(await convRepo.GetOwnedAsync("conv_a", uid));
        Assert.Null(await convRepo.GetOwnedAsync("conv_b", uid));

        var msgs = await msgRepo.ListOwnedMessagesAsync("conv_a", uid);
        Assert.Empty(msgs);
    }

    [Fact]
    public async Task User_Delete_Nonexistent_ReturnsFalse()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        Assert.False(await repo.DeleteAsync("nonexistent"));
    }

    [Fact]
    public async Task User_Delete_TransactionRollsBackOnFailure()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        var uid = await repo.CreateAsync("rollbackuser", "h", TestRoleMember);
        await convRepo.CreateOwnedAsync("rollback_conv", uid, "回滚对话");
        await msgRepo.AddOwnedAsync("rollback_conv", uid, "user", "回滚消息");

        using (var triggerConn = await GetOpenConnectionAsync(factory))
        {
            using var createTrigger = triggerConn.CreateCommand();
            createTrigger.CommandText = """
                CREATE TRIGGER fail_user_delete BEFORE DELETE ON users
                BEGIN
                    SELECT RAISE(ABORT, 'forced failure');
                END;
                """;
            await createTrigger.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.DeleteAsync(uid));

        Assert.Null(ex.InnerException);
        Assert.DoesNotContain("forced failure", ex.ToString(), StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(await repo.GetByIdAsync(uid));
        Assert.NotNull(await convRepo.GetOwnedAsync("rollback_conv", uid));

        var msgs = await msgRepo.ListOwnedMessagesAsync("rollback_conv", uid);
        Assert.Single(msgs);

        var conv = await convRepo.GetOwnedAsync("rollback_conv", uid);
        Assert.NotNull(conv);
        Assert.Equal(1, conv.MessageCount);
    }

    // ──────────────────────────────────────────────
    // Conversation Repository
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Conversation_Create_Idempotent()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        var c1 = await repo.CreateOwnedAsync("conv_id", "user_a", "原始标题");
        var c2 = await repo.CreateOwnedAsync("conv_id", "user_a", "不应覆盖");
        Assert.Equal("原始标题", c2.Title);
        Assert.Equal(c1.CreatedAt, c2.CreatedAt);
    }

    [Fact]
    public async Task Conversation_Create_DoesNotOverwriteTitle()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        var c1 = await repo.CreateOwnedAsync("fixed_conv", "user_a", "原始标题");
        var c2 = await repo.CreateOwnedAsync("fixed_conv", "user_a", "新标题");
        Assert.Equal("原始标题", c2.Title);
    }

    [Fact]
    public async Task Conversation_Create_DoesNotOverwriteOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("owned_conv", "user_a", "对话");
        var c2 = await repo.CreateOwnedAsync("owned_conv", "user_b", "不应改属于");
        Assert.Equal("user_a", c2.OwnerUserId);
    }

    [Fact]
    public async Task Conversation_Create_DoesNotOverwriteOwnerOnRecreate()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        var c1 = await repo.CreateOwnedAsync("unowned_conv", "user_a", "对话");
        Assert.Equal("user_a", c1.OwnerUserId);

        var c2 = await repo.CreateOwnedAsync("unowned_conv", "new_owner", "对话");
        Assert.Equal("user_a", c2.OwnerUserId);
    }

    [Fact]
    public async Task Conversation_EnsureOwned_CreatesIfMissing()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        var (record, result) = await repo.EnsureOwnedAsync("new_conv", "user1");
        Assert.Equal(ConversationOwnershipResult.Created, result);
        Assert.NotNull(record);
        Assert.Equal("new_conv", record.Id);
    }

    [Fact]
    public async Task Conversation_EnsureOwned_AlreadyOwned()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("my_conv", "user1", "对话");
        var (record, result) = await repo.EnsureOwnedAsync("my_conv", "user1");
        Assert.Equal(ConversationOwnershipResult.AlreadyOwned, result);
        Assert.NotNull(record);
    }

    [Fact]
    public async Task Conversation_EnsureOwned_AlreadyOwnedForExistingUser()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("free_conv", "claimer", "对话");
        var (record, result) = await repo.EnsureOwnedAsync("free_conv", "claimer");
        Assert.Equal(ConversationOwnershipResult.AlreadyOwned, result);
        Assert.NotNull(record);
        Assert.Equal("claimer", record.OwnerUserId);
    }

    [Fact]
    public async Task Conversation_EnsureOwned_ThrowsForAnotherUser()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("taken_conv", "owner_a", "对话");
        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.EnsureOwnedAsync("taken_conv", "owner_b"));
    }

    [Fact]
    public async Task Conversation_ConcurrentClaimDifferentUsers()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        var userA = "user_a";
        var userB = "user_b";
        await repo.CreateOwnedAsync("concurrent_claim", userA, "抢对话");

        var taskA = repo.EnsureOwnedAsync("concurrent_claim", userA);
        var taskB = repo.EnsureOwnedAsync("concurrent_claim", userB);

        var results = await Task.WhenAll(
            taskA.ContinueWith(t => (userA, result: t.IsCompletedSuccessfully ? t.Result : default, exception: t.Exception?.InnerException)),
            taskB.ContinueWith(t => (userB, result: t.IsCompletedSuccessfully ? t.Result : default, exception: t.Exception?.InnerException)));

        var succeeded = results.Where(r => r.exception is null).ToList();
        var failed = results.Where(r => r.exception is not null).ToList();

        Assert.Single(succeeded);
        var successResult = succeeded[0];
        Assert.Equal(ConversationOwnershipResult.AlreadyOwned, successResult.result.Item2);

        Assert.Single(failed);
        Assert.IsType<KejiPersistenceException>(failed[0].exception);

        var final = await repo.GetOwnedAsync("concurrent_claim", userA);
        Assert.NotNull(final);
        Assert.Equal(userA, final!.OwnerUserId);
    }

    [Fact]
    public async Task Conversation_ConcurrentCreateDifferentUsers()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        var taskA = repo.EnsureOwnedAsync("concurrent_create", "user_a");
        var taskB = repo.EnsureOwnedAsync("concurrent_create", "user_b");

        var results = await Task.WhenAll(
            taskA.ContinueWith(t => (user: "user_a", result: t.IsCompletedSuccessfully ? t.Result : default, exception: t.Exception?.InnerException)),
            taskB.ContinueWith(t => (user: "user_b", result: t.IsCompletedSuccessfully ? t.Result : default, exception: t.Exception?.InnerException)));

        var created = results.Where(r => r.exception is null && r.result.Item2 == ConversationOwnershipResult.Created).ToList();
        Assert.Single(created);

        var thrown = results.Where(r => r.exception is not null).ToList();
        Assert.Single(thrown);
        Assert.IsType<KejiPersistenceException>(thrown[0].exception);
    }

    [Fact]
    public async Task Conversation_ConcurrentSameUser()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        var tasks = new Task<(ConversationRecord Record, ConversationOwnershipResult Result)>[10];
        for (int i = 0; i < 10; i++)
            tasks[i] = repo.EnsureOwnedAsync("same_user_conv", "user_x");

        var results = await Task.WhenAll(tasks);

        var created = results.Count(r => r.Item2 == ConversationOwnershipResult.Created);
        Assert.Equal(1, created);

        foreach (var (record, _) in results)
            Assert.Equal("user_x", record.OwnerUserId);
    }

    [Fact]
    public async Task Conversation_List_FiltersByOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("c1", "u1", "u1对话");
        await repo.CreateOwnedAsync("c2", "u2", "u2对话");
        await repo.CreateOwnedAsync("c3", "u1", "无主对话");

        var u1Convs = await repo.ListOwnedAsync("u1", limit: 50);
        Assert.Equal(2, u1Convs.Count);
        Assert.Contains(u1Convs, c => c.Id == "c1");
        Assert.Contains(u1Convs, c => c.Id == "c3");
    }

    [Fact]
    public async Task Conversation_List_BySpecificOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("c1", "user_a", "a");
        await repo.CreateOwnedAsync("c2", "user_a", "b");
        var all = await repo.ListOwnedAsync("user_a", limit: 50);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task Conversation_List_OrderedByUpdatedAtDesc()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        var convRepoWithTime = new SqliteConversationRepository(factory, new FixedTimeProvider(100.0));
        await convRepoWithTime.CreateOwnedAsync("c_old", "user_a", "旧对话");
        var convRepoWithTime2 = new SqliteConversationRepository(factory, new FixedTimeProvider(200.0));
        await convRepoWithTime2.CreateOwnedAsync("c_new", "user_a", "新对话");

        var repoForList = new SqliteConversationRepository(factory, new FixedTimeProvider(0));
        var list = await repoForList.ListOwnedAsync("user_a", limit: 50);
        Assert.Equal("c_new", list[0].Id);
        Assert.Equal("c_old", list[1].Id);
    }

    [Fact]
    public async Task Conversation_List_LimitBoundaries()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        for (int i = 0; i < 10; i++)
            await repo.CreateOwnedAsync($"c{i}", "user_a", $"对话{i}");

        Assert.Single(await repo.ListOwnedAsync("user_a", limit: 1));
        Assert.Equal(5, (await repo.ListOwnedAsync("user_a", limit: 5)).Count);
        Assert.Equal(10, (await repo.ListOwnedAsync("user_a", limit: 500)).Count);
    }

    [Fact]
    public async Task Conversation_Rename_DoesNotChangeOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        var c = await repo.CreateOwnedAsync("rename_conv", "owner1", "原名");
        await repo.RenameOwnedAsync("rename_conv", "owner1", "新名");
        var updated = await repo.GetOwnedAsync("rename_conv", "owner1");
        Assert.Equal("新名", updated!.Title);
        Assert.Equal("owner1", updated.OwnerUserId);
    }

    [Fact]
    public async Task Conversation_Rename_UsesFixedTime()
    {
        using var ctx = new TestContext(fixedTimestamp: 5000.0);
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("time_conv", "user_a", "原名");
        await repo.RenameOwnedAsync("time_conv", "user_a", "新名");
        var updated = await repo.GetOwnedAsync("time_conv", "user_a");
        Assert.Equal(5000.0, updated!.UpdatedAt);
    }

    [Fact]
    public async Task Conversation_Delete_RemovesConversation()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("del_conv", "user_a", "待删");
        Assert.True(await repo.DeleteOwnedAsync("del_conv", "user_a"));
        Assert.Null(await repo.GetOwnedAsync("del_conv", "user_a"));
    }

    [Fact]
    public async Task Conversation_Delete_Nonexistent_ReturnsFalse()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        Assert.False(await repo.DeleteOwnedAsync("nonexistent", "user_a"));
    }

    [Fact]
    public async Task Conversation_Delete_RemovesMessages()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("del_conv_msgs", "user_a", "对话");
        await msgRepo.AddOwnedAsync("del_conv_msgs", "user_a", "user", "text");
        await convRepo.DeleteOwnedAsync("del_conv_msgs", "user_a");

        var msgs = await msgRepo.ListOwnedMessagesAsync("del_conv_msgs", "user_a");
        Assert.Empty(msgs);
    }

    // ──────────────────────────────────────────────
    // Message Repository
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Message_Add_InsertsAndReturnsId()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("msg_conv", "user_a", "对话");
        var msgId = await msgRepo.AddOwnedAsync("msg_conv", "user_a", "user", "hello world");
        Assert.True(msgId > 0);
    }

    [Fact]
    public async Task Message_Add_IncrementsMessageCount()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        var c = await convRepo.CreateOwnedAsync("count_conv", "user_a");
        Assert.Equal(0, c.MessageCount);

        await msgRepo.AddOwnedAsync("count_conv", "user_a", "user", "m1");
        await msgRepo.AddOwnedAsync("count_conv", "user_a", "assistant", "m2");

        var updated = await convRepo.GetOwnedAsync("count_conv", "user_a");
        Assert.Equal(2, updated!.MessageCount);
    }

    [Fact]
    public async Task Message_Add_MissingConversation_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            msgRepo.AddOwnedAsync("does_not_exist", "user_a", "user", "content"));
        Assert.IsNotType<SqliteException>(ex);
    }

    [Fact]
    public async Task Message_Add_MissingConversation_NoResidualRows()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            msgRepo.AddOwnedAsync("missing_conv", "user_a", "user", "content"));

        using var conn = await GetOpenConnectionAsync(factory);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM messages";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Message_Add_MissingConversation_OtherCountUnaffected()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("good_conv", "user_a");
        await msgRepo.AddOwnedAsync("good_conv", "user_a", "user", "first");
        var before = await convRepo.GetOwnedAsync("good_conv", "user_a");
        Assert.Equal(1, before!.MessageCount);

        await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            msgRepo.AddOwnedAsync("does_not_exist", "user_a", "user", "content"));

        var after = await convRepo.GetOwnedAsync("good_conv", "user_a");
        Assert.Equal(1, after!.MessageCount);
    }

    [Fact]
    public async Task Message_Add_MissingConversation_ErrorMessageNoContent()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        try
        {
            await msgRepo.AddOwnedAsync("bad_conv", "user_a", "user", "超敏感内容不可泄露");
        }
        catch (KejiPersistenceException ex)
        {
            Assert.DoesNotContain("超敏感内容", ex.Message);
        }
    }

    [Fact]
    public async Task Message_Add_KeepsCorrectUpdatedAt()
    {
        using var ctx = new TestContext(fixedTimestamp: 7777.0);
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("time_conv", "user_a");
        await msgRepo.AddOwnedAsync("time_conv", "user_a", "user", "test");
        var conv = await convRepo.GetOwnedAsync("time_conv", "user_a");
        Assert.Equal(7777.0, conv!.UpdatedAt);
    }

    [Fact]
    public async Task Message_List_OrderedByCreatedAtAndId()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("order_conv", "user_a");
        await msgRepo.AddOwnedAsync("order_conv", "user_a", "user", "first");
        await msgRepo.AddOwnedAsync("order_conv", "user_a", "assistant", "second");
        await msgRepo.AddOwnedAsync("order_conv", "user_a", "user", "third");

        var msgs = await msgRepo.ListOwnedMessagesAsync("order_conv", "user_a", limit: 100);
        Assert.Equal(3, msgs.Count);
        Assert.Equal("first", msgs[0].Content);
        Assert.Equal("second", msgs[1].Content);
        Assert.Equal("third", msgs[2].Content);
    }

    [Fact]
    public async Task Message_Add_RoleAndContentParameterized()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("param_conv", "user_a");
        var id = await msgRepo.AddOwnedAsync("param_conv", "user_a", "user", "safe' OR '1'='1");
        var msgs = await msgRepo.ListOwnedMessagesAsync("param_conv", "user_a");
        Assert.Single(msgs);
        Assert.Equal("safe' OR '1'='1", msgs[0].Content);
    }

    [Fact]
    public async Task Message_List_LimitBoundaries()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("limit_conv", "user_a");
        for (int i = 0; i < 10; i++)
            await msgRepo.AddOwnedAsync("limit_conv", "user_a", "user", $"msg{i}");

        Assert.Single(await msgRepo.ListOwnedMessagesAsync("limit_conv", "user_a", limit: 1));
        Assert.Equal(5, (await msgRepo.ListOwnedMessagesAsync("limit_conv", "user_a", limit: 5)).Count);
        Assert.Equal(10, (await msgRepo.ListOwnedMessagesAsync("limit_conv", "user_a", limit: 1000)).Count);
    }

    [Fact]
    public async Task Message_ConcurrentWrites_MaintainCorrectCount()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("concurrent_conv", "user_a");

        const int count = 20;
        var tasks = new Task[count];
        for (int i = 0; i < count; i++)
        {
            var local = i;
            tasks[local] = msgRepo.AddOwnedAsync("concurrent_conv", "user_a", "user", $"msg{local}");
        }
        await Task.WhenAll(tasks);

        var updated = await convRepo.GetOwnedAsync("concurrent_conv", "user_a");
        Assert.Equal(count, updated!.MessageCount);
    }

    // ──────────────────────────────────────────────
    // Settings Repository
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Settings_Get_ReturnsDefaultForMissing()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteSettingsRepository(factory, ctx.FixedTime);

        Assert.Equal("default_val", await repo.GetAsync("missing_key", "default_val"));
    }

    [Fact]
    public async Task Settings_Set_CreatesNew()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteSettingsRepository(factory, ctx.FixedTime);

        await repo.SetAsync("theme", "dark");
        Assert.Equal("dark", await repo.GetAsync("theme"));
    }

    [Fact]
    public async Task Settings_Set_UpdatesExisting()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteSettingsRepository(factory, ctx.FixedTime);

        await repo.SetAsync("lang", "zh");
        await repo.SetAsync("lang", "en");
        Assert.Equal("en", await repo.GetAsync("lang"));
    }

    [Fact]
    public async Task Settings_GetAll_ReturnsAll()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteSettingsRepository(factory, ctx.FixedTime);

        await repo.SetAsync("k1", "v1");
        await repo.SetAsync("k2", "v2");
        var all = await repo.GetAllAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal("v1", all["k1"]);
        Assert.Equal("v2", all["k2"]);
    }

    [Fact]
    public async Task Settings_KeySqlInjection_HandledAsValue()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteSettingsRepository(factory, ctx.FixedTime);

        await repo.SetAsync("key' OR '1'='1", "injected");
        var val = await repo.GetAsync("key' OR '1'='1");
        Assert.Equal("injected", val);
    }

    [Fact]
    public async Task Settings_Get_NullKey_Throws()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteSettingsRepository(factory, ctx.FixedTime);

        await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.GetAsync(null!));
    }

    [Fact]
    public async Task Settings_Get_EmptyKey_Throws()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteSettingsRepository(factory, ctx.FixedTime);

        await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.GetAsync(""));
    }

    [Fact]
    public async Task Settings_Get_WhitespaceKey_Throws()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteSettingsRepository(factory, ctx.FixedTime);

        await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.GetAsync("   "));
    }

    [Fact]
    public async Task Settings_Set_NullKey_Throws()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteSettingsRepository(factory, ctx.FixedTime);

        await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.SetAsync(null!, "val"));
    }

    [Fact]
    public async Task Settings_Set_EmptyKey_Throws()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteSettingsRepository(factory, ctx.FixedTime);

        await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.SetAsync("", "val"));
    }

    [Fact]
    public async Task Settings_Set_WhitespaceKey_Throws()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteSettingsRepository(factory, ctx.FixedTime);

        await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.SetAsync("   ", "val"));
    }

    [Fact]
    public async Task Settings_Get_EmptyKey_ErrorMessageNoDefault()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteSettingsRepository(factory, ctx.FixedTime);

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.GetAsync("", "超敏感默认值"));
        Assert.DoesNotContain("超敏感默认值", ex.Message);
    }

    [Fact]
    public async Task Settings_Set_EmptyKey_ErrorMessageNoValue()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteSettingsRepository(factory, ctx.FixedTime);

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.SetAsync("", "超敏感值"));
        Assert.DoesNotContain("超敏感值", ex.Message);
    }

    [Fact]
    public async Task Settings_Set_UsesFixedTime()
    {
        using var ctx = new TestContext(fixedTimestamp: 8888.0);
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteSettingsRepository(factory, ctx.FixedTime);

        await repo.SetAsync("time_key", "val");
        using var conn = await GetOpenConnectionAsync(factory);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT updated_at FROM settings WHERE key = 'time_key'";
        var result = await cmd.ExecuteScalarAsync();
        Assert.Equal(8888.0, Convert.ToDouble(result));
    }

    // ──────────────────────────────────────────────
    // DI Registration
    // ──────────────────────────────────────────────

    [Fact]
    public void DI_Registration_DoesNotCreateDatabaseFile()
    {
        using var ctx = new TestContext();
        var services = new ServiceCollection();
        services.AddKejiPersistenceFoundation(o =>
        {
            o.ProjectRoot = ctx.Dir;
            o.DatabasePath = "test.db";
        });

        Assert.False(File.Exists(ctx.DbPath));
        services.BuildServiceProvider();
        Assert.False(File.Exists(ctx.DbPath));
    }

    [Fact]
    public void DI_ResolveServices_DoesNotCreateDatabaseFile()
    {
        using var ctx = new TestContext();
        var services = new ServiceCollection();
        services.AddKejiPersistenceFoundation(o =>
        {
            o.ProjectRoot = ctx.Dir;
            o.DatabasePath = "test.db";
        });
        var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IUserRepository>();
        sp.GetRequiredService<IConversationRepository>();
        sp.GetRequiredService<IMessageRepository>();
        sp.GetRequiredService<ISettingsRepository>();
        sp.GetRequiredService<IUnixTimeProvider>();

        Assert.False(File.Exists(ctx.DbPath));
    }

    [Fact]
    public async Task DI_InitializeAsync_CreatesDatabaseFile()
    {
        using var ctx = new TestContext();
        var services = new ServiceCollection();
        services.AddKejiPersistenceFoundation(o =>
        {
            o.ProjectRoot = ctx.Dir;
            o.DatabasePath = "test.db";
        });
        var sp = services.BuildServiceProvider();

        var initializer = sp.GetRequiredService<IKejiDatabaseInitializer>();
        await initializer.InitializeAsync();

        Assert.True(File.Exists(ctx.DbPath));
    }

    // ──────────────────────────────────────────────
    // Security: No leak of sensitive data
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Security_DuplicateUsernameException_DoesNotContainPasswordHash()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        await repo.CreateAsync("safedup", "$2a$12$abcdefghijklmnopqrstuv", TestRoleMember);
        var ex = await Assert.ThrowsAsync<DuplicateUsernameException>(() =>
            repo.CreateAsync("safedup", "$2a$12$zzzzzzzzzzzzzzzzzzzzzz", TestRoleMember));

        Assert.DoesNotContain("$2a$", ex.Message);
        Assert.DoesNotContain("$2a$", ex.ToString());
    }

    [Fact]
    public async Task Security_UserException_DoesNotContainPasswordHash()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        try
        {
            await repo.CreateAsync("", "$2a$12$secret_hash_value", TestRoleMember);
        }
        catch (KejiPersistenceException ex)
        {
            Assert.DoesNotContain("$2a$", ex.Message);
            Assert.DoesNotContain("secret_hash", ex.Message);
        }
    }

    [Fact]
    public async Task Security_MessageException_DoesNotContainContent()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        try
        {
            await msgRepo.AddOwnedAsync("nonexistent", "user_a", "", "超敏感内容不可泄露");
        }
        catch (KejiPersistenceException ex)
        {
            Assert.DoesNotContain("超敏感内容", ex.Message);
        }
    }

    [Fact]
    public async Task Security_SettingsException_DoesNotContainValue()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteSettingsRepository(factory, ctx.FixedTime);

        try
        {
            await repo.SetAsync("", "my_secret_api_key_12345");
        }
        catch (KejiPersistenceException ex)
        {
            Assert.DoesNotContain("my_secret_api_key", ex.Message);
        }
    }

    [Fact]
    public void Security_Options_DoesNotModifyProcessEnv()
    {
        var key = "KJ_PERSIST_TEST_" + Guid.NewGuid().ToString("N")[..8];
        var orig = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "should_not_be_read");
            var opts = new KejiPersistenceOptions();
            Assert.NotNull(opts);
        }
        finally
        {
            if (orig is null)
                Environment.SetEnvironmentVariable(key, null);
        }
    }

    [Fact]
    public void Security_DoesNotModifyRealKejiDb()
    {
        var realDb = System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "keji.db");

        Assert.False(File.Exists(realDb), $"Test should not modify real database at {realDb}");
    }

    [Fact]
    public async Task All_Methods_SupportCancellationToken()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var userRepo = new SqliteUserRepository(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);
        var settingsRepo = new SqliteSettingsRepository(factory, ctx.FixedTime);

        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        var uid = await userRepo.CreateAsync("ctu", "hash", TestRoleMember, cancellationToken: ct);
        Assert.NotNull(uid);

        var conv = await convRepo.CreateOwnedAsync("ct_conv", "user_a", cancellationToken: ct);
        Assert.NotNull(conv);

        var msgId = await msgRepo.AddOwnedAsync("ct_conv", "user_a", "user", "test", cancellationToken: ct);
        Assert.True(msgId > 0);

        await settingsRepo.SetAsync("ct_key", "ct_val", ct);
        Assert.Equal("ct_val", await settingsRepo.GetAsync("ct_key", cancellationToken: ct));
    }

    // ──────────────────────────────────────────────
    // Pre-Cancelled Token Tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Cancellation_Initialize_PreCancelled_ThrowsOperationCanceled()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        var initializer = new KejiDatabaseInitializer(factory, ctx.FixedTime);
        var preCancelled = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            initializer.InitializeAsync(preCancelled));
    }

    [Fact]
    public async Task Cancellation_UserCreate_PreCancelled_ThrowsOperationCanceled()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);
        var preCancelled = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            repo.CreateAsync("u", "h", TestRoleMember, cancellationToken: preCancelled));
    }

    [Fact]
    public async Task Cancellation_UserDelete_PreCancelled_ThrowsOperationCanceled()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);
        var preCancelled = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            repo.DeleteAsync("nonexistent", preCancelled));
    }

    [Fact]
    public async Task Cancellation_ConversationEnsureOwned_PreCancelled_ThrowsOperationCanceled()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var preCancelled = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            repo.EnsureOwnedAsync("test_conv", "user", cancellationToken: preCancelled));
    }

    [Fact]
    public async Task Cancellation_MessageAdd_PreCancelled_ThrowsOperationCanceled()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteMessageRepository(factory, ctx.FixedTime);
        var preCancelled = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            repo.AddOwnedAsync("test_conv", "user_a", "user", "test", cancellationToken: preCancelled));
    }

    [Fact]
    public async Task Cancellation_SettingsSet_PreCancelled_ThrowsOperationCanceled()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteSettingsRepository(factory, ctx.FixedTime);
        var preCancelled = new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            repo.SetAsync("k", "v", preCancelled));
    }

    // ──────────────────────────────────────────────
    // Exception Boundary Tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task User_Create_DuplicateUsername_PreciseExtendedErrorCode()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        await repo.CreateAsync("precise_dup", "h1", TestRoleMember);
        var ex = await Assert.ThrowsAsync<DuplicateUsernameException>(() =>
            repo.CreateAsync("precise_dup", "h2", TestRoleMember));
        Assert.Equal("precise_dup", ex.Username);
    }

    [Fact]
    public async Task User_Create_IdPrimaryKeyConflict_TranslatorNotDuplicate()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        var uid = await repo.CreateAsync("idconflict_user", "h", TestRoleMember);
        using var conn = await GetOpenConnectionAsync(factory);
        using var insertCmd = conn.CreateCommand();
        insertCmd.CommandText = "INSERT INTO users (id, username, password_hash, role, created_at) VALUES (@id, @u2, @pwh, 'member', 100.0)";
        insertCmd.Parameters.AddWithValue("@id", uid + "x");
        insertCmd.Parameters.AddWithValue("@u2", "other_user");
        insertCmd.Parameters.AddWithValue("@pwh", "hash");
        await insertCmd.ExecuteNonQueryAsync();

        using var conflictCmd = conn.CreateCommand();
        conflictCmd.CommandText = "INSERT INTO users (id, username, password_hash, role, created_at) VALUES (@id, @u2, @pwh, 'member', 100.0)";
        conflictCmd.Parameters.AddWithValue("@id", uid);
        conflictCmd.Parameters.AddWithValue("@u2", "unique_user");
        conflictCmd.Parameters.AddWithValue("@pwh", "h2");
        var sqliteEx = await Assert.ThrowsAsync<SqliteException>(() =>
            conflictCmd.ExecuteNonQueryAsync());
        Assert.Equal(19, sqliteEx.SqliteErrorCode);

        var translated = SqliteExceptionTranslator.Create(sqliteEx, "TestOperation", "safe_id");
        Assert.IsType<KejiPersistenceException>(translated);
        Assert.IsNotType<DuplicateUsernameException>(translated);
        Assert.Equal(19, translated.ErrorCode);
        Assert.Null(translated.InnerException);
        Assert.DoesNotContain(sqliteEx.Message, translated.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task User_Delete_TriggerFailure_InnerExceptionNotSqlite()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        var uid = await repo.CreateAsync("del_inner_user", "h", TestRoleMember);

        using (var triggerConn = await GetOpenConnectionAsync(factory))
        {
            using var createTrigger = triggerConn.CreateCommand();
            createTrigger.CommandText = """
                CREATE TRIGGER fail_user_delete_inner BEFORE DELETE ON users
                BEGIN
                    SELECT RAISE(ABORT, 'forced failure');
                END;
                """;
            await createTrigger.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.DeleteAsync(uid));

        Assert.Null(ex.InnerException);
        Assert.DoesNotContain("forced failure", ex.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(19, ex.ErrorCode);
    }

    [Fact]
    public async Task User_Update_TriggerFailure_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        var uid = await repo.CreateAsync("update_trig_user", "h", TestRoleMember);

        using (var triggerConn = await GetOpenConnectionAsync(factory))
        {
            using var createTrigger = triggerConn.CreateCommand();
            createTrigger.CommandText = """
                CREATE TRIGGER fail_user_update BEFORE UPDATE ON users
                BEGIN
                    SELECT RAISE(ABORT, 'update blocked');
                END;
                """;
            await createTrigger.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.UpdateAsync(uid, new UpdateUserCommand { DisplayName = "new_name" }));
        Assert.IsNotType<DuplicateUsernameException>(ex);
        Assert.Equal(19, ex.ErrorCode);
    }

    [Fact]
    public async Task Message_Add_InsertTriggerFailure_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("msg_trig_conv", "user_a");

        using (var triggerConn = await GetOpenConnectionAsync(factory))
        {
            using var createTrigger = triggerConn.CreateCommand();
            createTrigger.CommandText = """
                CREATE TRIGGER fail_msg_insert BEFORE INSERT ON messages
                BEGIN
                    SELECT RAISE(ABORT, 'insert blocked');
                END;
                """;
            await createTrigger.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            msgRepo.AddOwnedAsync("msg_trig_conv", "user_a", "user", "test content"));
        Assert.IsNotType<SqliteException>(ex);
        Assert.Equal(19, ex.ErrorCode);

        var conv = await convRepo.GetOwnedAsync("msg_trig_conv", "user_a");
        Assert.NotNull(conv);
        Assert.Equal(0, conv.MessageCount);
    }

    [Fact]
    public async Task Settings_Set_TriggerFailure_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteSettingsRepository(factory, ctx.FixedTime);

        using (var triggerConn = await GetOpenConnectionAsync(factory))
        {
            using var createTrigger = triggerConn.CreateCommand();
            createTrigger.CommandText = """
                CREATE TRIGGER fail_settings_insert BEFORE INSERT ON settings
                BEGIN
                    SELECT RAISE(ABORT, 'settings blocked');
                END;
                """;
            await createTrigger.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.SetAsync("blocked_key", "value"));
        Assert.IsNotType<SqliteException>(ex);
        Assert.Equal(19, ex.ErrorCode);
    }

    [Fact]
    public async Task Conversation_Rename_TriggerFailure_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("rename_trig_conv", "user_a");

        using (var triggerConn = await GetOpenConnectionAsync(factory))
        {
            using var createTrigger = triggerConn.CreateCommand();
            createTrigger.CommandText = """
                CREATE TRIGGER fail_conv_rename BEFORE UPDATE ON conversations
                BEGIN
                    SELECT RAISE(ABORT, 'rename blocked');
                END;
                """;
            await createTrigger.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.RenameOwnedAsync("rename_trig_conv", "user_a", "new name"));
        Assert.IsNotType<SqliteException>(ex);
        Assert.Equal(19, ex.ErrorCode);
    }

    [Fact]
    public async Task Conversation_Create_TriggerFailure_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        using (var triggerConn = await GetOpenConnectionAsync(factory))
        {
            using var createTrigger = triggerConn.CreateCommand();
            createTrigger.CommandText = """
                CREATE TRIGGER fail_conv_create BEFORE INSERT ON conversations
                BEGIN
                    SELECT RAISE(ABORT, 'create blocked');
                END;
                """;
            await createTrigger.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.CreateOwnedAsync("blocked_conv", "user_a"));
        Assert.IsNotType<SqliteException>(ex);
        Assert.Equal(19, ex.ErrorCode);
    }

    [Fact]
    public async Task Message_Add_TriggerFailure_ErrorMessageDoesNotLeakContent()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("leak_conv", "user_a");

        using (var triggerConn = await GetOpenConnectionAsync(factory))
        {
            using var createTrigger = triggerConn.CreateCommand();
            createTrigger.CommandText = """
                CREATE TRIGGER fail_msg_leak BEFORE INSERT ON messages
                BEGIN
                    SELECT RAISE(ABORT, 'secret leak test');
                END;
                """;
            await createTrigger.ExecuteNonQueryAsync();
        }

        try
        {
            await msgRepo.AddOwnedAsync("leak_conv", "user_a", "user", "超敏感内容不可泄露");
        }
        catch (KejiPersistenceException ex)
        {
            Assert.DoesNotContain("超敏感内容", ex.Message);
            Assert.DoesNotContain("secret leak test", ex.Message);
        }
    }

    // ──────────────────────────────────────────────
    // Read Method Failure Tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task User_Count_SchemaCorruption_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        using (var conn = await GetOpenConnectionAsync(factory))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP TABLE users";
            await cmd.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.CountAsync());
        Assert.IsNotType<SqliteException>(ex);
        Assert.Null(ex.InnerException);
        Assert.True(ex.ErrorCode > 0);
    }

    [Fact]
    public async Task User_GetByUsername_SchemaCorruption_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        using (var conn = await GetOpenConnectionAsync(factory))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP TABLE users";
            await cmd.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.GetByUsernameAsync("test"));
        Assert.IsNotType<SqliteException>(ex);
        Assert.Null(ex.InnerException);
        Assert.True(ex.ErrorCode > 0);
    }

    [Fact]
    public async Task User_GetById_SchemaCorruption_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        using (var conn = await GetOpenConnectionAsync(factory))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP TABLE users";
            await cmd.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.GetByIdAsync("nonexistent"));
        Assert.IsNotType<SqliteException>(ex);
        Assert.Null(ex.InnerException);
        Assert.True(ex.ErrorCode > 0);
    }

    [Fact]
    public async Task User_List_SchemaCorruption_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        using (var conn = await GetOpenConnectionAsync(factory))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP TABLE users";
            await cmd.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.ListAsync());
        Assert.IsNotType<SqliteException>(ex);
        Assert.Null(ex.InnerException);
        Assert.True(ex.ErrorCode > 0);
    }

    [Fact]
    public async Task User_TouchLogin_SchemaCorruption_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteUserRepository(factory, ctx.FixedTime);

        using (var conn = await GetOpenConnectionAsync(factory))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP TABLE users";
            await cmd.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.TouchLoginAsync("nonexistent"));
        Assert.IsNotType<SqliteException>(ex);
        Assert.Null(ex.InnerException);
        Assert.True(ex.ErrorCode > 0);
    }

    [Fact]
    public async Task Conversation_Get_SchemaCorruption_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        using (var conn = await GetOpenConnectionAsync(factory))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP TABLE conversations";
            await cmd.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.GetOwnedAsync("test_conv", "user_a"));
        Assert.IsNotType<SqliteException>(ex);
        Assert.Null(ex.InnerException);
        Assert.True(ex.ErrorCode > 0);
    }

    [Fact]
    public async Task Conversation_List_SchemaCorruption_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        using (var conn = await GetOpenConnectionAsync(factory))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP TABLE conversations";
            await cmd.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.ListOwnedAsync("user_a"));
        Assert.IsNotType<SqliteException>(ex);
        Assert.Null(ex.InnerException);
        Assert.True(ex.ErrorCode > 0);
    }

    [Fact]
    public async Task Conversation_Delete_SchemaCorruption_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        using (var conn = await GetOpenConnectionAsync(factory))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP TABLE conversations";
            await cmd.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.DeleteOwnedAsync("test_conv", "user_a"));
        Assert.IsNotType<SqliteException>(ex);
        Assert.Null(ex.InnerException);
        Assert.True(ex.ErrorCode > 0);
    }

    [Fact]
    public async Task Message_List_SchemaCorruption_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteMessageRepository(factory, ctx.FixedTime);

        using (var conn = await GetOpenConnectionAsync(factory))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP TABLE messages";
            await cmd.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.ListOwnedMessagesAsync("test_conv", "user_a"));
        Assert.IsNotType<SqliteException>(ex);
        Assert.Null(ex.InnerException);
        Assert.True(ex.ErrorCode > 0);
    }

    [Fact]
    public async Task Settings_Get_SchemaCorruption_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteSettingsRepository(factory, ctx.FixedTime);

        using (var conn = await GetOpenConnectionAsync(factory))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP TABLE settings";
            await cmd.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.GetAsync("test_key"));
        Assert.IsNotType<SqliteException>(ex);
        Assert.Null(ex.InnerException);
        Assert.True(ex.ErrorCode > 0);
    }

    [Fact]
    public async Task Settings_GetAll_SchemaCorruption_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteSettingsRepository(factory, ctx.FixedTime);

        using (var conn = await GetOpenConnectionAsync(factory))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP TABLE settings";
            await cmd.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.GetAllAsync());
        Assert.IsNotType<SqliteException>(ex);
        Assert.Null(ex.InnerException);
        Assert.True(ex.ErrorCode > 0);
    }

    // ──────────────────────────────────────────────
    // Transaction Boundary Tests
    // ──────────────────────────────────────────────

    [Fact]
    public void Translator_Create_PreservesErrorCode()
    {
        var sqliteEx = new SqliteException("test constraint", 19, 2067);
        var translated = SqliteExceptionTranslator.Create(sqliteEx, "TestOp", "safe_id");
        Assert.Equal(19, translated.ErrorCode);
        Assert.Null(translated.InnerException);
        Assert.Contains("TestOp", translated.Message);
        Assert.Contains("safe_id", translated.Message);
    }

    [Fact]
    public void Translator_Create_NoEntityId_OmitsIdSuffix()
    {
        var sqliteEx = new SqliteException("test", 1, 1);
        var translated = SqliteExceptionTranslator.Create(sqliteEx, "TestOp");
        Assert.DoesNotContain("(id:", translated.Message);
    }

    [Fact]
    public async Task EnsureOwned_CancellationAfterBeginTransaction_RollsBack()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);

        using var cts = new CancellationTokenSource();
        var ready = new TaskCompletionSource();
        var proceed = new TaskCompletionSource();
        var blockingFactory = new BlockingConnectionFactory(factory, ready, proceed);
        var repo = new SqliteConversationRepository(blockingFactory, ctx.FixedTime);

        var repoTask = repo.EnsureOwnedAsync("cancel_eo", "user", cancellationToken: cts.Token);

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        proceed.TrySetResult();

        try { await repoTask; Assert.Fail("Expected OCE"); }
        catch (OperationCanceledException) { }

        Assert.Null(await repo.GetOwnedAsync("cancel_eo", "user"));
    }

    [Fact]
    public async Task ConversationDelete_CancellationAfterBeginTransaction_RollsBack()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        await convRepo.CreateOwnedAsync("cancel_del", "user_a");

        using var cts = new CancellationTokenSource();
        var ready = new TaskCompletionSource();
        var proceed = new TaskCompletionSource();
        var blockingFactory = new BlockingConnectionFactory(factory, ready, proceed);
        var repo = new SqliteConversationRepository(blockingFactory, ctx.FixedTime);

        var repoTask = repo.DeleteOwnedAsync("cancel_del", "user_a", cancellationToken: cts.Token);

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        proceed.TrySetResult();

        try { await repoTask; Assert.Fail("Expected OCE"); }
        catch (OperationCanceledException) { }

        Assert.NotNull(await convRepo.GetOwnedAsync("cancel_del", "user_a"));
    }

    [Fact]
    public async Task UserDelete_CancellationAfterBeginTransaction_RollsBack()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var userRepo = new SqliteUserRepository(factory, ctx.FixedTime);
        var uid = await userRepo.CreateAsync("cancel_del_user", "h", TestRoleMember);

        using var cts = new CancellationTokenSource();
        var ready = new TaskCompletionSource();
        var proceed = new TaskCompletionSource();
        var blockingFactory = new BlockingConnectionFactory(factory, ready, proceed);
        var repo = new SqliteUserRepository(blockingFactory, ctx.FixedTime);

        var repoTask = repo.DeleteAsync(uid, cts.Token);

        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        proceed.TrySetResult();

        try { await repoTask; Assert.Fail("Expected OCE"); }
        catch (OperationCanceledException) { }

        Assert.NotNull(await userRepo.GetByIdAsync(uid));
    }

    [Fact]
    public async Task Message_Add_CancellationAfterBeginTransaction_RollsBack()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        await convRepo.CreateOwnedAsync("cancel_after_tx", "user_a");

        using var cts = new CancellationTokenSource();
        var ready = new TaskCompletionSource();
        var proceed = new TaskCompletionSource();

        var blockingFactory = new BlockingConnectionFactory(factory, ready, proceed);
        var msgRepo = new SqliteMessageRepository(blockingFactory, ctx.FixedTime);

        var repoTask = msgRepo.AddOwnedAsync("cancel_after_tx", "user_a", "user", "test", cancellationToken: cts.Token);

        // Wait for connection to be opened (but not yet returned to AddAsync)
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Cancel the token before letting OpenConnectionAsync return
        cts.Cancel();
        proceed.TrySetResult();

        try
        {
            await repoTask;
            Assert.Fail("Expected OperationCanceledException but no exception was thrown.");
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        var conv = await convRepo.GetOwnedAsync("cancel_after_tx", "user_a");
        Assert.NotNull(conv);
        Assert.Equal(0, conv.MessageCount);

        var msgs = await new SqliteMessageRepository(factory, ctx.FixedTime).ListOwnedMessagesAsync("cancel_after_tx", "user_a");
        Assert.Empty(msgs);
    }

    private sealed class BlockingConnectionFactory : ISqliteConnectionFactory
    {
        private readonly ISqliteConnectionFactory _inner;
        private readonly TaskCompletionSource _ready;
        private readonly TaskCompletionSource _proceed;

        public BlockingConnectionFactory(ISqliteConnectionFactory inner, TaskCompletionSource ready, TaskCompletionSource proceed)
        {
            _inner = inner;
            _ready = ready;
            _proceed = proceed;
        }

        public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
        {
            var conn = await _inner.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            _ready.TrySetResult();
            await _proceed.Task.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            return conn;
        }
    }

    [Fact]
    public async Task Transaction_EnsureOwned_FirstSqlFailure_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        using (var conn = await GetOpenConnectionAsync(factory))
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DROP TABLE conversations";
            await cmd.ExecuteNonQueryAsync();
        }

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.EnsureOwnedAsync("test_conv", "user"));
        Assert.IsNotType<SqliteException>(ex);
        Assert.Null(ex.InnerException);
        Assert.True(ex.ErrorCode > 0);
    }

    // ──────────────────────────────────────────────
    // Ownership Isolation Tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task Conversation_Get_WithoutOwner_ReturnsAny()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("c_get_any", "user_a", "对话");
        var result = await repo.GetOwnedAsync("c_get_any", "user_a");
        Assert.NotNull(result);
        Assert.Equal("user_a", result!.OwnerUserId);
    }

    [Fact]
    public async Task Conversation_Get_WithOwner_ReturnsOwned()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("c_get_own", "user_a", "我的对话");
        var result = await repo.GetOwnedAsync("c_get_own", ownerUserId: "user_a");
        Assert.NotNull(result);
        Assert.Equal("user_a", result!.OwnerUserId);
    }

    [Fact]
    public async Task Conversation_Get_WithOwner_ReturnsNullForOtherOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("c_get_other", "user_a", "别人的对话");
        var result = await repo.GetOwnedAsync("c_get_other", ownerUserId: "user_b");
        Assert.Null(result);
    }

    [Fact]
    public async Task Conversation_Get_WithOwner_ReturnsNullForNonexistent()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        var result = await repo.GetOwnedAsync("nonexistent", ownerUserId: "user_a");
        Assert.Null(result);
    }

    [Fact]
    public async Task Conversation_Get_WithOwner_UnownedIsHidden()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("c_other_owned", "user_b", "别人的对话");
        var result = await repo.GetOwnedAsync("c_other_owned", ownerUserId: "user_a");
        Assert.Null(result);
    }

    [Fact]
    public async Task Conversation_Rename_WithoutOwner_AlwaysWorks()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("c_ren_no_owner", "user_a", "原名");
        var ok = await repo.RenameOwnedAsync("c_ren_no_owner", "user_a", "新名");
        Assert.True(ok);
        var updated = await repo.GetOwnedAsync("c_ren_no_owner", "user_a");
        Assert.Equal("新名", updated!.Title);
    }

    [Fact]
    public async Task Conversation_Rename_WithOwner_SucceedsForOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("c_ren_owner", "user_a", "原名");
        var ok = await repo.RenameOwnedAsync("c_ren_owner", "user_a", "新名");
        Assert.True(ok);
        var updated = await repo.GetOwnedAsync("c_ren_owner", "user_a");
        Assert.Equal("新名", updated!.Title);
    }

    [Fact]
    public async Task Conversation_Rename_WithOwner_FailsForOtherOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("c_ren_other", "user_a", "原名");
        var ok = await repo.RenameOwnedAsync("c_ren_other", "user_b", "新名");
        Assert.False(ok);
        var updated = await repo.GetOwnedAsync("c_ren_other", "user_a");
        Assert.Equal("原名", updated!.Title);
    }

    [Fact]
    public async Task Conversation_Rename_WithOwner_FailsForUnowned()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("c_ren_unowned", "user_b", "原名");
        var ok = await repo.RenameOwnedAsync("c_ren_unowned", "user_a", "新名");
        Assert.False(ok);
        var updated = await repo.GetOwnedAsync("c_ren_unowned", "user_b");
        Assert.Equal("原名", updated!.Title);
    }

    [Fact]
    public async Task Conversation_Rename_WithOwner_FailsForNonexistent()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        var ok = await repo.RenameOwnedAsync("nonexistent", "user_a", "新名");
        Assert.False(ok);
    }

    [Fact]
    public async Task Conversation_Delete_WithoutOwner_AlwaysWorks()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("c_del_no_owner", "user_a", "待删");
        Assert.True(await repo.DeleteOwnedAsync("c_del_no_owner", "user_a"));
        Assert.Null(await repo.GetOwnedAsync("c_del_no_owner", "user_a"));
    }

    [Fact]
    public async Task Conversation_Delete_WithOwner_SucceedsForOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("c_del_owner", "user_a", "待删");
        Assert.True(await repo.DeleteOwnedAsync("c_del_owner", "user_a"));
        Assert.Null(await repo.GetOwnedAsync("c_del_owner", "user_a"));
    }

    [Fact]
    public async Task Conversation_Delete_WithOwner_FailsForOtherOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("c_del_other", "user_a", "别人的对话");
        Assert.False(await repo.DeleteOwnedAsync("c_del_other", "user_b"));
        Assert.NotNull(await repo.GetOwnedAsync("c_del_other", "user_a"));
    }

    [Fact]
    public async Task Conversation_Delete_WithOwner_FailsForUnowned()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("c_del_unowned", "user_b", "无主对话");
        Assert.False(await repo.DeleteOwnedAsync("c_del_unowned", "user_a"));
        Assert.NotNull(await repo.GetOwnedAsync("c_del_unowned", "user_b"));
    }

    [Fact]
    public async Task Conversation_Delete_WithOwner_FailsForNonexistent()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        Assert.False(await repo.DeleteOwnedAsync("nonexistent", ownerUserId: "user_a"));
    }

    [Fact]
    public async Task Message_Add_WithoutOwner_AlwaysWorks()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("msg_add_no_owner", "user_a", "对话");
        var msgId = await msgRepo.AddOwnedAsync("msg_add_no_owner", "user_a", "user", "hello");
        Assert.True(msgId > 0);
    }

    [Fact]
    public async Task Message_Add_WithOwner_SucceedsForOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("msg_add_own", "user_a", "我的对话");
        var msgId = await msgRepo.AddOwnedAsync("msg_add_own", "user_a", "user", "hello");
        Assert.True(msgId > 0);
    }

    [Fact]
    public async Task Message_Add_WithOwner_ThrowsForOtherOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("msg_add_other", "user_a", "别人的对话");
        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            msgRepo.AddOwnedAsync("msg_add_other", "user_b", "user", "hello"));
        Assert.DoesNotContain("hello", ex.Message);
    }

    [Fact]
    public async Task Message_Add_WithOwner_ThrowsForUnowned()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("msg_add_unowned", "user_b", "无主对话");
        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            msgRepo.AddOwnedAsync("msg_add_unowned", "user_a", "user", "hello"));
        Assert.DoesNotContain("hello", ex.Message);
    }

    [Fact]
    public async Task Message_Add_WithOwner_ThrowsForMissingConversation()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            msgRepo.AddOwnedAsync("nonexistent", "user_a", "user", "hello"));
        Assert.IsNotType<SqliteException>(ex);
    }

    [Fact]
    public async Task Message_Add_WithOwner_NoResidualRowsOnFailure()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("msg_residual", "user_a", "对话");
        await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            msgRepo.AddOwnedAsync("msg_residual", "user_b", "user", "secret"));

        using var conn = await GetOpenConnectionAsync(factory);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM messages WHERE conversation_id = 'msg_residual'";
        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Message_List_WithoutOwner_ShowsAll()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("msg_list_all", "user_a", "对话");
        await msgRepo.AddOwnedAsync("msg_list_all", "user_a", "user", "hello");
        var msgs = await msgRepo.ListOwnedMessagesAsync("msg_list_all", "user_a");
        Assert.Single(msgs);
    }

    [Fact]
    public async Task Message_List_WithOwner_ShowsForOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("msg_list_own", "user_a", "对话");
        await msgRepo.AddOwnedAsync("msg_list_own", "user_a", "user", "hello");
        var msgs = await msgRepo.ListOwnedMessagesAsync("msg_list_own", "user_a");
        Assert.Single(msgs);
        Assert.Equal("hello", msgs[0].Content);
    }

    [Fact]
    public async Task Message_List_WithOwner_EmptyForOtherOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("msg_list_other", "user_a", "对话");
        await msgRepo.AddOwnedAsync("msg_list_other", "user_a", "user", "hello");
        var msgs = await msgRepo.ListOwnedMessagesAsync("msg_list_other", "user_b");
        Assert.Empty(msgs);
    }

    [Fact]
    public async Task Message_List_WithOwner_EmptyForUnowned()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("msg_list_unowned", "user_b", "无主对话");
        await msgRepo.AddOwnedAsync("msg_list_unowned", "user_b", "user", "hello");
        var msgs = await msgRepo.ListOwnedMessagesAsync("msg_list_unowned", "user_a");
        Assert.Empty(msgs);
    }

    [Fact]
    public async Task Message_List_WithOwner_EmptyForNonexistent()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        var msgs = await msgRepo.ListOwnedMessagesAsync("nonexistent", "user_a");
        Assert.Empty(msgs);
    }

    [Fact]
    public async Task Conversation_List_WithOwner_HidesOtherOwners()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("c_list_a1", "user_a", "a1");
        await repo.CreateOwnedAsync("c_list_b1", "user_b", "b1");
        await repo.CreateOwnedAsync("c_list_a2", "user_a", "a2");
        await repo.CreateOwnedAsync("c_list_b2", "user_b", "b2");

        var aConvs = await repo.ListOwnedAsync("user_a", limit: 50);
        Assert.Equal(2, aConvs.Count);
        Assert.All(aConvs, c => Assert.Equal("user_a", c.OwnerUserId));
    }

    [Fact]
    public async Task Conversation_List_WithOwner_ExcludesOtherOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("c_list_owned", "user_a", "有主");
        await repo.CreateOwnedAsync("c_list_other", "user_b", "其他");

        var aConvs = await repo.ListOwnedAsync("user_a", limit: 50);
        Assert.Single(aConvs);
        Assert.Equal("c_list_owned", aConvs[0].Id);
    }

    [Fact]
    public async Task Conversation_List_OwnedOnlyShowsOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateOwnedAsync("c_list_all1", "user_a", "a");
        await repo.CreateOwnedAsync("c_list_all2", "user_b", "b");
        await repo.CreateOwnedAsync("c_list_all3", "user_a", "a3");

        var all = await repo.ListOwnedAsync("user_a", limit: 50);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task Conversation_Delete_WithOwner_RemovesMessagesOnlyForOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("c_del_msgs_own", "user_a", "对话");
        await msgRepo.AddOwnedAsync("c_del_msgs_own", "user_a", "user", "hello");

        Assert.True(await convRepo.DeleteOwnedAsync("c_del_msgs_own", "user_a"));
        var msgs = await msgRepo.ListOwnedMessagesAsync("c_del_msgs_own", "user_a");
        Assert.Empty(msgs);
    }

    [Fact]
    public async Task Conversation_Delete_WithOwner_FailsForOtherOwner_MessagesPreserved()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("c_del_msgs_other", "user_a", "对话");
        await msgRepo.AddOwnedAsync("c_del_msgs_other", "user_a", "user", "hello");

        Assert.False(await convRepo.DeleteOwnedAsync("c_del_msgs_other", "user_b"));
        var msgs = await msgRepo.ListOwnedMessagesAsync("c_del_msgs_other", "user_a");
        Assert.Single(msgs);
    }

    [Fact]
    public async Task EnsureOwned_WithOwner_IsolationPreserved()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        var (record1, result1) = await repo.EnsureOwnedAsync("eo_owner", "user_a");
        Assert.Equal(ConversationOwnershipResult.Created, result1);

        var ex = await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            repo.EnsureOwnedAsync("eo_owner", "user_b"));

        var fetched = await repo.GetOwnedAsync("eo_owner", "user_a");
        Assert.NotNull(fetched);

        var hidden = await repo.GetOwnedAsync("eo_owner", "user_b");
        Assert.Null(hidden);
    }

    [Fact]
    public async Task Message_Add_WithOwner_Failure_DoesNotLeakSensitiveContent()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateOwnedAsync("leak_test", "user_a", "对话");
        try
        {
            await msgRepo.AddOwnedAsync("leak_test", "user_b", "user", "超敏感API_Key_12345");
        }
        catch (KejiPersistenceException ex)
        {
            Assert.DoesNotContain("超敏感API_Key", ex.Message);
        }
    }

    private static async Task<List<string>> GetTableNamesAsync(SqliteConnection conn)
    {
        var list = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(reader.GetString(0));
        return list;
    }

    private static async Task<List<string>> GetIndexNamesAsync(SqliteConnection conn)
    {
        var list = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name IS NOT NULL ORDER BY name";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(reader.GetString(0));
        return list;
    }

    private static async Task<List<string>> GetColumnNamesAsync(SqliteConnection conn, string table)
    {
        var list = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT name FROM pragma_table_info('{table}') ORDER BY cid";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            list.Add(reader.GetString(0));
        return list;
    }
}
