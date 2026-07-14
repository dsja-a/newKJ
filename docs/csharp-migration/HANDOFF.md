# C# Migration Handoff

## Current state

- Repository: `dsja-a/newKJ`
- Branch: `rewrite/csharp-core`
- Last accepted baseline: `33c849a7eb1e9116b0fb0bb08a18c582714a4605`
- Current task: TASK-007
- Current status: done
- Formal C# completion: 28%
- Next task: TASK-008
- TASK-008 status: not started

TASK-007's formal commit SHA is intentionally recorded when TASK-008 begins; no self-referencing SHA placeholder is written into the TASK-007 commit.

## Completed in TASK-007

- Implemented the Windows-local shared and per-user workspace candidate model with strict lexical validation, Unicode normalization, exact containment, and no P1 filesystem I/O.
- Added true-path inspection from the configured workspace root through every existing component using no-follow Windows handles, final-path verification, file identity, reparse-point rejection, and hard-link rejection.
- Added default-deny authorization based only on authenticated `ICurrentUserAccessor` identity, with the complete admin/member/readonly shared, own, and other-user matrix.
- Added a bounded workspace execution interface for existence, text and byte reads, existing-file writes, one-component missing-file creation, one-level directory creation, direct-child enumeration, and single-file deletion.
- Bound reads, existing-file writes, deletion, and enumeration to verified target handles. Missing files and directories are created relative to a pinned parent handle and the created object is verified before use.
- Added dependency-injection registration with singleton immutable path services and scoped identity-sensitive policy and filesystem services.
- Removed the empty production and test placeholder classes.

## Verification

- `Keji.Security.Tests`: 541/541
- `Keji.Persistence.Tests`: 138/138
- `Keji.Integration.Tests`: 91/91
- `Keji.Agent.Tests`: 1/1
- `Keji.FileSystem.Tests`: 289/289
- `Keji.Tools.Tests`: 1/1
- Full solution: 1061/1061
- Failed: 0
- Skipped: 0
- Build warnings: 0
- Build errors: 0
- Known NuGet vulnerabilities: 0 across all 19 projects
- `git diff --check`: empty stdout and stderr

## Security decisions and limits

- Candidate resolution never implies authorization or tool executability.
- User IDs and actor IDs are exactly 16 lowercase hexadecimal characters and are never trimmed.
- Unknown identities, roles, scopes, operations, candidates, and inspection states fail closed.
- Reparse points, junctions, symbolic links, mount points, and regular-file hard links are denied.
- Public operation results do not expose absolute filesystem paths or native exception details.
- Recursive enumeration and recursive deletion are not implemented.
- Directory enumeration is handle-bound but not an atomic snapshot. Existing-file in-place writes are not transactional; callers must independently reauthorize every later operation.

## Forbidden changes confirmed

- Python unchanged.
- Web frontend unchanged.
- Persistence production code unchanged.
- TASK-006 tests unchanged.
- No real configuration, secret, SQLite database, user data, log, TRX, ZIP, or temporary review artifact added.
- No history rewrite performed.

## Next action

Create the single TASK-007 commit with exact message `feat: implement workspace path sandbox`, push normally, verify the local and remote SHA, then record that SHA and enter TASK-008.
