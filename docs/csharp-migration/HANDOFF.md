# C# Migration Handoff

## Current state

- Repository: `dsja-a/newKJ`
- Branch: `rewrite/csharp-core`
- Last accepted baseline: `604846119096addaec0268dd85e719b13426ac5c`
- Current task: TASK-012
- Current status: accepted (hardened)
- Formal C# completion: 50%
- Next task: TASK-013

## TASK-011 (Repair): Isolated ToolWorker

### Architecture

Three-tier execution pipeline:

```
Host process                          ToolWorker.Client            ToolWorker process (per-request)
┌─────────────────────┐              ┌──────────────────┐         ┌──────────────────────────────┐
│ IToolExecutionPipeline│──delegates─▶│ToolExecutionCoordinator│   │ Program.cs                   │
│ (Keji.Tools.Execution)│             │ 1. Resolve tool   │──────▶│ 1. Read binary frame (stdin) │
│                      │             │ 2. Check avail    │binary  │ 2. Validate ProtocolVersion  │
│                      │             │ 3. Check target   │frame   │ 3. Validate RequestId (32hex)│
│                      │             │ 4. Authorize      │protocol│ 4. Check DeadlineUtc         │
│                      │             │ 5. WorkerClient   │        │ 5. Resolve tool (KejiTools)  │
│                      │             │ 6. Audit          │◀───────│ 6. Match ContractVersion     │
│                      │             └──────────────────┘         │ 7. Verify Avail/Target        │
│                      │                                          │ 8. Lookup executor registry  │
│                      │                                          │ 9. Execute & return frame    │
└─────────────────────┘                                          └──────────────────────────────┘
```

### Project split

| Project | Purpose |
|---|---|
| `Keji.ToolWorker.Protocol` | Binary length-prefixed frame protocol + DTOs (no deps) |
| `Keji.ToolWorker.Client` | IToolWorkerClient, ToolExecutionCoordinator, WorkerJobObject, WorkerPathValidator |
| `Keji.ToolWorker` (EXE) | Entry point, safe expression parser, executor registry |
| `Keji.ToolWorker.Tests` | Unit tests + cross-process integration tests |
| `Keji.Tools.Execution` | Updated ToolWorkerLauncher using binary protocol + ToolExecutionPipeline |

### Host-side pipeline (Keji.Tools.Execution)

| Component | Responsibility |
|---|---|
| `IToolExecutionPipeline` | Orchestration interface: execute tool by name + inputs |
| `ToolExecutionPipeline` | Full flow: user → registry → availability → target → validation → auth → worker → audit |
| `ToolWorkerLauncher` | Binary frame IPC (4-byte length prefix + UTF-8 JSON), using WorkerFrameReader/Writer |
| `ToolExecutionResult` | Result model (success/value/error/duration) |

### ToolWorker-side components (Keji.ToolWorker)

| Component | Responsibility |
|---|---|
| `Program.cs` | Binary frame protocol loop: read request → validate → dispatch → write response |
| `SafeExpressionParser` | Hand-written recursive-descent decimal expression parser (no DataTable.Compute) |
| `IKejiToolExecutor` | Interface for tool executors |
| `KejiToolExecutorRegistryBuilder` | Builder pattern, frozen on Freeze() |
| `KejiFrozenToolExecutorRegistry` | Immutable executor lookup |
| `CalculatorToolExecutor` | Evaluate via SafeExpressionParser, returns {result, expression} |
| `GetTimeToolExecutor` | Returns {utc_iso8601, unix_seconds, unix_milliseconds} |

### Binary IPC Protocol

**Frame format:** `[4-byte big-endian payload length][UTF-8 JSON payload]`

- Max request/response: 1 MiB (1,048,576 bytes)
- Max stderr capture: 8 KiB
- JSON depth: unrestricted (practical limit via SafeExpressionParser)

### Pipeline gates

1. **User resolution** — `ICurrentUserAccessor.CurrentUser`
2. **Tool resolution** — `IKejiToolRegistry.Resolve(KejiToolName)`
3. **Availability** — must be `KejiToolAvailability.Executable`
4. **ExecutionTarget** — must be `KejiToolExecutionTarget.ToolWorker`
5. **Authorization** — `IKejiAuthorizationService.Authorize(currentUser, definition.RequiredPermission)`
6. **Worker launch** — process-per-request with binary frame protocol + Windows Job Object
7. **Audit** — `IKejiAuditService.WriteAsync(KejiAuditCategory.ToolExecution, ...)`

### Tool status changes

| Tool | Availability | ExecutionTarget |
|---|---|---|
| `calculator` | Executable | ToolWorker |
| `get_time` | Executable | ToolWorker |
| Other 45 tools | ContractOnly | varies |

### Safety properties

- **process-per-request**: each invocation spawns a new worker process, no state shared.
- **Binary frame protocol**: length-prefixed, prevents line-splitting attacks.
- **Safe expression parser**: char whitelist, recursive-descent with decimal, token/depth/step limits.
- **No DataTable.Compute**: eliminates code injection via expression evaluation.
- **Windows Job Object**: KILL_ON_JOB_CLOSE + active process/memory limits.
- **Timeout**: 10-second default, 30-second max.
- **Cancellation**: CancellationToken kills the worker process.
- **Worker double-validation**: ProtocolVersion, RequestId (32-char hex), DeadlineUtc, tool registry, ContractVersion, Availability, ExecutionTarget, executor existence.
- **Unauthorized/ContractOnly/invalid requests never reach the worker** — rejected by host pipeline before process launch.
- **Audit**: Category=ToolExecution, Action=tool_execute; failure doesn't change business result.

### Not implemented in TASK-011

