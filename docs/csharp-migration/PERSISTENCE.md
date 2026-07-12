# Persistence Layer (TASK-004)

## Overview

SQLite persistence infrastructure for Keji C# migration. Schema-compatible with Python 9 business tables, with intentionally strengthened unsafe behaviors.

## Python Current SQLite

- **Path**: `data/keji.db` relative to project root
- **Connection**: `sqlite3.connect()` at `Database.__init__()`, stored per-thread via `threading.local()`. Same connection reused for all operations on the same thread — NOT per-operation connections.
- **Journal**: `PRAGMA journal_mode=WAL` set once in `_get_conn()` on each new thread connection.
- **FK enforcement**: `PRAGMA foreign_keys=ON` set once in `_get_conn()`.
- **busy_timeout**: No explicit `PRAGMA busy_timeout` anywhere. Python's `sqlite3.connect()` call does not pass a `timeout` argument, so the default `timeout=5.0` applies — Python `sqlite3` waits up to 5.0 seconds for a lock before raising `sqlite3.OperationalError` (SQLITE_BUSY). This is a Python-level default, not a SQLite `busy_timeout` PRAGMA.
- **Time**: Unix epoch seconds via `time.time()` stored as REAL.
- **No connection pooling** — each thread keeps its own persistent connection via `threading.local`.
- **9 business tables**: `conversations`, `messages`, `documents`, `settings`, `database_configs`, `table_metadata`, `tool_usage_log`, `audit_events`, `users`. No `schema_migrations` table in Python.
- **DB directory**: Created at `__init__` via `os.makedirs(os.path.dirname(db_path), exist_ok=True)`, not deferred.
- **Init**: `_init_tables()` runs all CREATE TABLE IF NOT EXISTS + CREATE INDEX + migrate_schema inside `Database.__init__()`, committed via `conn.commit()` (no explicit transaction).
- **No explicit busy_timeout PRAGMA** in Python. Python `sqlite3.connect()` default timeout is 5.0 seconds.

## Python 9 Business Tables (no schema_migrations)

| Table | Purpose |
|-------|---------|
| `conversations` | Chat session records |
| `messages` | Individual messages in conversations |
| `users` | User accounts |
| `settings` | Key-value configuration store |
| `documents` | Uploaded/indexed documents |
| `database_configs` | External DB connection configs |
| `table_metadata` | Metadata for external DB tables |
| `tool_usage_log` | LLM tool usage audit log |
| `audit_events` | General audit events |

## C# Schema Compatibility

All 9 Python tables are created with matching schemas. C# additionally creates a `schema_migrations` table (10 total) for migration tracking. Additional columns/extensions:
- `conversations.owner_user_id` — Python added via `_migrate_schema()`; C# unconditionally creates it.
- `idx_conv_owner` — always created on `conversations(owner_user_id, updated_at)`.

## schema_migrations

```sql
CREATE TABLE IF NOT EXISTS schema_migrations (
    version TEXT PRIMARY KEY,
    applied_at REAL NOT NULL
);
```

Version `001_add_owner_user_id` records the owner_user_id migration. C# adds this table; Python does not have it.

## owner_user_id Migration

Flow during `InitializeAsync()`:
1. Check `pragma_table_info('conversations')` for `owner_user_id` column.
2. If missing → `ALTER TABLE conversations ADD COLUMN owner_user_id TEXT`.
3. Always create index: `CREATE INDEX IF NOT EXISTS idx_conv_owner ON conversations(owner_user_id, updated_at)`.
4. Always record: `INSERT OR IGNORE INTO schema_migrations (version, applied_at) VALUES ('001_add_owner_user_id', @time)`.

This is idempotent: safe to call multiple times.

## WAL

Enabled via `PRAGMA journal_mode = WAL` on first connection. Protected by `SemaphoreSlim` for concurrent safety — `_walEnsured` flag with double-check locking. WAL failure does not incorrectly mark as ensured.

## foreign_keys

`PRAGMA foreign_keys = ON` set on every connection open. Controlled by `KejiPersistenceOptions.EnableForeignKeys` (default `true`).

## busy_timeout

`PRAGMA busy_timeout = <ms>` set on every connection open. Default `5000ms`. Configurable via `BusyTimeoutMilliseconds`.

## CreateDirectoryIfMissing

Controlled by `KejiPersistenceOptions.CreateDirectoryIfMissing` (default `true`).

In `SqliteConnectionFactory.OpenConnectionAsync()`:
1. Resolve absolute database path.
2. Get parent directory.
3. If directory does not exist:
   - `true` → `Directory.CreateDirectory(dir)` — creates all missing parents.
   - `false` → throw `KejiPersistenceException`.
