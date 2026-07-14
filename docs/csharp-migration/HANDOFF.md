# C# Migration Handoff

## Current state

- Repository: `dsja-a/newKJ`
- Branch: `rewrite/csharp-core`
- Last accepted baseline: `bfe047aaa0f3549c95b1f840e372758c70a71e2c`
- Current task: TASK-010
- Current status: ready_for_acceptance
- Formal C# completion: 40%
- Next task: TASK-011

## Completed in TASK-010

### Tool contracts model (`Keji.Tools` project — 157 tests)

1. `KejiToolName` — regex-validated value object (`^[a-z][a-z0-9_]{0,63}$`, 1–64 chars, Ordinal). `TryCreate()` with `[NotNullWhen(true)]`. All `KejiToolName` instances use `StringComparer.Ordinal`.

2. `KejiToolContractException` — sealed exception for all contract violations. Thrown from constructors of all definition types.

3. Enum definitions (all validated via `Enum.IsDefined`):
   - `KejiToolCategory` (14 values: FileRead=1 … Other=14).
   - `KejiToolRiskLevel` (4 values).
   - `KejiToolExecutionTarget` (3 values).
   - `KejiToolAvailability` (2 values: `ContractOnly`, `ExecutionPending` — no executable code).
   - `KejiToolParameterType` (6 values: String, Integer, Boolean, Double, Array, Object).

4. `KejiToolParameterDefinition` — immutable class with get-only properties. Validates name, type, default, constraints, allowedValues, sensitive, maxItems for arrays. Default values must be convertible to the declared type.

5. `KejiToolInputSchema` — parameter collection. Null list → empty schema. Duplicate names → `KejiToolContractException`.

6. `KejiToolDefinition` — immutable, validates: ContractVersion ≥1, description 1–2000 non-empty, tags ≤16 × ≤32 chars, `RequiredPermission` via `KejiPermissionCatalog.IsDefined()`, all enums defined.

7. Registry (two-phase):
   - `KejiToolRegistryBuilder` — startup-only `Register()`, single `Build()`. Duplicate name → throw. Double-build → throw.
   - `IKejiToolRegistry` — read-only: `Resolve(KejiToolName)`, `Contains(KejiToolName)`, `GetAll()`.
   - `KejiFrozenToolRegistry` — internal implementation. Uses `FrozenDictionary` + `ReaderWriterLockSlim`. Ordinal name comparison. `GetAll()` returns ordered copy.

8. Resolution: `KejiToolResolutionStatus` (Found=1, InvalidName=2, NotRegistered=3). `KejiToolResolution` via factory methods `Found()` / `InvalidName()` / `NotRegistered()`.

9. `KejiToolInputValidator` — static `Validate()`. Validates name format, registration, required params, CLR types, constraints, allowed values. Sensitive-parameter-safe error messages. First-error-only (early-exit). Returns `KejiToolValidationResult` via `Valid()` / `Invalid(error, message)` factories. `KejiToolValidationError` enum (9 codes).

10. `BuiltInToolCatalog` — 43 tool definitions derived from Python baseline. All `ContractOnly` or `ExecutionPending`. No executable code, no Delegate/Type/MethodInfo. Uses constructor parameter syntax for immutable `KejiToolParameterDefinition`.

11. `ServiceCollectionExtensions.AddKejiToolRegistry()` — DI registration as singleton `IKejiToolRegistry`.

12. Python baseline high-risk tools (exec, run_code, db_execute_query, cron, spawn, notebook_edit, my) classified as `RejectedLegacyCapability`, not registered.

### Forbidden features NOT implemented

- No ToolWorker, agent, SmartQuery, Python Worker, process execution, dynamic assembly loading, or shell commands.
- No reflection-based tool discovery. No mutable static Dictionary. No Delegate/Type/MethodInfo in registry.
- No `ExecuteAsync` / `InvokeAsync` / `RunAsync` methods.

### Test coverage

| Test file | Count | Area |
|---|---|---|
| `KejiToolNameTests.cs` | 16 | valid/invalid names, TryCreate, equality |
| `KejiToolDefinitionTests.cs` | 18 | property validation, constraints, all enum values |
| `KejiToolParameterDefinitionTests.cs` | 18 | name/type/default/constraints/sensitive/array |
| `KejiToolInputSchemaTests.cs` | 5 | empty, single, duplicate, null |
| `KejiToolRegistryTests.cs` | 13 | register/build, dedup, freeze, resolve, concurrent |
| `KejiToolValidationTests.cs` | 19 | valid/invalid inputs, type matching, constraints, enums, sensitive, multiple errors |
| `BuiltInToolCatalogTests.cs` | 13 | 43 tools validation, no duplicates, specific tool checks, builder |
| `DependencyInjectionTests.cs` | 3 | registration, singleton, builtins |
| **Total** | **157** | |

## Verification

| Project | Tests | Status |
|---|---|---|
| `Keji.Persistence.Tests` | 171/171 | passed |
| `Keji.Security.Tests` | 541/541 | passed |
| `Keji.Integration.Tests` | 148/148 | passed |
| `Keji.Auditing.Tests` | 102/102 | passed |
| `Keji.FileSystem.Tests` | 289/289 | passed |
| `Keji.Agent.Tests` | 1/1 | passed |
| `Keji.Tools.Tests` | **157/157** | **passed (new in TASK-010)** |
| **Full solution** | **1409/1409** | **passed** |
| Failed | 0 | |
| Skipped | 0 | |
| Build warnings | 0 | |
| Build errors | 0 | |
| NuGet vulnerabilities | 0 | |
| `git diff --check` | empty | |

## Security decisions and limits (unchanged from TASK-009, plus tool-specific)

- Candidate resolution never implies authorization or tool executability.
- Tool name validation at registration and validation time rejects non-ASCII, uppercase, and empty names.
- All tool definitions are `ContractOnly` or `ExecutionPending` — no tool has executable code.
- `KejiToolInputValidator` returns generic error messages for sensitive parameters (never leaks parameter value).
- Registry is immutable post-startup; no hot-reload or dynamic registration path exists.
- Python baseline high-risk tools (exec, run_code, etc.) are explicitly excluded from `BuiltInToolCatalog`.
- User IDs and actor IDs are exactly 16 lowercase hexadecimal characters and are never trimmed.
- Unknown identities, roles, scopes, operations, candidates, and inspection states fail closed.
- Reparse points, junctions, symbolic links, mount points, and regular-file hard links are denied.
- Public operation results do not expose absolute filesystem paths or native exception details.
- Recursive enumeration and recursive deletion are not implemented.
- Audit category, severity, and outcome enums reject default/zero as undefined.
- Audit sinks are resolved from DI and run within the service scope; a failing sink does not block other sinks.

## Forbidden changes confirmed

- Python unchanged.
- Web frontend unchanged.
- No null/optional ownerUserId exists in repository or service interfaces.
- No admin bypass path exists in persistence or service layer.
- No NULL-old conversation claim path exists.
- No ToolWorker, agent, SmartQuery, Python Worker, process execution, dynamic assembly loading, or shell commands implemented.
- No real configuration, secret, SQLite database, user data, log, TRX, ZIP, or temporary review artifact added.
- No history rewrite performed.

## Next action

TASK-010 production code, tests, and documentation are ready for acceptance. The next task is TASK-011 (Isolated ToolWorker). TASK-011 has not started.
