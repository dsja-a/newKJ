# C# Migration Handoff

## Current state

- Repository: `dsja-a/newKJ`
- Branch: `rewrite/csharp-core`
- Last accepted baseline: `8d507c7ec1f0f7a81e79470b22a421d767fa4cef`
- Current task: TASK-009
- Current status: accepted
- Formal C# completion: 36%
- Next task: TASK-010
- TASK-010: not_started

## Completed in TASK-009

### Repository layer

1. `IConversationRepository` — all methods are owned-only: `CreateOwnedAsync`, `EnsureOwnedAsync`, `GetOwnedAsync`, `ListOwnedAsync`, `RenameOwnedAsync`, `DeleteOwnedAsync`, `CountByOwnerAsync`. Every method requires `ownerUserId` as a mandatory `string` parameter (never null, never optional).
2. `IMessageRepository` — all methods are owned-only: `AddOwnedAsync`, `ListOwnedMessagesAsync`. Every method requires `ownerUserId` as a mandatory `string`.
3. `GetInternalNoOwnerAsync` — deleted. No fallback, no admin bypass, no null-owner path exists.
4. `UserIdValidator.RequireValid` — enforces `^[0-9a-f]{16}$` at persistence method entry. Rejects null, empty, wrong length, non-hex, uppercase, and whitespace-padded values.
5. `CreateOwnedAsync` — uses `INSERT OR IGNORE` inside a transaction. On insert (new conversation) returns the record. On no-insert checks `SELECT ... WHERE id = @id AND owner_user_id = @owner`. Same owner returns existing record (idempotent). Other owner or NULL-old record throws `ConversationNotFoundException`.
6. `EnsureOwnedAsync` — same transaction pattern. Returns `(record, Created)` on insert, `(record, AlreadyOwned)` on same-owner hit. Other owner or NULL-old record throws `ConversationNotFoundException`. No no-owner query is ever executed.
7. `GetOwnedAsync`, `RenameOwnedAsync`, `DeleteOwnedAsync` — all use `WHERE id = @id AND owner_user_id = @owner` in SQL. Cross-owner and nonexistent both return null/false — no distinguishing information leaked.
8. `ListOwnedAsync`, `CountByOwnerAsync` — filter exclusively by `owner_user_id`.
9. `AddOwnedAsync` — validates conversation ownership via `UPDATE conversations SET ... WHERE id = @cid AND owner_user_id = @owner` inside a transaction. Throws `KejiPersistenceException` if not owned or nonexistent.
10. `ListOwnedMessagesAsync` — joins `messages` with `conversations` on `conversation_id` and filters by `c.owner_user_id = @owner`. Returns empty list for cross-owner or nonexistent.

### Service layer (`IKejiConversationService` / `KejiConversationService`)

1. No method accepts an `ownerUserId` parameter — owner is always read from `ICurrentUserAccessor.CurrentUser.Id`.
2. `RequireUserId()` rejects null (unauthenticated) and invalid-format user IDs with `KejiPersistenceException`.
3. All roles (admin, member, readonly) use the same service — no role-based bypass. Admin calls from ordinary endpoints are isolated to the admin's own conversations.
4. `CreateConversationAsync` / `EnsureConversationAsync` — catch `ConversationNotFoundException` from cross-owner collision, audit `DataAccess/conversation_access/Denied` with `targetId=null`, then rethrow. AlreadyOwned (same-owner idempotent) audits `conversation_ensure/Success`.
5. `GetConversationAsync` — null result audits `Denied`.
6. `DeleteConversationAsync` — success audits `conversation_delete/Success`, failure audits `Denied`.
7. `RenameConversationAsync` — false result audits `Denied`.
8. `AuditDataAccessAsync` — safely catches all exceptions except `OperationCanceledException`. Logs fixed security codes (`AUDIT_PARTIAL_SINK_FAILURE`, `AUDIT_SINK_FAILED`, `AUDIT_VALIDATION_FAILED`, `AUDIT_UNEXPECTED_FAILURE`) with only the action parameter. Never passes raw `Exception` objects, exception messages, or stack traces to `ILogger`. Audit failures never change the business result of the calling operation.

