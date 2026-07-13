# C# Migration Handoff

## Current state

- Branch: `rewrite/csharp-core`
- Fix parent: `2bbdce42494d1e0a7eaaa8bd2905785bc4b6941f`
- Last accepted commit: `fix: close jwt lifetime and auth contract test gaps`
- Current task: TASK-006 authorization edge-case closure
- Current C# completion: 24%; TASK-006 accepted
- 当前任务：TASK-006
- 当前状态：accepted
- 正式完成度：24%
- TASK-006最终验收基线：
  96eed064ef2bbee3f0b04e6727b0b49dbfdd4144
- 下一任务：TASK-007
- TASK-007状态：未开始
- TASK-006主提交：
  2bbdce42494d1e0a7eaaa8bd2905785bc4b6941f
- TASK-006边界修复提交：
  f6a2dc201d5fb4dafdf04988375751b051743f46
- 当前状态：`ready_for_final_review`

## Completed in the TASK-006 repair set

- Restored the accepted TASK-005 Security 262 and Integration 48 tests exactly from the accepted baseline.
- Implemented the 25-value typed permission enum and exact admin/member/readonly 25/16/13 immutable matrices.
- Implemented default-deny HTTP authorization, typed metadata, exact safe responses, AND semantics, and conflict-to-safe-500 handling.
- Implemented immutable Legacy tool descriptors, fail-safe MCP filesystem classification, unknown-tool denial, dedicated tool authorization, and bounded role hints.
- Registered all authorization services and applied the required API middleware order.
- Added independent Security and Integration Authorization test suites, a test-only Probe, and production Controller metadata reflection coverage.
- Updated TASK-006 security, authorization, plan, state, and handoff documentation.
- Migrated the lasting authorization design details into the formal migration documents and removed the untracked tool-process design artifact.
- Closed AdminOnly-over-readonly-write precedence, centralized role helpers, protected started responses, and declared OpenAPI `SystemRead` metadata.
- Added independent `KejiAuthorizationMiddlewareTests.cs` and the expanded three-role/API-key/forwarded-header/OpenAPI integration matrix.

## Verification

- Clean: success, 0 warnings, 0 errors
- Restore: success
- Build: success, 0 warnings, 0 errors
- `Keji.Security.Tests`: 541/541
- `Keji.Persistence.Tests`: 138/138
- `Keji.Integration.Tests`: 91/91
- `Keji.Agent.Tests`: 1/1
- `Keji.FileSystem.Tests`: 1/1
- `Keji.Tools.Tests`: 1/1
- Solution: 773/773, 0 skipped
- NuGet vulnerabilities: 0 across all 19 projects
- `git diff --check`: passed with empty stdout and stderr
- Temporary TASK-006 test directories: none remaining

## Key decisions

- Core permissions and attributes are enum-only; no string permission path exists.
- Invalid roles, missing metadata, unknown ordinary tools, and missing descriptors fail closed.
- AdminOnly拒绝优先于readonly-write拒绝。
- Both custom and standard `IAllowAnonymous` metadata are honored.
- Legacy classification does not establish registration or executability; TASK-010 owns the authoritative frozen Registry.

## Forbidden changes confirmed

- Python unchanged.
- Web frontend unchanged.
- Persistence production code unchanged.
- Accepted TASK-005 tests unchanged.
- No real configuration, secret, SQLite, user data, or temporary test artifact added.
- No history rewrite or TASK-007 work performed.

## Next action

等待队长最终远程验收；通过后才允许进入TASK-007。
