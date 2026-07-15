# C# Migration Handoff

## Current state

- Repository: `dsja-a/newKJ`
- Branch: `rewrite/csharp-core`
- Last accepted baseline: `cd00ef16088b80d23507f4fdc54aa2108de906ff`
- Current task: TASK-010
- Current status: accepted
- Formal C# completion: 40%
- Next task: TASK-011

## Repair scope: TASK-010 hardened tool contracts

### 1. Parameter Type validation

`KejiToolParameterDefinition` constructor now validates `Enum.IsDefined(type)` before any other check. Rejects `(KejiToolParameterType)0`, `(KejiToolParameterType)(-1)`, `(KejiToolParameterType)999`.

### 2. Constraint type matching

Each `KejiToolParameterType` only accepts its valid constraints:

| Type | Valid Constraints | Invalid Constraints (rejected) |
|---|---|---|
| `String` | MinLength, MaxLength (required), AllowedValues | Minimum, Maximum, MaxItems, MaxItemLength |
| `Integer` | Minimum, Maximum | MinLength, MaxLength, MaxItems, MaxItemLength, AllowedValues |
| `Number` | Minimum, Maximum | MinLength, MaxLength, MaxItems, MaxItemLength, AllowedValues |
| `Boolean` | None | All constraints, AllowedValues |
| `StringArray` | MaxItems (required), MaxItemLength (required) | Minimum, Maximum, MinLength, MaxLength, AllowedValues |
| `IntegerArray` | MaxItems (required) | Minimum, Maximum, MinLength, MaxLength, MaxItemLength, AllowedValues |

### 3. Long/Number/array type handling (`KejiToolInputValidator`)

- Integer: accepts `int` and `long`, both checked against Min/Max.
- Number: accepts `int`, `long`, `float`, `double`. NaN and Infinity rejected.
- IntegerArray: accepts `IReadOnlyList<int>` and `IReadOnlyList<long>`, MaxItems enforced on both.

### 4. Deep immutability

- `DefaultValue` for StringArray: stored as `ImmutableArray<string>` (snapshot via `ImmutableArray.Create`).
- `DefaultValue` for IntegerArray: stored as `ImmutableArray<long>` (snapshot via `ImmutableArray.CreateRange`).
- `Tags`: stored as `FrozenSet<string>` (not `HashSet<string>`).
- `InputSchema.Parameters`: stored as `ImmutableArray<KejiToolParameterDefinition>` (preserves insertion order, immutable).
- Empty schema: `ImmutableArray<KejiToolParameterDefinition>.Empty`.
- `BuiltInToolCatalog.All`: stored as `ImmutableArray<KejiToolDefinition>`.

### 5. Null contracts

- `KejiToolDefinition` rejects `name == null`.
- `KejiToolDefinition` rejects null parameters in schema.
- `KejiToolDefinition` rejects null tags, empty tags, tags with control characters.
- `KejiToolInputSchema` rejects null parameter in its constructor.
- All failures throw `KejiToolContractException`, never `NullReferenceException`.

### 6. Built-in catalog (47 tools)

All 47 tools now have:
- Every string parameter has explicit `MaxLength`.
- Every array parameter has explicit `MaxItems`.
- Sensitive parameters (db passwords) have `Sensitive=true` and bounded length.
- All tools `Availability = ContractOnly`.

### 7. Python baseline mapping (47 tools)

Complete per-tool mapping table created in `TOOL_REGISTRY.md` showing Python source file, C# canonical name, category, risk level, permission, execution target, and security tightening for each of the 47 tools. 7 high-risk Python tools (exec, run_code, db_execute_query, cron, spawn, notebook_edit, my) are classified as RejectedLegacyCapability and excluded.

## Test coverage

| Test file | Count | Area |
|---|---|---|
| `KejiToolNameTests.cs` | 16 | valid/invalid names, TryCreate, equality |
| `KejiToolDefinitionTests.cs` | 27 | property validation, constraints, null/empty tags, immutability |
| `KejiToolParameterDefinitionTests.cs` | 55 | type/constraint validation, enum checks, immutability, defaultValue validation, invalid names |
| `KejiToolInputSchemaTests.cs` | 9 | empty, single, duplicate, null, immutability, order |
| `KejiToolRegistryTests.cs` | 13 | register/build, dedup, freeze, resolve, concurrent |
| `KejiToolValidationTests.cs` | 38 | valid/invalid inputs, long/NaN/Infinity, arrays, sensitive, MaxItemLength |
| `BuiltInToolCatalogTests.cs` | 22 | 47 tools, names, params, MaxLength, MaxItems, ContractOnly, ImmutableArray |
| `DependencyInjectionTests.cs` | 3 | registration, singleton, builtins |
| **Total** | **265** | |

## Verification

| Project | Tests | Status |
|---|---|---|
| `Keji.Persistence.Tests` | 171/171 | passed |
| `Keji.Security.Tests` | 541/541 | passed |
| `Keji.Integration.Tests` | 148/148 | passed |
| `Keji.Auditing.Tests` | 102/102 | passed |
| `Keji.FileSystem.Tests` | 289/289 | passed |
| `Keji.Agent.Tests` | 1/1 | passed |
| `Keji.Tools.Tests` | **265/265** | **passed (16 new in immutable refactor)** |
| **Full solution** | **1517/1517** | **passed** |
| Failed | 0 | |
| Skipped | 0 | |
| Build warnings | 0 | |
| Build errors | 0 | |
| NuGet vulnerabilities | 0 | |
| `git diff --check` | empty | |

## Security decisions and limits

- TASK-011 (ToolWorker) has not started. Tools have no execution capability.
- Registered does not imply authorized; authorized does not imply executable.
- All 47 tools are `ContractOnly`.
- Parameter type enum validated at construction (rejects 0, -1, 999).
- Constraint type matching enforced at construction.
- Deep immutability (ImmutableArray<string>, ImmutableArray<long>, ImmutableArray<KejiToolParameterDefinition>, ImmutableArray<KejiToolDefinition>) prevents mutation via caller references.
- NaN/Infinity rejected for Number type.
- Sensitive parameters return generic error messages (no value leak).
- Parameter names rejected if not matching ^[a-z][a-z0-9_]{0,63}$.

## Forbidden changes confirmed

- Python unchanged.
- Web frontend unchanged.
- No ToolWorker, agent, SmartQuery, Python Worker, process execution, dynamic assembly loading, or shell commands implemented.
- No real configuration, secret, SQLite database, user data, log, TRX, ZIP, or temporary review artifact added.
- No history rewrite performed.

## Next action

TASK-010 is accepted at `cd00ef16088b80d23507f4fdc54aa2108de906ff`. The next task is TASK-011 (Isolated ToolWorker). TASK-011 has not started.
