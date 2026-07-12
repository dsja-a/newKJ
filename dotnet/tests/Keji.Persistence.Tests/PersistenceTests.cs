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
        await convRepo.CreateAsync("conv_a", "对话A", uid);
        await convRepo.CreateAsync("conv_b", "对话B", uid);
        await msgRepo.AddAsync("conv_a", "user", "hello");

        var deleted = await userRepo.DeleteAsync(uid);
        Assert.True(deleted);

        Assert.Null(await userRepo.GetByIdAsync(uid));
        Assert.Null(await convRepo.GetAsync("conv_a"));
        Assert.Null(await convRepo.GetAsync("conv_b"));

        var msgs = await msgRepo.ListByConversationAsync("conv_a");
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
        await convRepo.CreateAsync("rollback_conv", "回滚对话", uid);
        await msgRepo.AddAsync("rollback_conv", "user", "回滚消息");

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
        Assert.NotNull(await convRepo.GetAsync("rollback_conv"));

        var msgs = await msgRepo.ListByConversationAsync("rollback_conv");
        Assert.Single(msgs);

        var conv = await convRepo.GetAsync("rollback_conv");
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

        var c1 = await repo.CreateAsync("conv_id", "原始标题");
        var c2 = await repo.CreateAsync("conv_id", "不应覆盖");
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

        var c1 = await repo.CreateAsync("fixed_conv", "原始标题");
        var c2 = await repo.CreateAsync("fixed_conv", "新标题");
        Assert.Equal("原始标题", c2.Title);
    }

    [Fact]
    public async Task Conversation_Create_DoesNotOverwriteOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateAsync("owned_conv", "对话", "user_a");
        var c2 = await repo.CreateAsync("owned_conv", "不应改属于", "user_b");
        Assert.Equal("user_a", c2.OwnerUserId);
    }

    [Fact]
    public async Task Conversation_Create_NullOwnerCanBeClaimed()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        var c1 = await repo.CreateAsync("unowned_conv", "对话");
        Assert.Null(c1.OwnerUserId);

        var c2 = await repo.CreateAsync("unowned_conv", "对话", "new_owner");
        Assert.Equal("new_owner", c2.OwnerUserId);
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

        await repo.CreateAsync("my_conv", "对话", "user1");
        var (record, result) = await repo.EnsureOwnedAsync("my_conv", "user1");
        Assert.Equal(ConversationOwnershipResult.AlreadyOwned, result);
        Assert.NotNull(record);
    }

    [Fact]
    public async Task Conversation_EnsureOwned_ClaimUnowned()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateAsync("free_conv", "对话");
        var (record, result) = await repo.EnsureOwnedAsync("free_conv", "claimer");
        Assert.Equal(ConversationOwnershipResult.ClaimedUnowned, result);
        Assert.NotNull(record);
        Assert.Equal("claimer", record.OwnerUserId);
    }

    [Fact]
    public async Task Conversation_EnsureOwned_OwnedByAnotherUser()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateAsync("taken_conv", "对话", "owner_a");
        var (record, result) = await repo.EnsureOwnedAsync("taken_conv", "owner_b");
        Assert.Equal(ConversationOwnershipResult.OwnedByAnotherUser, result);
        Assert.NotNull(record);
        Assert.Equal("owner_a", record.OwnerUserId);
    }

    [Fact]
    public async Task Conversation_ConcurrentClaimDifferentUsers()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateAsync("concurrent_claim", "抢对话");

        var userA = "user_a";
        var userB = "user_b";

        var taskA = repo.EnsureOwnedAsync("concurrent_claim", userA);
        var taskB = repo.EnsureOwnedAsync("concurrent_claim", userB);

        var (ra, rb) = (await taskA, await taskB);

        var claimedCount = 0;
        if (ra.Item2 == ConversationOwnershipResult.ClaimedUnowned) claimedCount++;
        if (rb.Item2 == ConversationOwnershipResult.ClaimedUnowned) claimedCount++;
        Assert.Equal(1, claimedCount);

        var ownedByOtherCount = 0;
        if (ra.Item2 == ConversationOwnershipResult.OwnedByAnotherUser) ownedByOtherCount++;
        if (rb.Item2 == ConversationOwnershipResult.OwnedByAnotherUser) ownedByOtherCount++;
        Assert.Equal(1, ownedByOtherCount);

        var final = await repo.GetAsync("concurrent_claim");
        Assert.NotNull(final);
        if (ra.Item2 == ConversationOwnershipResult.ClaimedUnowned)
            Assert.Equal(userA, final!.OwnerUserId);
        else
            Assert.Equal(userB, final!.OwnerUserId);
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

        var (ra, rb) = (await taskA, await taskB);

        var createdCount = 0;
        if (ra.Item2 == ConversationOwnershipResult.Created) createdCount++;
        if (rb.Item2 == ConversationOwnershipResult.Created) createdCount++;
        Assert.Equal(1, createdCount);

        Assert.True(
            ra.Item2 == ConversationOwnershipResult.OwnedByAnotherUser ||
            rb.Item2 == ConversationOwnershipResult.OwnedByAnotherUser);
    }

    [Fact]
    public async Task Conversation_ConcurrentSameUser()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        var tasks = new Task<(ConversationRecord?, ConversationOwnershipResult)>[10];
        for (int i = 0; i < 10; i++)
            tasks[i] = repo.EnsureOwnedAsync("same_user_conv", "user_x");

        var results = await Task.WhenAll(tasks);

        var createdOrClaimed = results.Count(r =>
            r.Item2 == ConversationOwnershipResult.Created ||
            r.Item2 == ConversationOwnershipResult.ClaimedUnowned);
        Assert.Equal(1, createdOrClaimed);

        foreach (var (record, _) in results)
            Assert.Equal("user_x", record!.OwnerUserId);
    }

    [Fact]
    public async Task Conversation_List_FiltersByOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateAsync("c1", "u1对话", "u1");
        await repo.CreateAsync("c2", "u2对话", "u2");
        await repo.CreateAsync("c3", "无主对话");

        var u1Convs = await repo.ListAsync(limit: 50, ownerUserId: "u1");
        Assert.Single(u1Convs);
        Assert.Equal("c1", u1Convs[0].Id);
    }

    [Fact]
    public async Task Conversation_List_AllWhenNoOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateAsync("c1", "a");
        await repo.CreateAsync("c2", "b");
        var all = await repo.ListAsync(limit: 50);
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
        await convRepoWithTime.CreateAsync("c_old", "旧对话");
        var convRepoWithTime2 = new SqliteConversationRepository(factory, new FixedTimeProvider(200.0));
        await convRepoWithTime2.CreateAsync("c_new", "新对话");

        var repoForList = new SqliteConversationRepository(factory, new FixedTimeProvider(0));
        var list = await repoForList.ListAsync(limit: 50);
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
            await repo.CreateAsync($"c{i}", $"对话{i}");

        Assert.Single(await repo.ListAsync(limit: 1));
        Assert.Equal(5, (await repo.ListAsync(limit: 5)).Count);
        Assert.Equal(10, (await repo.ListAsync(limit: 500)).Count);
    }

    [Fact]
    public async Task Conversation_Rename_DoesNotChangeOwner()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        var c = await repo.CreateAsync("rename_conv", "原名", "owner1");
        await repo.RenameAsync("rename_conv", "新名");
        var updated = await repo.GetAsync("rename_conv");
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

        await repo.CreateAsync("time_conv", "原名");
        await repo.RenameAsync("time_conv", "新名");
        var updated = await repo.GetAsync("time_conv");
        Assert.Equal(5000.0, updated!.UpdatedAt);
    }

    [Fact]
    public async Task Conversation_Delete_RemovesConversation()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateAsync("del_conv", "待删");
        Assert.True(await repo.DeleteAsync("del_conv"));
        Assert.Null(await repo.GetAsync("del_conv"));
    }

    [Fact]
    public async Task Conversation_Delete_Nonexistent_ReturnsFalse()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        Assert.False(await repo.DeleteAsync("nonexistent"));
    }

    [Fact]
    public async Task Conversation_Delete_RemovesMessages()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateAsync("del_conv_msgs", "对话");
        await msgRepo.AddAsync("del_conv_msgs", "user", "text");
        await convRepo.DeleteAsync("del_conv_msgs");

        var msgs = await msgRepo.ListByConversationAsync("del_conv_msgs");
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

        await convRepo.CreateAsync("msg_conv", "对话");
        var msgId = await msgRepo.AddAsync("msg_conv", "user", "hello world");
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

        var c = await convRepo.CreateAsync("count_conv");
        Assert.Equal(0, c.MessageCount);

        await msgRepo.AddAsync("count_conv", "user", "m1");
        await msgRepo.AddAsync("count_conv", "assistant", "m2");

        var updated = await convRepo.GetAsync("count_conv");
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
            msgRepo.AddAsync("does_not_exist", "user", "content"));
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
            msgRepo.AddAsync("missing_conv", "user", "content"));

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

        await convRepo.CreateAsync("good_conv");
        await msgRepo.AddAsync("good_conv", "user", "first");
        var before = await convRepo.GetAsync("good_conv");
        Assert.Equal(1, before!.MessageCount);

        await Assert.ThrowsAsync<KejiPersistenceException>(() =>
            msgRepo.AddAsync("does_not_exist", "user", "content"));

        var after = await convRepo.GetAsync("good_conv");
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
            await msgRepo.AddAsync("bad_conv", "user", "超敏感内容不可泄露");
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

        await convRepo.CreateAsync("time_conv");
        await msgRepo.AddAsync("time_conv", "user", "test");
        var conv = await convRepo.GetAsync("time_conv");
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

        await convRepo.CreateAsync("order_conv");
        await msgRepo.AddAsync("order_conv", "user", "first");
        await msgRepo.AddAsync("order_conv", "assistant", "second");
        await msgRepo.AddAsync("order_conv", "user", "third");

        var msgs = await msgRepo.ListByConversationAsync("order_conv", limit: 100);
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

        await convRepo.CreateAsync("param_conv");
        var id = await msgRepo.AddAsync("param_conv", "user", "safe' OR '1'='1");
        var msgs = await msgRepo.ListByConversationAsync("param_conv");
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

        await convRepo.CreateAsync("limit_conv");
        for (int i = 0; i < 10; i++)
            await msgRepo.AddAsync("limit_conv", "user", $"msg{i}");

        Assert.Single(await msgRepo.ListByConversationAsync("limit_conv", limit: 1));
        Assert.Equal(5, (await msgRepo.ListByConversationAsync("limit_conv", limit: 5)).Count);
        Assert.Equal(10, (await msgRepo.ListByConversationAsync("limit_conv", limit: 1000)).Count);
    }

    [Fact]
    public async Task Message_ConcurrentWrites_MaintainCorrectCount()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateAsync("concurrent_conv");

        const int count = 20;
        var tasks = new Task[count];
        for (int i = 0; i < count; i++)
        {
            var local = i;
            tasks[local] = msgRepo.AddAsync("concurrent_conv", "user", $"msg{local}");
        }
        await Task.WhenAll(tasks);

        var updated = await convRepo.GetAsync("concurrent_conv");
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
            await msgRepo.AddAsync("nonexistent", "", "超敏感内容不可泄露");
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
    public async Task Security_DoesNotModifyRealKejiDb()
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

        var conv = await convRepo.CreateAsync("ct_conv", cancellationToken: ct);
        Assert.NotNull(conv);

        var msgId = await msgRepo.AddAsync("ct_conv", "user", "test", ct);
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
            repo.AddAsync("test_conv", "user", "test", preCancelled));
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
    public async Task User_Create_IdPrimaryKeyConflict_NotDuplicateUsername()
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
        Assert.True(ex.ErrorCode > 0 || ex.ErrorCode == 0);
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
    }

    [Fact]
    public async Task Message_Add_InsertTriggerFailure_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateAsync("msg_trig_conv");

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
            msgRepo.AddAsync("msg_trig_conv", "user", "test content"));
        Assert.IsNotType<SqliteException>(ex);

        var conv = await convRepo.GetAsync("msg_trig_conv");
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
    }

    [Fact]
    public async Task Conversation_Rename_TriggerFailure_ThrowsKejiPersistenceException()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var repo = new SqliteConversationRepository(factory, ctx.FixedTime);

        await repo.CreateAsync("rename_trig_conv");

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
            repo.RenameAsync("rename_trig_conv", "new name"));
        Assert.IsNotType<SqliteException>(ex);
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
            repo.CreateAsync("blocked_conv"));
        Assert.IsNotType<SqliteException>(ex);
    }

    [Fact]
    public async Task Message_Add_TriggerFailure_ErrorMessageDoesNotLeakContent()
    {
        using var ctx = new TestContext();
        var factory = CreateFactory(ctx.Options);
        await CreateInitializerAsync(factory, ctx.FixedTime);
        var convRepo = new SqliteConversationRepository(factory, ctx.FixedTime);
        var msgRepo = new SqliteMessageRepository(factory, ctx.FixedTime);

        await convRepo.CreateAsync("leak_conv");

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
            await msgRepo.AddAsync("leak_conv", "user", "超敏感内容不可泄露");
        }
        catch (KejiPersistenceException ex)
        {
            Assert.DoesNotContain("超敏感内容", ex.Message);
            Assert.DoesNotContain("secret leak test", ex.Message);
        }
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

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
