# Session & Conversation Ownership Isolation

## Design

TASK-009 enforces **SQL-level ownership isolation** for conversations and messages. Every repository method requires a mandatory `ownerUserId` parameter (non-nullable `string`). The owner filter is embedded **directly in the SQL WHERE clause** — never read-then-compared in application memory. No fallback, no admin bypass, no null-owner path exists.

## Key Concepts

| Concept | Implementation |
|---|---|
| `ConversationRecord.Id` | Conversation identifier |
| `ConversationRecord.OwnerUserId` | Owning user (16-char lowercase hex) |
| `ConversationOwnershipResult.Created` | New conversation created |
| `ConversationOwnershipResult.AlreadyOwned` | Same-owner idempotent hit |
| `ConversationNotFoundException` | Cross-owner or nonexistent — same exception type and message |
| `UserIdValidator.RequireValid()` | `^[0-9a-f]{16}$` enforced at persistence entry |
| `ICurrentUserAccessor.CurrentUser.Id` | Sole source of owner in service layer |

## Repository Interfaces

### `IConversationRepository`

```csharp
Task<ConversationRecord> CreateOwnedAsync(string convId, string ownerUserId, string title = "新对话", CancellationToken ct = default);
Task<(ConversationRecord Record, ConversationOwnershipResult Result)> EnsureOwnedAsync(string convId, string ownerUserId, string title = "新对话", CancellationToken ct = default);
Task<ConversationRecord?> GetOwnedAsync(string convId, string ownerUserId, CancellationToken ct = default);
Task<List<ConversationRecord>> ListOwnedAsync(string ownerUserId, int limit = 50, CancellationToken ct = default);
Task<bool> RenameOwnedAsync(string convId, string ownerUserId, string title, CancellationToken ct = default);
Task<bool> DeleteOwnedAsync(string convId, string ownerUserId, CancellationToken ct = default);
Task<int> CountByOwnerAsync(string ownerUserId, CancellationToken ct = default);
```

- `ownerUserId` is mandatory `string` — never null, never optional.
- Every method calls `UserIdValidator.RequireValid(ownerUserId)` at entry.
- No `GetInternalNoOwnerAsync` exists.

### `IMessageRepository`

```csharp
Task<long> AddOwnedAsync(string conversationId, string ownerUserId, string role, string content, CancellationToken ct = default);
Task<List<MessageRecord>> ListOwnedMessagesAsync(string conversationId, string ownerUserId, int limit = 100, CancellationToken ct = default);
```

- `ownerUserId` is mandatory `string` — never null, never optional.
- `AddOwnedAsync` validates conversation ownership in a transaction via `UPDATE conversations SET ... WHERE id = @cid AND owner_user_id = @owner`.
- `ListOwnedMessagesAsync` joins `messages m` with `conversations c` on `c.id = m.conversation_id` and filters by `c.owner_user_id = @owner`.

## Service Interface

### `IKejiConversationService`

```csharp
Task<ConversationRecord> CreateConversationAsync(string convId, string title = "新对话", CancellationToken ct = default);
Task<(ConversationRecord Record, ConversationOwnershipResult Result)> EnsureConversationAsync(string convId, string title = "新对话", CancellationToken ct = default);
Task<ConversationRecord?> GetConversationAsync(string convId, CancellationToken ct = default);
Task<List<ConversationRecord>> ListConversationsAsync(int limit = 50, CancellationToken ct = default);
Task<bool> RenameConversationAsync(string convId, string title, CancellationToken ct = default);
Task<bool> DeleteConversationAsync(string convId, CancellationToken ct = default);
Task<int> CountConversationsAsync(CancellationToken ct = default);
Task<long> AddMessageAsync(string conversationId, string role, string content, CancellationToken ct = default);
Task<List<MessageRecord>> ListMessagesAsync(string conversationId, int limit = 100, CancellationToken ct = default);
```

- No method accepts `ownerUserId` — owner is read from `ICurrentUserAccessor.CurrentUser.Id`.
- Registered as `Scoped` in DI.
- All roles (admin, member, readonly) use the same service — no role-based bypass in ordinary endpoints.

## User ID Format

- Pattern: `^[0-9a-f]{16}$`
- Exactly 16 characters, lowercase hexadecimal.
- Valid: `aaaaaaaaaaaaaaaa`, `bbbbbbbbbbbbbbbb`, `cccccccccccccccc`.
- Rejected: null, empty, 15 or 17 characters, uppercase hex (`AAAAAAAAAAAAAAAA`), non-hex (`gggggggggggggggg`), whitespace, control characters.

## SQL Query Patterns

### CreateOwnedAsync (transaction)
```sql
INSERT OR IGNORE INTO conversations (id, title, created_at, updated_at, owner_user_id)
VALUES (@id, @title, @now, @now, @owner);

-- If inserted=0 (row exists):
SELECT id, title, created_at, updated_at, message_count, owner_user_id
FROM conversations WHERE id = @id AND owner_user_id = @owner;
-- Found → return existing (idempotent). Not found → throw ConversationNotFoundException.
```

### GetOwnedAsync
```sql
SELECT id, title, created_at, updated_at, message_count, owner_user_id
FROM conversations WHERE id = @id AND owner_user_id = @owner;
```

### RenameOwnedAsync
```sql
UPDATE conversations SET title = @title, updated_at = @t
WHERE id = @id AND owner_user_id = @owner;
```

