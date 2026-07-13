# C# Migration Handoff

## Current state

- Branch: `rewrite/csharp-core`
- HEAD: `cdd7a5422e074eeac613638695d6b22e5528938b`
- Last accepted commit: `fix: close jwt lifetime and auth contract test gaps`
- Current task: TASK-006, done
- Current C# completion: 24%; TASK-006 is implemented and verified but not yet committed or accepted
- Commit/push authorization: waiting for the captain

## Completed in the working tree

- Restored the accepted TASK-005 Security 262 and Integration 48 tests exactly from the accepted baseline.
- Implemented the 25-value typed permission enum and exact admin/member/readonly 25/16/13 immutable matrices.
- Implemented default-deny HTTP authorization, typed metadata, exact safe responses, AND semantics, and conflict-to-safe-500 handling.
- Implemented immutable Legacy tool descriptors, fail-safe MCP filesystem classification, unknown-tool denial, dedicated tool authorization, and bounded role hints.
- Registered all authorization services and applied the required API middleware order.
- Added independent Security and Integration Authorization test suites, a test-only Probe, and production Controller metadata reflection coverage.
- Updated TASK-006 security, authorization, plan, state, and handoff documentation.
- Migrated the lasting authorization design details into the formal migration documents and removed the untracked tool-process design artifact.

## Verification

- Clean: success, 0 warnings, 0 errors
- Restore: success
- Build: success, 0 warnings, 0 errors
- `Keji.Security.Tests`: 503/503
- `Keji.Persistence.Tests`: 138/138
- `Keji.Integration.Tests`: 71/71
- `Keji.Agent.Tests`: 1/1
- `Keji.FileSystem.Tests`: 1/1
- `Keji.Tools.Tests`: 1/1
- Solution: 715/715, 0 skipped
- NuGet vulnerabilities: 0 across all 19 projects
- `git diff --check`: passed with empty stdout and stderr
- Temporary TASK-006 test directories: none remaining

## Key decisions

- Core permissions and attributes are enum-only; no string permission path exists.
- Invalid roles, missing metadata, unknown ordinary tools, and missing descriptors fail closed.
- Readonly Write denial takes priority over AdminOnly denial; non-write AdminOnly operations still return AdminRequired.
- Both custom and standard `IAllowAnonymous` metadata are honored.
- Legacy classification does not establish registration or executability; TASK-010 owns the authoritative frozen Registry.

## Forbidden changes confirmed

- Python unchanged.
- Web frontend unchanged.
- Persistence production code unchanged.
- Accepted TASK-005 tests unchanged.
- No real configuration, secret, SQLite, user data, or temporary test artifact added.
- No commit, push, history rewrite, or TASK-007 work performed.

## Next action

Wait for the captain to authorize the TASK-006 commit. After authorization: run the final diff/status check, create the ordinary main commit `feat: implement default-deny authorization foundation`, push normally, verify local/remote SHA equality, update state and handoff, and only then begin TASK-007.