4. Then open `SqliteConnection`.

Directory creation NEVER happens during constructor or DI registration.

## DB Creation Timing

- DI registration (`AddKejiPersistenceFoundation()`) does NOT create directory or database file.
- Service resolution (`sp.GetRequiredService<>()`) does NOT create directory or database file.
- First `OpenConnectionAsync()` MAY create the database file due to `ReadWriteCreate` mode.
- Normal business use requires explicit `IKejiDatabaseInitializer.InitializeAsync()` call.
- Uninitialized repositories may get a safe `KejiPersistenceException` from SQLite errors.

## REAL Unix Seconds

All timestamps stored as REAL (SQLite floating point), matching Python `time.time()`. Precision: millisecond-level via `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0`.

## IUnixTimeProvider

Abstract time interface:
```csharp
public interface IUnixTimeProvider { double Now { get; } }
```

- `UnixTimeProvider` — real implementation using `DateTimeOffset.UtcNow`.
- `FixedTimeProvider` — test-only, returns a pre-configured value.

Every timestamp in repositories (`created_at`, `updated_at`, `last_login_at`, `applied_at`) uses `_timeProvider.Now`. No caller-provided timestamp parameters.

## Connection Management

- **Every repository operation creates its own connection** via `ISqliteConnectionFactory`.
- **No shared `SqliteConnection`** across operations.
- Connection pooling is enabled (`Pooling=true`) in the connection string.
- Connections are created at `ReadWriteCreate` mode.

## Repository Methods

### IUserRepository

| Method | Description |
|--------|-------------|
| `CountAsync()` | Total user count |
| `GetByUsernameAsync(username)` | Full record (includes `PasswordHash`) |
| `GetByIdAsync(userId)` | Full record |
| `ListAsync()` | Summary records (no `PasswordHash`) |
| `CreateAsync(username, passwordHash, role, displayName)` | Returns 16-char hex ID |
| `UpdateAsync(userId, command)` | Whitelist fields: `display_name`, `role`, `is_active`, `password_hash` |
| `TouchLoginAsync(userId)` | Sets `last_login_at` to `_timeProvider.Now` |
| `DeleteAsync(userId)` | Transactional: delete messages → conversations → user |

### IConversationRepository

| Method | Description |
|--------|-------------|
| `CreateAsync(convId, title, ownerUserId)` | `INSERT OR IGNORE` — idempotent |
| `EnsureOwnedAsync(convId, ownerUserId, title)` | Atomic transaction (see below) |
| `GetAsync(convId)` | Single record |
| `ListAsync(limit, ownerUserId)` | Ordered by `updated_at DESC`, filtered by owner |
| `RenameAsync(convId, title)` | Also sets `updated_at` to `_timeProvider.Now` |
| `DeleteAsync(convId)` | Transactional: delete messages → conversation |

### IMessageRepository

| Method | Description |
|--------|-------------|
| `AddAsync(conversationId, role, content)` | Transactional: update conversation → insert message |
| `ListByConversationAsync(conversationId, limit)` | Ordered by `created_at ASC, id ASC` |

### ISettingsRepository

| Method | Description |
|--------|-------------|
| `GetAsync(key, defaultValue)` | Returns value or default |
| `SetAsync(key, value)` | `INSERT ... ON CONFLICT(key) DO UPDATE` |
| `GetAllAsync()` | All settings as dictionary |

## SQL Injection Protection

All business values are passed as parameterized SQL. No string concatenation for values.

## Fixed Whitelist UPDATE Fields

`SqliteUserRepository.UpdateAsync` only updates fields with non-null values from `UpdateUserCommand`. Whitelist: `display_name`, `password_hash`, `is_active`, `role`. Empty command checks existence without modifying.

## Role Normalization

Roles are normalized via `SqliteExceptionTranslator.NormalizeRole(string?)`:
- Trims whitespace.
- Converts to lowercase invariant.
- Rejects null, empty, or whitespace.
- Only allows `"admin"`, `"member"`, `"readonly"`.
- Throws `KejiPersistenceException` for invalid values.
- Used by both `CreateAsync` and `UpdateAsync` before SQL execution.

## Transaction Boundaries

### DeleteUser
```
BEGIN TRANSACTION
  SELECT conversations WHERE owner_user_id = @id
  FOR EACH conversation:
    DELETE FROM messages WHERE conversation_id = @cid
    DELETE FROM conversations WHERE id = @cid
  DELETE FROM users WHERE id = @id
  IF rows == 0 → ROLLBACK → return false
COMMIT
```

