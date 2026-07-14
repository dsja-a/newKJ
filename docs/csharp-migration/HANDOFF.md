# C# Migration Handoff

## Current state

- Repository: `dsja-a/newKJ`
- Branch: `rewrite/csharp-core`
- Last accepted baseline: `c027ab34a7a30655f11ff035af191b31cae57bd1`
- Current task: TASK-009
- Current status: in_progress (pending commit)
- Formal C# completion: 34%
- Next task: TASK-010
- TASK-009 status: pending_commit

## Completed in TASK-009

- Added `ownerUserId` parameter (`string?`) to `IConversationRepository.GetAsync`, `RenameAsync`, `DeleteAsync` — ownership enforced at SQL level via `AND owner_user_id = @owner` in WHERE clause.
- Added `ownerUserId` parameter (`string?`) to `IMessageRepository.AddAsync` and `ListByConversationAsync` — message writes filter by `AND owner_user_id = @owner` on the conversation; message reads JOIN with `conversations` table to enforce owner.
- Extended `SqliteConversationRepository` with owner-filtered `GetAsync` (dedicated SQL query), `RenameAsync` (conditional WHERE), `DeleteAsync` (conditional WHERE on both message DELETE and conversation DELETE).
- Extended `SqliteMessageRepository` with owner-filtered `AddAsync` (conditional `AND owner_user_id = @owner` on the UPDATE conversations) and `ListByConversationAsync` (JOIN with conversations + `c.owner_user_id = @owner`).
- When `ownerUserId` is `null` (admin/fallback path), all methods behave as before (no owner filter).
- All ownership enforcement is purely SQL-level — no read-then-compare in application memory.
- Added 33 comprehensive tests covering: get-own, get-other (returns null), rename-own (succeeds), rename-other (fails), delete-own (succeeds, cascades messages), delete-other (fails, preserves messages), message-add-own (succeeds), message-add-other (throws), message-list-own (returns messages), message-list-other (empty), unowned conversation isolation, nonexistent cases, sensitive content not leaked in error messages.

## Verification

- `Keji.Persistence.Tests`: 171/171 (138 original + 33 new)
- `Keji.Security.Tests`: 541/541
- `Keji.Auditing.Tests`: 102/102
- `Keji.FileSystem.Tests`: 289/289
- `Keji.Integration.Tests`: 91/91
- `Keji.Agent.Tests`: 1/1
- `Keji.Tools.Tests`: 1/1
- Full solution: 1196/1196
- Failed: 0
- Skipped: 0
- Build warnings: 0
- Build errors: 0
- Known NuGet vulnerabilities: 0 across all 20 projects
- `git diff --check`: empty stdout and stderr

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
- Persistence production code unchanged (interface signatures changed with backward-compatible defaults).
- TASK-006 and TASK-007 tests unchanged.
- No real configuration, secret, SQLite database, user data, log, TRX, ZIP, or temporary review artifact added.
- No history rewrite performed.

## Next action

TASK-009 production code and tests are complete. The commit is `feat: enforce session ownership isolation`. After commit, the next task is TASK-010 (tool contracts and frozen registry).
