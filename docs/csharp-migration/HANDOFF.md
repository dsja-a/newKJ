# C# Migration Handoff

## Current state

- Repository: `dsja-a/newKJ`
- Branch: `rewrite/csharp-core`
- Last accepted baseline: `c45f1e78a45e470dd484dadf700f14eeb3671281`
- Current task: TASK-015
- Current status: accepted (final)
- Formal C# completion: 80%
- Next task: TASK-016 (`not_started`)

## TASK-015 (Accepted): ASP.NET Core API takeover

- Functional baseline: `c45f1e78a45e470dd484dadf700f14eeb3671281`.
- `Keji.Api` explicitly owns all 87 compatibility method/path contracts.
- Agent chat and SmartQuery streaming reuse the accepted C# loops and TASK-012 SSE formatter.
- Authentication, conversations, security status, Agent, and SmartQuery execute through accepted C# services.
- Deferred capabilities are registered, authenticated, authorized, rate-limited, audited, and return fixed safe 503 responses.
- Requests/uploads/JSON, concurrency, correlation IDs, response headers, and audit metadata are bounded.
- Query-string API keys, FastAPI proxying, Python processes, raw SQL, request secrets, and production OpenAPI are absent.
- Local Gate: Integration 230/230 and solution 2476/2476; failures, skips, warnings, errors, and known NuGet vulnerabilities are all zero.
- Python and Web are unchanged. TASK-016 is `not_started`.

## TASK-014 R2 (Accepted): Final production boundaries

TASK-014 R2 is accepted at `6806886a0baa333ac3b833b6c995c1fef932b2f8`.

- Public enums have explicit `Invalid = 0`; compiled SQL and database parameter objects are internal.
- `RunStreamAsync` remains the only business chain, with deterministic phase events, adjacent `Error`/`Completed`, caller-cancellation propagation, and bounded internal timeouts.
- Keji.Persistence owns normalized, versioned SmartQuery catalog migrations and private/shared/disabled visibility enforcement.
- Secrets use strict `env:NAME`, resolve per execution without caching, and are validated for size, UTF-8, and control characters.
- Planner repair is bounded to one retry; MySQL and PostgreSQL retain separate safe dialects and read-only executors.
- Results, summaries, and audit metadata are strongly typed and bounded; sensitive inputs, SQL, parameters, rows, secrets, exceptions, and connection details are excluded from events and audit.
- Local Gate: SmartQuery 230/230, Persistence 181/181, Integration 216/216, full solution 2462/2462; zero failures, skips, warnings, errors, and known NuGet vulnerabilities.
- Python and Web are unchanged. TASK-015 is `not_started`.

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
- **ModelProviderConfig.cs**: HTTPS enforcement, loopback policy, and reference-only Secret configuration; no resolved Secret is stored.
- **ChatCompletionStreamEvent.cs**: Added `Sequence` and `ToolCallIndex` fields.
- **KejiSseEvent.cs**: Added integer `ProtocolVersion` (`1`), positive `Sequence`, independent 32-character lowercase hexadecimal `EventId`, and `TimestampUtc`.
- **KejiSseFormatter.cs**: Wire format includes `protocol_version`, `sequence`, `timestamp_utc`, optional `id:` line, `event:` + `data:` framing per SSE spec.
- **KejiSseAdapter.cs**: Sequence incrementing from 1 across events, independent GUID-derived EventIds, `TimestampUtc` set per event, `WithCancellation(ct)` support, phase tracking preserved.

### Test files and counts
| File | Count | Key additions |
|---|---|---|
| `ProviderHardeningTests.cs` | new | ProviderBase input validation, SSE reader limits, retry scenarios, cancel/timeout, config hardening, registry freeze |
| Various | +65 (total) | Extended streaming and non-streaming test coverage |
| Various | +107 (total) | Extended formatter, adapter, and security test coverage |