### Cross-owner and NULL-old behavior

1. Cross-owner `Create` or `Ensure` throws `ConversationNotFoundException` — identical exception type and message to a nonexistent conversation. No conversation ID, owner ID, title, or message count is leaked.
2. Cross-owner `Get` returns `null` (same as nonexistent).
3. Cross-owner `Rename` / `Delete` returns `false` (same as nonexistent).
4. Cross-owner `AddMessage` throws `KejiPersistenceException` (same as nonexistent).
5. Cross-owner `ListMessages` returns empty (same as nonexistent).
6. NULL-old conversations (`owner_user_id IS NULL` in database) are never returned, mutated, or claimed by any owned method. No migration path exists.

### User ID format

- Exact pattern: `^[0-9a-f]{16}$` (16 lowercase hexadecimal characters).
- Valid examples: `aaaaaaaaaaaaaaaa`, `bbbbbbbbbbbbbbbb`, `cccccccccccccccc`.
- Rejected: null, empty, 15 or 17 characters, uppercase hex, non-hex characters, whitespace, control characters.

## Verification

| Project | Tests | Status |
|---|---|---|
| `Keji.Persistence.Tests` | 171/171 | passed |
| `Keji.Security.Tests` | 541/541 | passed |
| `Keji.Integration.Tests` | 148/148 | passed (57 new: service architecture, audit isolation, cross-owner, integration) |
| `Keji.Auditing.Tests` | 102/102 | passed |
| `Keji.FileSystem.Tests` | 289/289 | passed |
| `Keji.Agent.Tests` | 1/1 | passed |
| `Keji.Tools.Tests` | 1/1 | passed |
| **Full solution** | **1253/1253** | **passed** |
| Failed | 0 | |
| Skipped | 0 | |
| Build warnings | 0 | |
| Build errors | 0 | |
| NuGet vulnerabilities | 0 | |
| `git diff --check` | empty | |

## Security decisions and limits (unchanged from TASK-008)

- Candidate resolution never implies authorization or tool executability.
- User IDs and actor IDs are exactly 16 lowercase hexadecimal characters and are never trimmed.
- Unknown identities, roles, scopes, operations, candidates, and inspection states fail closed.
- Reparse points, junctions, symbolic links, mount points, and regular-file hard links are denied.
- Public operation results do not expose absolute filesystem paths or native exception details.
- Recursive enumeration and recursive deletion are not implemented.
- Directory enumeration is handle-bound but not an atomic snapshot. Existing-file in-place writes are not transactional; callers must independently reauthorize every later operation.
- Audit category, severity, and outcome enums reject default/zero as undefined; serialization enforces message length and ID format limits.
- Audit sinks are resolved from DI and run within the service scope; a failing sink does not block other sinks.

## Forbidden changes confirmed

- Python unchanged.
- Web frontend unchanged.
- No null/optional ownerUserId exists in repository or service interfaces.
- No admin bypass path exists in persistence or service layer.
- No NULL-old conversation claim path exists.
- No real configuration, secret, SQLite database, user data, log, TRX, ZIP, or temporary review artifact added.
- No history rewrite performed.

## Next action

TASK-009 production code, tests, and documentation are accepted. The three functional commits are:
- `1a113ba` — `feat: enforce session ownership isolation`
- `7dca227` — `fix: close task 009 ownership bypasses`
- `b4bb697` — `fix: prevent task 009 ownership collision leaks`

The acceptance commits are:
- `8d507c7` — `test: close task 009 service isolation gaps`
- `fd849bb` — `docs: record task 009 final acceptance`

The next task is TASK-010 (tool contracts and frozen registry). TASK-010 has not started.
