# C# Migration Handoff

## Current state

- Repository: `dsja-a/newKJ`
- Branch: `rewrite/csharp-core`
- Last accepted baseline: `1728b8a1c15591bbddd4f7e638174e1bcd4ac0ab`
- Current task: TASK-011
- Current status: accepted
- Formal C# completion: 45%
- Next task: TASK-012

## TASK-011: Isolated ToolWorker

### Architecture

Two-tier execution pipeline:

```
Host process                          ToolWorker process (per-request)
┌─────────────────────┐              ┌──────────────────────────┐
│ IToolExecutionPipeline │  stdin/stdout  │ WorkerRequestHandler    │
│ 1. Resolve user      │──────JSON─────▶│ 1. Re-validate registry  │
│ 2. Resolve tool      │              │ 2. Check Availability    │
│ 3. Check Availability│              │ 3. Check ExecutionTarget │
│ 4. Check ExecTarget  │              │ 4. Validate ContractVer  │
│ 5. Validate inputs   │              │ 5. Validate inputs       │
│ 6. Authorize (IKejiAuthorizationService)│ 6. Execute tool          │
│ 7. Launch worker     │◀─────JSON────│ 7. Return result         │
│ 8. Audit             │              │ 8. Exit                  │
└─────────────────────┘              └──────────────────────────┘
```

### Host-side components (`Keji.Tools.Execution`)

| Component | Responsibility |
|---|---|
| `IToolExecutionPipeline` | Orchestration interface: execute tool by name + inputs |
| `ToolExecutionPipeline` | Full flow: ICurrentUserAccessor → registry → availability → target → KejiToolInputValidator → IKejiAuthorizationService → ToolWorkerLauncher → IKejiAuditService |
| `ToolWorkerLauncher` | Spawn Keji.ToolWorker process with stdin/stdout JSON IPC |
| `ToolExecutionRequest` | Request model (tool name + inputs) |
| `ToolExecutionResult` | Result model (success/value/error/duration) |
| `WorkerProtocolMessage` | JSON IPC message (request + response in one schema) |
| `JobObject` | Windows Job Object for process isolation (KILL_ON_JOB_CLOSE) |

### ToolWorker-side components (`Keji.ToolWorker`)

| Component | Responsibility |
|---|---|
| `Program.cs` | Entry point: read JSON from stdin, dispatch to handler, write JSON to stdout |
| `WorkerRequestHandler` | Validate: tool name → registry → Availability → ExecutionTarget → ContractVersion → inputs. Route to executor. |
| `BuiltInToolWorkerRegistry` | Worker-side frozen registry (only Executable + ToolWorker tools) |
| `CalculatorExecutor` | Evaluate arithmetic expressions via `DataTable.Compute` |
| `GetTimeExecutor` | Return `DateTime.UtcNow.ToString("o")` |

### IPC Protocol

**Host → Worker (stdin):** `{"protocol":"keji-toolworker-v1","requestId":"<uuid>","tool":"calculator","args":{"expr":"2+2"},"contractVersion":1}\n`

**Worker → Host (stdout):** `{"protocol":"keji-toolworker-v1","requestId":"<uuid>","success":true,"result":4}\n` or `{"protocol":"keji-toolworker-v1","requestId":"<uuid>","success":false,"error":"message","errorCode":"CODE"}\n`

### Pipeline gates

1. **User resolution** — `ICurrentUserAccessor.CurrentUser`
2. **Tool resolution** — `IKejiToolRegistry.Resolve(KejiToolName)`
3. **Availability** — must be `KejiToolAvailability.Executable`
4. **ExecutionTarget** — must be `KejiToolExecutionTarget.ToolWorker`
5. **Input validation** — `KejiToolInputValidator.Validate()`
6. **Authorization** — `IKejiAuthorizationService.Authorize(currentUser, definition.RequiredPermission)`
7. **Worker launch** — process-per-request with Windows Job Object
8. **Audit** — `IKejiAuditService.WriteAsync(KejiAuditCategory.ToolExecution, ...)`

### Tool status changes

| Tool | Previous Availability | New Availability | Previous ExecutionTarget | New ExecutionTarget |
|---|---|---|---|---|
| `calculator` | ContractOnly | **Executable** | Host | **ToolWorker** |
| `get_time` | ContractOnly | **Executable** | Host | **ToolWorker** |
| Other 45 tools | ContractOnly | unchanged | varies | unchanged |

### Safety properties

