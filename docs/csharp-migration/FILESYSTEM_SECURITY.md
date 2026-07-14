# TASK-007 Windows Workspace Sandbox Security Boundary

## Boundary and layout

The workspace sandbox owns only these Windows-local layouts:

```text
<WorkspaceRoot>\shared
<WorkspaceRoot>\users\<userId>
```

`WorkspaceRoot` must be a local drive-qualified absolute path. It does not have to exist when options are created, and option construction never creates it. UNC paths and device paths are rejected. A user ID is exactly 16 lowercase hexadecimal characters and is never trimmed. Shared scope requires a strictly null target user ID.

`KejiWorkspacePathCandidateResult` is only a lexical candidate. It does not mean that a user is authorized, that a read or write is permitted, that a tool is registered, or that a tool may execute. Absolute candidate paths and allowed-decision candidates are internal implementation details and are not part of the public result contract.

## Lexical rules

An empty relative path is allowed and addresses the selected scope root. Null and whitespace-only paths are distinct rejections. Normalization uses Unicode Form C and safely rejects unmatched surrogate code units.

The resolver rejects rooted paths, drive-qualified paths, UNC and device paths, current-directory and traversal segments, empty segments, repeated or trailing separators, alternate data streams, control and invalid Windows characters, trailing dots or spaces, reserved Windows device names, and configured path or segment length violations.

Containment is case-insensitive and accepts only either an exact root match or a path beginning with the root plus one directory separator. A drive root such as `C:\` is handled without producing a duplicate separator.

P1 candidate resolution performs no file or directory I/O.

## Authorization matrix

Identity comes from `ICurrentUserAccessor`; callers cannot supply or override the actor identity in a workspace request.

| Role | Shared | Own user workspace | Other user workspace |
|---|---|---|---|
| admin | Read, Write, Create, Delete, Enumerate | Read, Write, Create, Delete, Enumerate | Read, Write, Create, Delete, Enumerate |
| member | Read, Write, Create, Delete, Enumerate | Read, Write, Create, Delete, Enumerate | denied |
| readonly | Read, Enumerate | Read, Enumerate | denied |

Missing users, unknown roles, malformed actor IDs, invalid operations, invalid scopes, malformed target IDs, and rejected candidates fail closed with strong failure enums. Readonly write, create, and delete operations are denied before filesystem inspection.

## Windows path inspection

The Windows inspector starts at the configured `WorkspaceRoot` and opens every existing component through `CreateFileW` with `FILE_FLAG_OPEN_REPARSE_POINT`. It checks attributes, volume serial number, file identity, link count, and the final handle path.

The workspace root, scope root, parents, and target are all checked. Any reparse point is denied, including symbolic links, junctions, and mount points. Existing non-directory parents are denied. Regular files with more than one hard link are denied. When a target is missing, the lease pins the nearest existing parent and records the number of missing segments.

Final handle paths must remain inside both the true workspace root and the true selected scope root. Non-Windows execution fails closed with `PlatformNotSupported`. Access, I/O, Win32, and path failures are mapped to safe enums and do not expose system paths or exception details.

## Execution boundary and TOCTOU controls

`IKejiWorkspaceFileSystem` is the only execution-facing workspace API. It supports:

- existence checks;
- bounded UTF-8 text and byte reads;
- bounded text and byte writes to existing regular files, or creation of one
  missing file component beneath an existing pinned parent;
- creation of one missing directory component;
- bounded enumeration of direct child names;
- deletion of a single regular file.

It does not expose arbitrary absolute-path operations, recursive enumeration, or recursive deletion.

Inspection returns a disposable handle lease. Existing directory components are pinned against rename or reparse replacement. Reads and existing-file writes use the verified target handle through `RandomAccess`; file deletion uses `SetFileInformationByHandle` on that same handle. Missing-file and directory creation use `NtCreateFile` with exactly one name relative to the pinned parent handle, then verify the created handle attributes and final path. Enumeration reads directory records directly from the verified directory handle and revalidates that handle before and after returning direct child names; it never reopens the target by string path.

All operations revalidate handle identity immediately before execution. A missing or lease-free inspector result fails closed. Identity or namespace changes produce `RaceDetected`. Cancellation tokens are propagated, and no failure response includes an absolute path.

## Dependency injection

`AddKejiWorkspaceFileSystem` registers immutable options and the candidate resolver and inspector as singletons. The access policy and execution service are scoped so authenticated request identity cannot leak between requests.

## Known limits

- Directory enumeration is bounded but is not an atomic snapshot; concurrent child creation or deletion can change the names observed. Every later operation must independently resolve, authorize, inspect, and pin its own target.
- Existing-file writes are performed in place on the verified handle. Cancellation, process termination, or storage failure can leave partial existing-file content; callers must not treat those writes as transactional. A failed newly-created-file write performs best-effort deletion through the same handle.
- A more privileged process, kernel component, or hostile ACL change is outside the application authorization boundary.
- The implementation is intentionally Windows-only and assumes the configured local filesystem supports the required handle and identity operations.
- Workspace candidacy and authorization do not establish Tool Registry registration. TASK-010 remains responsible for frozen tool registration and execution eligibility.

## Verification

- `Keji.FileSystem.Tests`: 289/289
- Full solution: 1061/1061
- Failed: 0
- Skipped: 0
- Build warnings: 0
- Build errors: 0
- Known NuGet vulnerabilities: 0
- `git diff --check`: empty stdout and stderr

TASK-007 P1-P4 is accepted at commit `b88827df4b68158b0bd6a0a6c9f892c4bda55d92`.

## C# Migration Context

TASK-008 (structured security audit foundation) is ready for acceptance following this workspace sandbox work. See `AUDITING.md`.