### DeleteOwnedAsync
```sql
DELETE FROM messages WHERE id IN (
  SELECT m.id FROM messages m
  JOIN conversations c ON c.id = m.conversation_id
  WHERE m.conversation_id = @id AND c.owner_user_id = @owner
);
DELETE FROM conversations WHERE id = @id AND owner_user_id = @owner;
```

### AddOwnedAsync
```sql
UPDATE conversations SET updated_at = @now, message_count = message_count + 1
WHERE id = @cid AND owner_user_id = @owner;
-- If rows=0 → throw KejiPersistenceException
INSERT INTO messages (conversation_id, role, content, created_at)
VALUES (@cid, @role, @content, @now);
```

### ListOwnedMessagesAsync
```sql
SELECT m.id, m.conversation_id, m.role, m.content, m.created_at
FROM messages m
JOIN conversations c ON c.id = m.conversation_id
WHERE m.conversation_id = @cid AND c.owner_user_id = @owner
ORDER BY m.created_at ASC, m.id ASC LIMIT @lim;
```

## Behavior Matrix

### CreateOwnedAsync
| Scenario | Result |
|---|---|
| New conversation | Returns created record |
| Same owner, existing ID | Returns existing record (idempotent) |
| Different owner, existing ID | Throws `ConversationNotFoundException` |
| NULL-old record, existing ID | Throws `ConversationNotFoundException` |

### EnsureOwnedAsync
| Scenario | Result |
|---|---|
| New conversation | `(record, Created)` |
| Same owner, existing ID | `(record, AlreadyOwned)` |
| Different owner, existing ID | Throws `ConversationNotFoundException` |
| NULL-old record, existing ID | Throws `ConversationNotFoundException` |

### GetOwnedAsync / RenameOwnedAsync / DeleteOwnedAsync
| Scenario | Result |
|---|---|
| Owned by caller | Record / true / true |
| Owned by other user | null / false / false |
| Nonexistent | null / false / false |
| NULL-old record | null / false / false |

### AddOwnedAsync
| Scenario | Result |
|---|---|
| Owned by caller | Returns message ID |
| Owned by other user | Throws `KejiPersistenceException` |
| Nonexistent conversation | Throws `KejiPersistenceException` |
| NULL-old record | Throws `KejiPersistenceException` |

### ListOwnedMessagesAsync
| Scenario | Result |
|---|---|
| Owned by caller | Returns messages |
| Owned by other user | Returns empty list |
| Nonexistent conversation | Returns empty list |
| NULL-old record | Returns empty list |

## NULL-Old Conversations

Conversations with `owner_user_id IS NULL` in the database:
- Are **never returned** by any owned Get/List/Rename/Delete method.
- Are **never mutated** by any owned mutation method.
- Cannot be **claimed** — no migration or null-set path exists.
- Are effectively invisible to all owned operations.

## Audit Behavior

1. **Success audit**: `conversation_create`, `conversation_ensure`, `conversation_delete` with `Outcome=Success`.
2. **Denied audit** (cross-owner or nonexistent get/rename/delete): `DataAccess/conversation_access/Denied`, `targetType=conversation`, `targetId=null`. No conversation ID, owner ID, title, or message count is recorded.
3. **AlreadyOwned audit**: `conversation_ensure/Success` (not Denied).
4. **Cross-owner Create/Ensure**: catches `ConversationNotFoundException`, audits `DataAccess/conversation_access/Denied` with `targetId=null`, then rethrows.
5. **Audit failure isolation**: all exceptions except `OperationCanceledException` are caught. Logged with fixed security codes (`AUDIT_PARTIAL_SINK_FAILURE`, `AUDIT_SINK_FAILED`, `AUDIT_VALIDATION_FAILED`, `AUDIT_UNEXPECTED_FAILURE`) and the action name only. No raw exception, exception message, or stack trace is passed to `ILogger`. Audit failure never changes the business result.

## Test Coverage

### Persistence tests: 171/171

- 138 original tests (preserved and passing).
- 33 TASK-009 ownership tests covering: CreateOwned, EnsureOwned, GetOwned, RenameOwned, DeleteOwned, AddOwned, ListOwned, concurrent access, cross-owner isolation, NULL-old isolation, sensitive content leak prevention.

### Integration tests (TASK-009 additions): 148 total (57 new)

- **Architecture**: interface contract (no ownerUserId in service), repository interface (no unowned overloads), DI scope, current user source.
- **User ID validation**: null, empty, 15-char, 17-char, uppercase hex, whitespace, non-hex, control chars → rejected; valid 16-char hex → passes.
- **Audit isolation**: audit service throwing normal exceptions → Create/Get/Delete still return correct results; logger records fixed code `AUDIT_UNEXPECTED_FAILURE`; logger does not contain exception message or stack trace; `OperationCanceledException` propagates; `PartialFailure`/`SinkError`/`ValidationError` do not change results.
- **Cross-owner**: User B Create/Ensure with User A's conversation ID → throws `ConversationNotFoundException` + `Denied` audit with `targetId=null`; same-owner idempotent → `Success` audit.
- **Cross-owner Get/Rename/Delete/Messages**: User B cannot access User A's conversations; results identical to nonexistent.
- **NULL-old**: inaccessible via all operations.
- **Integration end-to-end**: full create/read/rename/delete/add-message/list-message cycles with multiple users; admin/member/readonly all isolated; cancellation token propagation.

### Full solution: 1253/1253 — 0 failed, 0 skipped, 0 warnings, 0 errors, 0 vulnerabilities.
