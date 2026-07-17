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

            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('audit_events') WHERE name = 'event_id'";
            cmd.Parameters.Clear();
            var hasEventIdCol = (long)(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))! > 0;

            if (!hasEventIdCol)
            {
                cmd.CommandText = "ALTER TABLE audit_events ADD COLUMN event_id TEXT";
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                cmd.CommandText = "UPDATE audit_events SET event_id = printf('%04x%04x-%04x-%04x-%04x-%04x%04x%04x', random(), random(), random(), random(), random(), random(), random(), random()) WHERE event_id IS NULL";
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            cmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS idx_audit_event_id ON audit_events(event_id)";
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            cmd.CommandText = "INSERT OR IGNORE INTO schema_migrations (version, applied_at) VALUES (@v, @t)";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@v", "001_add_owner_user_id");
            cmd.Parameters.AddWithValue("@t", _timeProvider.Now);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            cmd.CommandText = "INSERT OR IGNORE INTO schema_migrations (version, applied_at) VALUES (@v2, @t2)";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@v2", "002_add_audit_event_id");
            cmd.Parameters.AddWithValue("@t2", _timeProvider.Now);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            cmd.Parameters.Clear();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS smart_query_data_sources (
                    id TEXT NOT NULL,
                    owner_user_id TEXT NOT NULL,
                    display_name TEXT NOT NULL DEFAULT '',
                    dialect INTEGER NOT NULL CHECK(dialect IN (1,2)),
                    host TEXT NOT NULL,
                    port INTEGER NOT NULL CHECK(port BETWEEN 1 AND 65535),
                    database_name TEXT NOT NULL,
                    username TEXT NOT NULL,
                    password_secret_reference TEXT NOT NULL,
                    tls_mode INTEGER NOT NULL CHECK(tls_mode BETWEEN 1 AND 3),
                    visibility INTEGER NOT NULL CHECK(visibility IN (1,2)),
                    enabled INTEGER NOT NULL CHECK(enabled IN (0,1)),
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    PRIMARY KEY(id, owner_user_id)
                );
                CREATE TABLE IF NOT EXISTS smart_query_allowed_schemas (
                    data_source_id TEXT NOT NULL,
                    owner_user_id TEXT NOT NULL,
                    schema_name TEXT NOT NULL,
                    ordinal INTEGER NOT NULL CHECK(ordinal >= 0),
                    PRIMARY KEY(data_source_id, owner_user_id, schema_name),
                    UNIQUE(data_source_id, owner_user_id, ordinal),
                    FOREIGN KEY(data_source_id, owner_user_id)
                      REFERENCES smart_query_data_sources(id, owner_user_id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS smart_query_tables (
                    data_source_id TEXT NOT NULL,
                    owner_user_id TEXT NOT NULL,
                    schema_name TEXT NOT NULL,
                    table_name TEXT NOT NULL,
                    display_name TEXT NOT NULL DEFAULT '',
                    description TEXT NOT NULL DEFAULT '',
                    business_context TEXT NOT NULL DEFAULT '',
                    qa_enabled INTEGER NOT NULL CHECK(qa_enabled IN (0,1)),
                    query_enabled INTEGER NOT NULL CHECK(query_enabled IN (0,1)),
                    estimated_row_count INTEGER NOT NULL DEFAULT 0 CHECK(estimated_row_count >= 0),
                    ordinal INTEGER NOT NULL CHECK(ordinal >= 0),
                    PRIMARY KEY(data_source_id, owner_user_id, schema_name, table_name),
                    UNIQUE(data_source_id, owner_user_id, ordinal),
                    FOREIGN KEY(data_source_id, owner_user_id)
                      REFERENCES smart_query_data_sources(id, owner_user_id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS smart_query_columns (
                    data_source_id TEXT NOT NULL,
                    owner_user_id TEXT NOT NULL,
                    schema_name TEXT NOT NULL,
                    table_name TEXT NOT NULL,
                    column_name TEXT NOT NULL,
                    data_type INTEGER NOT NULL CHECK(data_type BETWEEN 1 AND 8),
                    nullable INTEGER NOT NULL CHECK(nullable IN (0,1)),
                    description TEXT NOT NULL DEFAULT '',
                    query_enabled INTEGER NOT NULL CHECK(query_enabled IN (0,1)),
                    sensitive INTEGER NOT NULL CHECK(sensitive IN (0,1)),
                    ordinal INTEGER NOT NULL CHECK(ordinal >= 0),
                    PRIMARY KEY(data_source_id, owner_user_id, schema_name, table_name, column_name),
                    UNIQUE(data_source_id, owner_user_id, schema_name, table_name, ordinal),
                    FOREIGN KEY(data_source_id, owner_user_id, schema_name, table_name)
                      REFERENCES smart_query_tables(data_source_id, owner_user_id, schema_name, table_name)
                      ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS smart_query_foreign_keys (
                    data_source_id TEXT NOT NULL,
                    owner_user_id TEXT NOT NULL,
                    name TEXT NOT NULL,
                    principal_schema TEXT NOT NULL,
                    principal_table TEXT NOT NULL,
                    principal_column TEXT NOT NULL,
                    dependent_schema TEXT NOT NULL,
                    dependent_table TEXT NOT NULL,
                    dependent_column TEXT NOT NULL,
                    query_enabled INTEGER NOT NULL CHECK(query_enabled IN (0,1)),
                    ordinal INTEGER NOT NULL CHECK(ordinal >= 0),
                    PRIMARY KEY(data_source_id, owner_user_id, name),
                    UNIQUE(data_source_id, owner_user_id, ordinal),
                    FOREIGN KEY(data_source_id, owner_user_id, principal_schema, principal_table, principal_column)
                      REFERENCES smart_query_columns(data_source_id, owner_user_id, schema_name, table_name, column_name),
                    FOREIGN KEY(data_source_id, owner_user_id, dependent_schema, dependent_table, dependent_column)
                      REFERENCES smart_query_columns(data_source_id, owner_user_id, schema_name, table_name, column_name),
                    FOREIGN KEY(data_source_id, owner_user_id)
                      REFERENCES smart_query_data_sources(id, owner_user_id) ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS idx_smart_query_sources_access
                  ON smart_query_data_sources(id, enabled, visibility, owner_user_id);
                CREATE INDEX IF NOT EXISTS idx_smart_query_tables_source
                  ON smart_query_tables(data_source_id, owner_user_id, ordinal);
                CREATE INDEX IF NOT EXISTS idx_smart_query_columns_table
                  ON smart_query_columns(data_source_id, owner_user_id, schema_name, table_name, ordinal);
                CREATE INDEX IF NOT EXISTS idx_smart_query_fk_source
                  ON smart_query_foreign_keys(data_source_id, owner_user_id, ordinal);
                """;
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            cmd.CommandText = "INSERT OR IGNORE INTO schema_migrations (version, applied_at) VALUES (@v3, @t3)";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@v3", "003_smart_query_normalized_catalog");
            cmd.Parameters.AddWithValue("@t3", _timeProvider.Now);
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