- **process-per-request**: each invocation spawns a new worker process, no state shared.
- **stdin/stdout bounded protocol**: single request line, single response line, no persistent connection.
- **Windows Job Object**: `KILL_ON_JOB_CLOSE` ensures worker termination when the host disposes the job. Prevents orphan processes.
- **Timeout**: 30-second default, worker killed on timeout.
- **Cancellation**: `CancellationToken` kills the worker process.
- **Worker double-validation**: worker independently validates registry, Availability, ExecutionTarget, ContractVersion, and input schema.
- **Unauthorized/ContractOnly/invalid requests never reach the worker** — rejected by host pipeline before process launch.
- **`KejiAuditCategory.ToolExecution`** added for execution audit events.

### Not implemented in TASK-011

- Host-targeted tool executors (deferred).
- PythonWorker bridge (TASK-016).
- File read/write, network, database, knowledge base, OCR, Office, email, archive, agent loop, providers/SSE, SmartQuery, shell, code execution.

## Test coverage

| Test file | Count | Area |
|---|---|---|
| `KejiToolNameTests.cs` | 16 | valid/invalid names, TryCreate, equality |
| `KejiToolDefinitionTests.cs` | 27 | property validation, constraints, null/empty tags, immutability |
| `KejiToolParameterDefinitionTests.cs` | 55 | type/constraint validation, enum checks, immutability, defaultValue validation, invalid names |
| `KejiToolInputSchemaTests.cs` | 9 | empty, single, duplicate, null, immutability, order |
| `KejiToolRegistryTests.cs` | 13 | register/build, dedup, freeze, resolve, concurrent |
| `KejiToolValidationTests.cs` | 38 | valid/invalid inputs, long/NaN/Infinity, arrays, sensitive, MaxItemLength |
| `BuiltInToolCatalogTests.cs` | 22 | 47 tools, names, params, 2 Executable + 45 ContractOnly, ImmutableArray |
| `DependencyInjectionTests.cs` | 3 | registration, singleton, builtins |
| `ToolExecutionPipelineTests.cs` | 6 | pipeline: invalid name, unknown tool, ContractOnly, unauthorized, no user |
| `ToolWorkerHandlerTests.cs` | 8 | worker: calculator, get_time, missing expr, unknown tool, version mismatch, registry |
| **Total** | **279** | |

## Verification

| Project | Tests | Status |
|---|---|---|
| `Keji.Persistence.Tests` | 171/171 | passed |
| `Keji.Security.Tests` | 541/541 | passed |
| `Keji.Integration.Tests` | 148/148 | passed |
| `Keji.Auditing.Tests` | 102/102 | passed |
| `Keji.FileSystem.Tests` | 289/289 | passed |
| `Keji.Agent.Tests` | 1/1 | passed |
| `Keji.Tools.Tests` | **279/279** | **passed (14 new in TASK-011)** |
| **Full solution** | **1531/1531** | **passed** |
| Failed | 0 | |
| Skipped | 0 | |
| Build warnings | 0 | |
| Build errors | 0 | |
| NuGet vulnerabilities | 0 | |
| `git diff --check` | LF/CRLF only | |

## Security decisions and limits

- TASK-011 (ToolWorker) implements Host execution coordinator + isolated worker process.
- Only `calculator` and `get_time` are `Executable`. 45 tools remain `ContractOnly`.
- Host-targeted tool executors not yet implemented (deferred to future tasks).
- PythonWorker bridge not yet implemented (TASK-016).
- Registered does not imply authorized; authorized does not imply executable.
- Worker double-validates every request independently.
- Process isolation via Windows Job Object with `KILL_ON_JOB_CLOSE`.
- Input validation and authorization happen before worker launch.
- Audit events captured for all execution outcomes.
- Parameter type enum, constraint matching, deep immutability, NaN/Infinity rejection, sensitive parameter messages, and parameter name regex all inherited from TASK-010.

## Forbidden changes confirmed

- Python unchanged.
- Web frontend unchanged.
- No SmartQuery, dynamic assembly loading, or shell commands implemented.
- No Host-targeted tool executors implemented.
- No PythonWorker bridge.
- No real configuration, secret, SQLite database, user data, log, TRX, ZIP, or temporary review artifact added.
- No history rewrite performed.

## Next action

TASK-011 is accepted at `1728b8a1c15591bbddd4f7e638174e1bcd4ac0ab`. The next task is TASK-012 (Model Providers and SSE Protocol). TASK-012 has not started.
