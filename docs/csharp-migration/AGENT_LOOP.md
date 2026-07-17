# C# Agent Loop Contract

TASK-013 R2 accepted baseline: `6f840c6d077bb424f2a1ab5e8d2e510c2af76bcf`.

## Execution and terminal protocol

- `IKejiAgentLoop.RunStreamAsync` is the production entry point and consumes provider `StreamAsync` only.
- Success terminates with optional `Usage`, then exactly one `RunCompleted`.
- Every non-cancellation failure terminates with optional `Usage`, exactly one `Error`, then exactly one adjacent `RunCompleted`.
- Caller cancellation emits neither `Error` nor `RunCompleted`, propagates cancellation, and releases the per-user/conversation session gate.
- Run timeout is distinct from caller cancellation and terminates as `RunTimedOut` error followed by `RunCompleted`.

## Public events and safety

The public event types are `RunStarted`, `ThinkingStarted`, `ThinkingDelta`, `AnsweringStarted`, `AnswerDelta`, `ToolStarted`, `ToolCompleted`, `Usage`, `Error`, and `RunCompleted`; every public enum starts with `Invalid = 0`. A caller supplies a validated 32-character lowercase hexadecimal RunId. Legacy `RunAsync` generates that RunId server-side.

Reasoning is emitted live only and is never persisted, audited, added to context, or included in transcripts. Tool events contain names, IDs, indexes, success, safe error codes, and duration only—never arguments or result bodies. Provider events, answer/reasoning bytes, context, iterations, tool counts, arguments, results, and the event channel are all bounded.

## SSE

`KejiAgentSseAdapter` maps Agent events to `Keji.Streaming.KejiSseEvent` and delegates all wire formatting to `KejiSseFormatter.FormatEvent`. It does not implement a second formatter. Wire event names are limited to `system_notice`, `thinking`, `think_token`, `answering`, `answer`, `tool_call`, `tool_result`, `usage`, `error`, and `done`.

Each SSE event receives an independent unique `Guid.NewGuid().ToString("N")` EventId, unrelated to RunId or sequence. The adapter strips transcript, conversation/user identifiers, prompts, reasoning history, tool arguments/results, raw errors, exceptions, stacks, and secrets.

## Context, transcript, tools, and audit

`KejiAgentContextBuilder` places the configured server system prompt first and fails explicitly on unsupported persisted roles or any message/byte limit; current persistence reconstructs only user and assistant history and does not claim to restore historical tool chains.

Transcripts are deeply immutable summaries with UTC timing, stop reason, iterations, tool count/names, final content, cumulative usage, and safe typed messages. Tools execute serially by ToolCallIndex through `IToolExecutionPipeline` only. Audit lifecycle actions are `agent_run_started`, `agent_tool_started`, `agent_tool_completed`, `agent_run_completed`, `agent_run_failed`, and `agent_run_cancelled`; audit failures do not alter the business result.

## Local acceptance evidence

- Keji.Agent.Tests: 172/172
- Keji.Integration.Tests: 177/177, including 17 real Agent composition paths
- Keji.Providers.Tests: 202/202
- Keji.Streaming.Tests: 156/156
- Full solution: 2158/2158
- Failed/skipped/build warnings/build errors/known NuGet vulnerabilities: 0/0/0/0/0

These are local Gate results. No remote GitHub CI status was available for this acceptance.