If any failure (e.g. trigger abort), the entire transaction is rolled back. The exception is wrapped in `KejiPersistenceException` without retaining the raw `SqliteException` as a public `InnerException`.

### DeleteConversation
```
BEGIN TRANSACTION
  DELETE FROM messages WHERE conversation_id = @id
  DELETE FROM conversations WHERE id = @id
  IF rows == 0 → ROLLBACK → return false
COMMIT
```

### EnsureOwned (Atomic)
```
BEGIN TRANSACTION
  INSERT OR IGNORE INTO conversations (...) VALUES (@id, @title, @now, @now, @owner)
  IF inserted > 0 → SELECT record → COMMIT → return (Created)
  UPDATE conversations SET owner_user_id = @owner WHERE id = @id AND owner_user_id IS NULL
  IF updated > 0 → SELECT record → COMMIT → return (ClaimedUnowned)
  SELECT final record → COMMIT
  IF owner == caller → AlreadyOwned ELSE OwnedByAnotherUser
EXCEPTION → ROLLBACK → rethrow
```

### AddMessage
```
BEGIN TRANSACTION
  UPDATE conversations SET updated_at = @now, message_count + 1 WHERE id = @cid
  IF rows == 0 → ROLLBACK → throw KejiPersistenceException (safe, conv not found)
  INSERT INTO messages (conversation_id, role, content, created_at)
  SELECT last_insert_rowid()
COMMIT
EXCEPTION (OperationCanceledException) → ROLLBACK (CancellationToken.None) → rethrow
EXCEPTION (KejiPersistenceException) → ROLLBACK (CancellationToken.None) → rethrow
EXCEPTION (SqliteException) → ROLLBACK (CancellationToken.None) → throw Create(ex, "AddMessage", convId)
EXCEPTION (other) → ROLLBACK (CancellationToken.None) → throw KejiPersistenceException("Database operation 'AddMessage' failed.")
```

## Exception Handling

### `SqliteExceptionTranslator`

Static helper class for safe SQLite exception translation:

```csharp
public static KejiPersistenceException Create(SqliteException exception, string operationName, string? safeEntityId = null)
```

- Returns a new `KejiPersistenceException` using `new KejiPersistenceException(message, exception.SqliteErrorCode)`.
- `ErrorCode` equals the original `SqliteErrorCode`.
- Uses `SqliteExtendedErrorCode == 2067` (SQLITE_CONSTRAINT_UNIQUE) to detect `DuplicateUsernameException` for username UNIQUE constraint violations.
- Does NOT convert primary key ID conflicts into `DuplicateUsernameException`.
- `operationName` is a fixed code constant, not user input.
- `safeEntityId` only accepts safe entity IDs (not message content, setting values, titles, or password hashes).
- Does NOT retain the raw `SqliteException` as a public `InnerException`.
- Does NOT leak SQL text, password hashes, setting values, message content, database password, or raw SQLite error description text in `Message` or `ToString()`.
- `ThrowTranslated` convenience method: `throw Create(ex, operationName, safeEntityId)`.

Static helper for safe rollback:

```csharp
public static async Task SafeRollbackAsync(SqliteTransaction? transaction)
```

- No-op if `transaction` is null.
- Uses `CancellationToken.None` — rollback must complete even if the original CancellationToken is cancelled.
- Silently swallows rollback exceptions to avoid masking the original exception.

### `DuplicateUsernameException`

- Only thrown for SQLITE_CONSTRAINT_UNIQUE (extended error code 2067) on the `users.username` column.
- Not thrown for primary key ID conflicts or any other constraint violation.
- Contains the username but NOT the password hash.

### `OperationCanceledException`

- Propagated unmodified through all repository methods.
- NOT converted into `KejiPersistenceException` or any other exception type.
- Transaction rollback occurs before rethrow when within a transaction scope.

### Exception Handling Rules

All public persistence methods follow these exception rules:

1. **OperationCanceledException** propagates unmodified — not converted to any other type.
2. **DuplicateUsernameException** propagates unmodified — thrown only for username UNIQUE constraint violations.
3. **KejiPersistenceException** propagates unmodified — business exceptions like invalid role or conversation not found.
4. **SqliteException** is translated via `SqliteExceptionTranslator.Create()` into `KejiPersistenceException` with:
   - `ErrorCode` set to the original `SqliteErrorCode`.
   - No public `InnerException`.
   - Safe message without SQL text, sensitive values, or raw SQLite error text.
