# Session & Conversation Ownership Isolation

## Design

TASK-009 enforces **SQL-level ownership isolation** for conversations and messages. Every repository method that reads or mutates a conversation (or its messages) accepts an optional `ownerUserId` parameter. When provided, the owner filter is embedded **directly in the SQL WHERE clause** — never read-then-compared in application memory.

## Key Concepts

| Python (Baseline) | C# (Migration) |
|---|---|
| `session_id` = nanobot session key (`user:{uid}:{conv_id}` for auth'd users) | Not migrated — simplified to `Conversation` concept |
| `conversation_id` = bare ID | `ConversationRecord.Id` |
| `owner_user_id` in conversations table | `ConversationRecord.OwnerUserId` |
| `ensure_conversation_owned()` | `IConversationRepository.EnsureOwnedAsync()` |
| `list_conversations(owner_user_id=user.id)` | `IConversationRepository.ListAsync(ownerUserId: ...)` |
| `_assert_conv_access()` (in routes.py) | Not migrated — handled at API level via `KejiRequirePermission` |
| Admin bypass: `if user.is_admin: return` | Admin bypass: pass `ownerUserId: null` (no filter) |

## Changed Interfaces

### `IConversationRepository`

```csharp
Task<ConversationRecord?> GetAsync(string convId, string? ownerUserId = null, CancellationToken cancellationToken = default);
Task<List<ConversationRecord>> ListAsync(int limit = 50, string? ownerUserId = null, CancellationToken cancellationToken = default);
Task<bool> RenameAsync(string convId, string title, string? ownerUserId = null, CancellationToken cancellationToken = default);
Task<bool> DeleteAsync(string convId, string? ownerUserId = null, CancellationToken cancellationToken = default);
```

- When `ownerUserId` is **not null**: SQL includes `AND owner_user_id = @owner`
- When `ownerUserId` is **null**: no owner filter (admin/fallback path)

### `IMessageRepository`

```csharp
Task<long> AddAsync(string conversationId, string role, string content, string? ownerUserId = null, CancellationToken cancellationToken = default);
Task<List<MessageRecord>> ListByConversationAsync(string conversationId, int limit = 100, string? ownerUserId = null, CancellationToken cancellationToken = default);
```

- `AddAsync` with owner: `UPDATE conversations ... WHERE id = @cid AND owner_user_id = @owner` — fails (throws) if not owner
- `ListByConversationAsync` with owner: `SELECT ... FROM messages m JOIN conversations c ON c.id = m.conversation_id WHERE m.conversation_id = @cid AND c.owner_user_id = @owner`

## SQL Query Patterns

### Get (with owner)
```sql
SELECT id, title, created_at, updated_at, message_count, owner_user_id
FROM conversations WHERE id = @id AND owner_user_id = @owner
```

### Rename (with owner)
```sql
UPDATE conversations SET title = @title, updated_at = @t
WHERE id = @id AND owner_user_id = @owner
```

### Delete (with owner)
```sql
-- Step 1: delete messages of owned conversation
DELETE FROM messages WHERE id IN (
  SELECT m.id FROM messages m
  JOIN conversations c ON c.id = m.conversation_id
  WHERE m.conversation_id = @id AND c.owner_user_id = @owner
);
-- Step 2: delete conversation (only if owned)
DELETE FROM conversations WHERE id = @id AND owner_user_id = @owner;
```

### Add message (with owner)
```sql
UPDATE conversations SET updated_at = @now, message_count = message_count + 1
WHERE id = @cid AND owner_user_id = @owner
```

### List messages (with owner)
```sql
SELECT m.id, m.conversation_id, m.role, m.content, m.created_at
FROM messages m
JOIN conversations c ON c.id = m.conversation_id
WHERE m.conversation_id = @cid AND c.owner_user_id = @owner
ORDER BY m.created_at ASC, m.id ASC LIMIT @lim
```

## Behavior Matrix

| Scenario | `ownerUserId` = null | `ownerUserId` = "user_a" (correct) | `ownerUserId` = "user_b" (wrong) |
|---|---|---|---|
| GetAsync | Returns conversation | Returns conversation | Returns null |
| RenameAsync | Succeeds (true) | Succeeds (true) | Fails (false) |
| DeleteAsync | Succeeds (true, cascades) | Succeeds (true, cascades) | Fails (false, preserved) |
| AddAsync (message) | Succeeds | Succeeds | Throws KejiPersistenceException |
| ListByConversationAsync | Returns all messages | Returns messages | Returns empty list |

## Unowned Conversations

Conversations with `owner_user_id IS NULL` are **not accessible** when an owner filter is applied. A user must first claim the conversation via `EnsureOwnedAsync` (which atomically sets `owner_user_id` from NULL to the caller's ID) before they can read, rename, delete, or add messages with an owner filter.

## Test Coverage

33 new tests in `Keji.Persistence.Tests.PersistenceTests`:

- **Get**: 5 tests (without owner, with owner own, with owner other, nonexistent, unowned)
- **Rename**: 6 tests (without owner, with owner own, with owner other, unowned, nonexistent, unowned via null)
- **Delete**: 7 tests (without owner, with owner own, with owner other, unowned, nonexistent, own messages cascaded, other messages preserved)
- **Message Add**: 6 tests (without owner, with owner own, with owner other throws, unowned throws, missing throws, no residual on failure)
- **Message List**: 5 tests (without owner, with owner own, with owner other empty, unowned empty, nonexistent empty)
- **Conversation List**: 3 tests (with owner filters others, excludes unowned, without owner includes all)
- **EnsureOwned isolation**: 1 test (ownership preserved after ensure)
- **Sensitive content**: 1 test (no leak in error message)

Total: **171** persistence tests (138 original + 33 new), 0 failures.