### Verification
| Project | Tests | Status |
|---|---|---|
| `Keji.Providers.Tests` | **202/202** | passed |
| `Keji.Streaming.Tests` | **156/156** | passed |
| **Full solution** | **1958/1958** | passed |
| Failed | 0 | |
| Skipped | 0 | |
| Build warnings | 0 | |
| Build errors | 0 | |
| Known NuGet vulnerabilities | 0 | 27 projects audited |

### Final secret boundary

- `ModelProviderConfig` stores `KejiProviderSecretReference` only; it neither resolves nor stores plaintext provider secrets.
- OpenAI accepts only `env:OPENAI_API_KEY`; DeepSeek accepts only `env:DEEPSEEK_API_KEY`.
- OpenAI and DeepSeek resolve the reference once per outbound request and do not cache the resolved value.
- Missing, oversized, or control-character secrets fail before network activity and produce only safe provider errors.
- Retry behavior and the accepted SSE protocol were not changed by this closeout.

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

## TASK-013 R2 (Accepted): Hardened streaming C# Agent Loop

TASK-013 R2 is accepted (final) at `6f840c6d077bb424f2a1ab5e8d2e510c2af76bcf`.

### TASK-013 R2 acceptance summary

- Production execution uses Provider `StreamAsync` only. Every non-cancellation failure emits `Usage? → Error → RunCompleted`; cancellation emits neither terminal event.
- Reasoning and answer phases are distinct, bounded events. Provider Done/Usage/Choice/tool-call ordering is revalidated at the Agent boundary.
- `KejiAgentSseAdapter` maps to TASK-012 `KejiSseEvent` and calls `KejiSseFormatter.FormatEvent`; it does not own wire formatting or use `agent_*` events.
- SSE EventIds are independent unique GUID `N` strings, unrelated to RunId or sequence. Transcript, identifiers, prompts, tool bodies, raw errors and secrets are stripped.
- Added a per-user/conversation concurrency Gate, bounded event Channel, RunTimeout, cumulative Usage, RunId, transcript, and UTC start/completion timestamps.
- Added bounded empty-answer and `Length` recovery. Context that cannot be represented in full now fails explicitly instead of silently dropping history.
- Added safe Agent audit events containing only run ID and result code; exception and Provider error text is never forwarded.
- Retained hard limits on iterations, context size, tool-call count, individual tool results, and aggregate tool results.
- Treats provider output as untrusted: validates assistant content, tool names, IDs, JSON shape, argument types, duplicate calls, and all configured limits.
- Advertises only `Executable` tools and executes them serially only through `IToolExecutionPipeline`; no direct executor, shell, web, Host tool, MCP, Python worker, or command path was added.
- Requires an authenticated user and owned conversation, and rechecks both before provider calls, tool execution, and message persistence.
- Persists user and final assistant messages only. Tool arguments/results remain bounded in-memory context and are not logged or persisted.
- Cancellation remains distinct; operational failures expose only bounded status/error codes without raw exception messages or stacks.
- Verification: Agent 172/172, Integration 177/177 (17 real Agent composition paths), Providers 202/202, Streaming 156/156, full solution 2158/2158; 0 failed, 0 skipped, 0 build warnings, 0 build errors, 0 known NuGet vulnerabilities.
- Evidence is from the local Gate; no remote GitHub CI status was available.
- Python and Web are unchanged. TASK-014 is `not_started`.

## Next action

TASK-015 is the next task and has not started.

## TASK-013 R3 (Accepted): Safe request and provider terminal closeout

TASK-013 R3 is accepted at `b09c31010771bfa9afc665dfea1829edf3e40c7d`.