5. All translation occurs inside exception boundaries — every public method wraps its SQL execution in try/catch.
6. `SafeRollbackAsync` uses `CancellationToken.None` to ensure rollback completes even if the original operation was cancelled.
7. `finally` blocks dispose transactions (SqliteTransaction) and connections (SqliteConnection) to prevent resource leaks.

### Methods with complete exception boundaries:

| Repository | Methods |
|------------|---------|
| SqliteUserRepository | CountAsync, GetByUsernameAsync, GetByIdAsync, ListAsync, CreateAsync, UpdateAsync, TouchLoginAsync, DeleteAsync |
| SqliteConversationRepository | CreateAsync, EnsureOwnedAsync, GetAsync, ListAsync, RenameAsync, DeleteAsync |
| SqliteMessageRepository | AddAsync, ListByConversationAsync |
| SqliteSettingsRepository | GetAsync, SetAsync, GetAllAsync |
| KejiDatabaseInitializer | InitializeAsync |
| SqliteConnectionFactory | OpenConnectionAsync |

**No public persistence method exposes a raw SqliteException.** All boundaries avoid:
- Not-found semantics for non-existent records (only the AddMessage UPDATE-rows==0 case returns "not found").
- Disguising a DB failure as entity-not-found.

### Exception Message Security

- Settings values never appear in exception messages.
- Message content never appears in exception messages.
- Password hash values never appear in exception messages.
- `DuplicateUsernameException` includes the username but not the password hash.
- Raw SQLite exception text is not exposed in `ToString()`.

## Security: Password Hash

Password hash is stored as an opaque string. It is not hashed, verified, or generated by the persistence layer — this is handled by `Keji.Security`.

`ListAsync()` returns `UserSummaryRecord` which explicitly excludes the `PasswordHash` field.

## Security Differences from Python

| Behavior | Python | C# |
|----------|--------|----|
| Connection lifecycle | `threading.local` per-thread persistent connection | Per-operation new connection (pooled) |
| WAL journal mode | Set on each new thread connection | Set on first connection with double-check locking |
| FK enforcement | ON | ON |
| Busy timeout | No explicit PRAGMA; Python sqlite3.connect() default timeout=5.0s | 5000ms by default (PRAGMA busy_timeout) |
| Conversation ownership | Missing conv = belongs to caller | Explicit `ConversationOwnershipResult` enum |
| Concurrent ownership claim | Race condition (read-then-write) | Atomic transaction |
| User delete | Per-operation delete (no explicit tx) | Transactional (messages + conversations + user) |
| Message count | May be inconsistent | Atomic increment |
| Database exceptions | Raw SQLite exceptions visible to caller | Wrapped in `KejiPersistenceException` with no public InnerException |
| Sensitive values in errors | May leak | Stripped from messages |
| Directory creation | At `__init__` (synchronous) | Deferred to first `OpenConnectionAsync` or `InitializeAsync` |
| schema_migrations | Not present | Created as 10th table |

## Things NOT Implemented

- Password hashing (handled by TASK-005 / Keji.Security)
- JWT tokens (TASK-005)
- Login / authentication middleware (TASK-005)
- API controllers (TASK-005)

## TASK-005 Usage of IUserRepository

TASK-005 will:
- Use `IUserRepository.CreateAsync()` to register new users.
- Use `IUserRepository.GetByUsernameAsync()` for login verification.
- Use `IUserRepository.GetByIdAsync()` for session lookups.
- Use `IUserRepository.TouchLoginAsync()` to record login timestamps.
- Use `IUserRepository.UpdateAsync()` for profile updates.
- Use `IUserRepository.ListAsync()` for admin user management.
- Hash passwords via `Keji.Security` before passing to `CreateAsync()` / `UpdateAsync()`.

## Dependency Injection

`AddKejiPersistenceFoundation()` registers:
- `KejiPersistenceOptions` (Singleton)
- `IUnixTimeProvider` → `UnixTimeProvider` (Singleton)
- `ISqliteConnectionFactory` → `SqliteConnectionFactory` (Singleton, factory-based)
- `IKejiDatabaseInitializer` → `KejiDatabaseInitializer` (Singleton)
- `IUserRepository` → `SqliteUserRepository` (Singleton)
- `IConversationRepository` → `SqliteConversationRepository` (Singleton)
- `IMessageRepository` → `SqliteMessageRepository` (Singleton, factory-based)
- `ISettingsRepository` → `SqliteSettingsRepository` (Singleton, factory-based)

No file I/O occurs during registration or service resolution. Database file is created only when `IKejiDatabaseInitializer.InitializeAsync()` is called (or on first `OpenConnectionAsync` due to `ReadWriteCreate` mode).
