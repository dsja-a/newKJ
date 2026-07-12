using Microsoft.Data.Sqlite;

namespace Keji.Persistence;

public class KejiDatabaseInitializer : IKejiDatabaseInitializer
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IUnixTimeProvider _timeProvider;

    public KejiDatabaseInitializer(
        ISqliteConnectionFactory connectionFactory,
        IUnixTimeProvider timeProvider)
    {
        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        SqliteConnection? conn = null;
        Microsoft.Data.Sqlite.SqliteTransaction? tx = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            conn = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            tx = (Microsoft.Data.Sqlite.SqliteTransaction)await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version TEXT PRIMARY KEY,
                    applied_at REAL NOT NULL
                );
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS conversations (
                    id TEXT PRIMARY KEY,
                    title TEXT DEFAULT '新对话',
                    created_at REAL NOT NULL,
                    updated_at REAL NOT NULL,
                    message_count INTEGER DEFAULT 0
                );
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS messages (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    conversation_id TEXT NOT NULL,
                    role TEXT NOT NULL,
                    content TEXT NOT NULL,
                    created_at REAL NOT NULL,
                    FOREIGN KEY (conversation_id) REFERENCES conversations(id) ON DELETE CASCADE
                );
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS documents (
                    id TEXT PRIMARY KEY,
                    file_path TEXT UNIQUE NOT NULL,
                    file_name TEXT NOT NULL,
                    file_type TEXT NOT NULL,
                    file_size INTEGER DEFAULT 0,
                    doc_category TEXT DEFAULT 'other',
                    content_hash TEXT,
                    chunk_count INTEGER DEFAULT 0,
                    indexed_at REAL,
                    created_at REAL NOT NULL,
                    last_accessed_at REAL
                );
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS settings (
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL,
                    updated_at REAL NOT NULL
                );
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS database_configs (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    db_type TEXT NOT NULL CHECK(db_type IN ('mysql','postgresql')),
                    host TEXT NOT NULL,
                    port INTEGER NOT NULL,
                    database_name TEXT NOT NULL,
                    username TEXT NOT NULL,
                    password_encrypted TEXT NOT NULL DEFAULT '',
                    created_at REAL NOT NULL,
                    updated_at REAL NOT NULL
                );
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS table_metadata (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    config_id INTEGER NOT NULL,
                    table_name TEXT NOT NULL,
                    columns_json TEXT NOT NULL DEFAULT '[]',
                    primary_keys_json TEXT NOT NULL DEFAULT '[]',
                    foreign_keys_json TEXT NOT NULL DEFAULT '[]',
                    row_count INTEGER DEFAULT 0,
                    table_comment TEXT DEFAULT '',
                    qa_enabled INTEGER DEFAULT 1,
                    business_context TEXT DEFAULT '',
                    sample_data TEXT DEFAULT '[]',
                    created_at REAL NOT NULL,
                    updated_at REAL NOT NULL,
                    FOREIGN KEY (config_id) REFERENCES database_configs(id) ON DELETE CASCADE,
                    UNIQUE(config_id, table_name)
                );
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS tool_usage_log (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    session_id TEXT NOT NULL,
                    turn_id TEXT NOT NULL DEFAULT '',
                    tool_name TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'ok',
                    duration_ms INTEGER NOT NULL DEFAULT 0,
                    prompt_tokens INTEGER NOT NULL DEFAULT 0,
                    completion_tokens INTEGER NOT NULL DEFAULT 0,
                    cached_tokens INTEGER NOT NULL DEFAULT 0,
                    estimated_cost REAL NOT NULL DEFAULT 0.0,
                    model TEXT NOT NULL DEFAULT '',
                    created_at REAL NOT NULL
                );
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS audit_events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    event_type TEXT NOT NULL,
                    actor TEXT NOT NULL DEFAULT 'api',
                    session_id TEXT NOT NULL DEFAULT '',
                    tool_name TEXT NOT NULL DEFAULT '',
                    path TEXT NOT NULL DEFAULT '',
                    action TEXT NOT NULL DEFAULT '',
                    status TEXT NOT NULL DEFAULT 'ok',
                    detail TEXT NOT NULL DEFAULT '',
                    client_ip TEXT NOT NULL DEFAULT '',
                    created_at REAL NOT NULL
                );
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS users (
                    id TEXT PRIMARY KEY,
                    username TEXT NOT NULL UNIQUE,
                    password_hash TEXT NOT NULL,
                    display_name TEXT NOT NULL DEFAULT '',
                    role TEXT NOT NULL DEFAULT 'member' CHECK(role IN ('admin','member','readonly')),
                    is_active INTEGER NOT NULL DEFAULT 1,
                    created_at REAL NOT NULL,
                    last_login_at REAL
                );
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            cmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_messages_conv ON messages(conversation_id, created_at);
                CREATE INDEX IF NOT EXISTS idx_documents_path ON documents(file_path);
                CREATE INDEX IF NOT EXISTS idx_documents_type ON documents(file_type);
                CREATE INDEX IF NOT EXISTS idx_tool_usage_session ON tool_usage_log(session_id, created_at);
                CREATE INDEX IF NOT EXISTS idx_tool_usage_name ON tool_usage_log(tool_name);
                CREATE INDEX IF NOT EXISTS idx_audit_created ON audit_events(created_at);
                CREATE INDEX IF NOT EXISTS idx_audit_type ON audit_events(event_type, created_at);
                CREATE INDEX IF NOT EXISTS idx_audit_path ON audit_events(path);
                CREATE INDEX IF NOT EXISTS idx_users_username ON users(username);
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            cmd.CommandText = """
                SELECT COUNT(*) FROM pragma_table_info('conversations') WHERE name = 'owner_user_id'
                """;
            var hasOwnerCol = (long)(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))! > 0;

            if (!hasOwnerCol)
            {
                cmd.CommandText = "ALTER TABLE conversations ADD COLUMN owner_user_id TEXT";
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            cmd.CommandText = "CREATE INDEX IF NOT EXISTS idx_conv_owner ON conversations(owner_user_id, updated_at)";
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            cmd.CommandText = "INSERT OR IGNORE INTO schema_migrations (version, applied_at) VALUES (@v, @t)";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@v", "001_add_owner_user_id");
            cmd.Parameters.AddWithValue("@t", _timeProvider.Now);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await SqliteExceptionTranslator.SafeRollbackAsync(tx).ConfigureAwait(false);
            throw;
        }
        catch (KejiPersistenceException)
        {
            await SqliteExceptionTranslator.SafeRollbackAsync(tx).ConfigureAwait(false);
            throw;
        }
        catch (SqliteException ex)
        {
            await SqliteExceptionTranslator.SafeRollbackAsync(tx).ConfigureAwait(false);
            throw SqliteExceptionTranslator.Create(ex, "Initialize");
        }
        catch
        {
            await SqliteExceptionTranslator.SafeRollbackAsync(tx).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (tx is not null)
                await tx.DisposeAsync().ConfigureAwait(false);
            conn?.Dispose();
        }
    }
}