- Invalid RunIds are replaced with a fresh lowercase 32-character hexadecimal effective RunId before events, transcripts, audit, or SSE; valid RunIds are preserved exactly.
- Conversation, provider, model, user message, and system prompt boundaries use strict UTF-8 validation. Unpaired surrogates are `InvalidRequest`, never `ContextLimit`.
- Invalid request values and user messages are absent from failure transcripts and audit metadata.
- Provider iteration order is explicit: content/reasoning/tool calls, `ChoiceFinished`, optional `Usage`, then `Done`; any late or duplicate event is `ProviderProtocolError`.
- R4 supersedes the R3 pre-execution tool event detail: these rejections emit failed `ToolCompleted`, `Error`, and `RunCompleted`, while never emitting `ToolStarted` or entering `IToolExecutionPipeline`.
- Verification: Agent 187/187, Integration 180/180 (20 real Agent composition paths), Providers 202/202, Streaming 156/156, full solution 2176/2176; 0 failed, 0 skipped, 0 build warnings, 0 build errors, and 0 known NuGet vulnerabilities.
- Python and Web are unchanged. TASK-014 remains `not_started`.

## TASK-014 R1 (Accepted): Production safe read-only SmartQuery

TASK-014 R1 is accepted at `d81609abd1474fd32071c3a2f022764acbaf829c`.

- Requires authenticated valid user plus both `SmartQueryExecute` and `DatabaseRead`.
- Resolves only a current-user-accessible data source and administrator-enabled, bounded schema metadata.
- Uses existing `IModelProvider` only to produce a closed, strongly typed QueryPlan. Models never provide executable SQL.
- Rejects malformed, duplicate-property, oversized, unknown, injected, or non-scalar plan input.
- The initial SQLite prototype has been superseded by R1's independent MySQL/PostgreSQL dialect and read-only executor boundaries.
- Returns immutable typed columns/rows with row, column, cell, schema, plan, and total-result limits.
- Optional summary treats result values as untrusted data and remains bounded.
- Audit excludes prompts, plans, SQL, parameters, rows, summaries, provider errors, exceptions, stacks, secrets, and connection references.
- R1 adds server-controlled model selection, the event-stream-only execution chain, persisted user-isolated MySQL/PostgreSQL data sources, per-run Secret resolution, independent dialect/read-only executors, sensitive/query-enabled/FK metadata, enabled-FK joins, aggregation, GroupBy, and bounded AND/OR filters.
- Local Gate: SmartQuery 199/199, Integration 204/204, Agent 197/197, Providers 202/202, Streaming 156/156, full solution 2409/2409; zero failures, skips, warnings, errors, and known NuGet vulnerabilities.
- Python and Web are unchanged. TASK-015 is `not_started`.

## TASK-013 R4 (Accepted): Final tool lifecycle and Provider terminal state

TASK-013 R4 is finally accepted at `f2d0cf7701d908eef9b09e473c6a3e7922e3f0ee` (primary R4 lifecycle commit `ed52a87966a1754d9eca8a91b9be511368980b87`).

- `ToolStarted` only means `IToolExecutionPipeline` is about to execute. Every pre-execution rejection emits one failed `ToolCompleted` with the correct call identity, safe code, and zero duration, followed by `Error` and `RunCompleted`.
- Pre-execution rejection writes `agent_tool_completed` only. It does not write `agent_tool_started`.
- User and conversation ownership are rechecked before `ToolStarted`; ownership loss emits no tool event or tool audit and never calls the pipeline.
- Except for caller cancellation, every emitted `ToolStarted` has exactly one matching `ToolCompleted`, including a safe failed completion when RunTimeout cancels an active pipeline call.
- Provider `Error` after `ChoiceFinished` or after `Usage` preserves the mapped safe error and terminates as `Error` then `RunCompleted` without requiring Provider `Done`.
- Local verification: Agent 197/197, Integration 184/184 (24 real Agent composition paths), Providers 202/202, Streaming 156/156, full solution 2190/2190; 0 failed, 0 skipped, 0 build warnings, 0 build errors, and 0 known NuGet vulnerabilities.
- Python and Web are unchanged. TASK-014 remains `not_started`.