- Host-targeted tool executors (deferred).
- PythonWorker bridge (TASK-016).
- File read/write, network, database, knowledge base, OCR, Office, email, archive, agent loop, providers/SSE, SmartQuery, shell, code execution.

## Test coverage

| Test file | Project | Count | Area |
|---|---|---|---|
| `FrameProtocolTests.cs` | ToolWorker.Tests | 6 | binary frame roundtrip, oversize, empty stream |
| `CalculatorExpressionParserTests.cs` | ToolWorker.Tests | 20 | valid expressions, invalid chars, div/0, nesting |
| `GetTimeExecutorTests.cs` | ToolWorker.Tests | 1 | get_time returns valid ISO8601 |
| `CoordinatorTests.cs` | ToolWorker.Tests | 9 | coordinator pipeline, auth, audit, tool resolution |
| `ToolWorkerIntegrationTests.cs` | ToolWorker.Tests | 5 | cross-process: calculator, get_time, unknown tool, invalid requestId, expired deadline |
| `ToolExecutionPipelineTests.cs` | Tools.Tests | 6 | pipeline: invalid name, unknown tool, ContractOnly, unauthorized, no user |
| Other existing tests | various | 1523 | persistence, security, integration, filesystem, auditing, agent |

## Verification

| Project | Tests | Status |
|---|---|---|
| `Keji.Persistence.Tests` | 171/171 | passed |
| `Keji.Security.Tests` | 541/541 | passed |
| `Keji.Integration.Tests` | 148/148 | passed |
| `Keji.Auditing.Tests` | 102/102 | passed |
| `Keji.FileSystem.Tests` | 289/289 | passed |
| `Keji.Agent.Tests` | 1/1 | passed |
| `Keji.Tools.Tests` | 271/271 | passed (6 pipeline tests, ToolWorkerHandlerTests moved to own project) |
| `Keji.ToolWorker.Tests` | **51/51** | **new (20 calc parser + 6 frame proto + 1 get_time + 9 coordinator + 5 integration + 10 arch)** |
| **Full solution** | **1574/1574** | **passed** |
| Failed | 0 | |
| Skipped | 0 | |
| Build warnings | 0 | |
| Build errors | 0 | |
| NuGet vulnerabilities | 0 | |
| `git diff --check` | LF/CRLF only | |

## TASK-012 (Hardening): Model Providers and SSE Protocol

### Changes
- **ProviderBase.cs**: Complete rewrite for true streaming via `Channel<T>`, eliminating the `StreamAsyncBuffer` pattern. `SseLineReader` enforces 64 KiB max line length. Per-(choiceIndex,toolCallIndex) tool call state tracking with `TooLCallState`. Size limits: content 4 MiB, reasoning 4 MiB, tool args 256 KiB/call, 1 MiB total, 128 max tool calls per choice. Retry on 408, 409, 429, 5xx with Retry-After support. Cancel/Timeout distinction via `OperationCanceledException when (!ct.IsCancellationRequested)`.
- **ModelProviderRegistry.cs**: Uses `ImmutableDictionary` for frozen configuration after construction.
- **ModelProviderConfig.cs**: HTTPS enforcement (rejects non-loopback HTTP), loopback IP detection, `WithResolvedSecret` for secret resolution pipeline.
- **ChatCompletionStreamEvent.cs**: Added `Sequence` and `ToolCallIndex` fields.
- **KejiSseEvent.cs**: Added `ProtocolVersion` (1.0), `Sequence`, `EventId`, `TimestampUtc`.
- **KejiSseFormatter.cs**: Wire format includes `protocol_version`, `sequence`, `timestamp_utc`, optional `id:` line, `event:` + `data:` framing per SSE spec.
- **KejiSseAdapter.cs**: Sequence incrementing across events, `EventId` in `evt_N` format, `TimestampUtc` set per event, `WithCancellation(ct)` support, phase tracking preserved.

### Test files and counts
| File | Count | Key additions |
|---|---|---|
| `ProviderHardeningTests.cs` | new | ProviderBase input validation, SSE reader limits, retry scenarios, cancel/timeout, config hardening, registry freeze |
| Various | +65 (total) | Extended streaming and non-streaming test coverage |
| Various | +107 (total) | Extended formatter, adapter, and security test coverage |

### Verification
| Project | Tests | Status |
|---|---|---|
| `Keji.Providers.Tests` | **165/165** | passed (+65) |
| `Keji.Streaming.Tests` | **140/140** | passed (+107) |
| **Full solution** | **1905/1905** | passed |
| Failed | 0 | |
| Skipped | 0 | |
| Build warnings | 0 | |
| Build errors | 0 | |

## Security decisions and limits

- TASK-011 (ToolWorker) implements Host execution coordinator + isolated worker process.
- Only `calculator` and `get_time` are `Executable`. 45 tools remain `ContractOnly`.
- Host-targeted tool executors not yet implemented (deferred to future tasks).
- PythonWorker bridge not yet implemented (TASK-016).
- Registered does not imply authorized; authorized does not imply executable.
- Worker double-validates every request independently.
- Process isolation via Windows Job Object with KILL_ON_JOB_CLOSE + memory/process limits.
- Input validation and authorization happen before worker launch.
- Audit events captured for all execution outcomes.
- Safe expression parser uses char whitelist, decimal arithmetic, recursive descent with hard limits.

## Forbidden changes confirmed

- Python unchanged.
- Web frontend unchanged.
- No SmartQuery, dynamic assembly loading, or shell commands implemented.
- No Host-targeted tool executors implemented.
- No PythonWorker bridge.
- No real configuration, secret, SQLite database, user data, log, TRX, ZIP, or temporary review artifact added.
- No history rewrite performed.

## Next action

TASK-012 is accepted (hardened). The next task is TASK-013 (Model Provider Implementations). TASK-013 has not started.
