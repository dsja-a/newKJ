# Persistence Layer (TASK-004)

## Overview

SQLite persistence infrastructure for Keji C# migration. Schema-compatible with Python, with intentionally strengthened unsafe behaviors.

## Python Current SQLite

- **Path**: `data/keji.db` relative to project root
- **Connection**: `sqlite3.connect()` with default journal mode (usually delete), no WAL, no FK enforcement, no busy_timeout
- **Time**: Unix epoch seconds via `time.time()` stored as REAL
- **No connection pooling**
- **Per-operation connections**: each function opens/closes its own connection

## Python All 10 Tables

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
| `schema_migrations` | Database schema migration tracking |

## C# Schema Compatibility

All 10 Python tables are created with matching schemas. Additional columns/extensions:
- `conversations.owner_user_id` — Python added this later via migration; C# unconditionally creates it.
- `idx_conv_owner` — always created on `conversations(owner_user_id, updated_at)`.

## schema_migrations

```sql
CREATE TABLE IF NOT EXISTS schema_migrations (
    version TEXT PRIMARY KEY,
    applied_at REAL NOT NULL
);
```

Version `001_add_owner_user_id` records the owner_user_id migration.

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

If any failure (e.g. trigger abort), the entire transaction is rolled back. The exception is wrapped in `KejiPersistenceException`.

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
  IF rows == 0 → ROLLBACK → throw KejiPersistenceException (safe)
  INSERT INTO messages (conversation_id, role, content, created_at)
  SELECT last_insert_rowid()
COMMIT
EXCEPTION → ROLLBACK → throw KejiPersistenceException (wraps original)
```

## Security: Password Hash

Password hash is stored as an opaque string. It is not hashed, verified, or generated by the persistence layer — this is handled by `Keji.Security`.

`ListAsync()` returns `UserSummaryRecord` which explicitly excludes the `PasswordHash` field.

## Security: Exception Messages

- Settings values never appear in exception messages.
- Message content never appears in exception messages.
- Password hash values never appear in exception messages.
- `DuplicateUsernameException` includes the username but not the password hash.

## Security Differences from Python

| Behavior | Python | C# |
|----------|--------|----|
| Conversation ownership | Missing conv = belongs to caller | Explicit `ConversationOwnershipResult` enum |
| Concurrent ownership claim | Race condition (read-then-write) | Atomic transaction |
| User delete | No transaction | Transactional (messages + conversations + user) |
| Message count | May be inconsistent | Atomic increment |
| Database exceptions | Raw SQLite exceptions visible to caller | Wrapped in `KejiPersistenceException` |
| Sensitive values in errors | May leak | Stripped from messages |
| FK enforcement | Off | On by default |
| WAL journal mode | Off (default delete) | On by default |
| Busy timeout | None | 5000ms by default |

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

No file I/O occurs during registration or service resolution. Database file is created only when `IKejiDatabaseInitializer.InitializeAsync()` is called.
