# C# Migration Master Plan

## Baselines

- Repository: `dsja-a/newKJ`
- Branch: `rewrite/csharp-core`
- Python reference: `main` at `aad0afab7181e529a53691a4f2801f295025a2c7`
- Last accepted C# baseline: `ed52a87966a1754d9eca8a91b9be511368980b87`
- Accepted solution test baseline: 2189
- Target: .NET 10 / `net10.0`, ASP.NET Core as the only public API, Python as a controlled internal worker

## Remaining quality gates

| Task | Outcome | Required main commit |
|------|---------|----------------------|
| TASK-006 | Strongly typed, default-deny authorization | `feat: implement default-deny authorization foundation` |
| TASK-007 | Windows workspace path sandbox | `feat: implement workspace path sandbox` |
| TASK-008 | Structured security audit foundation | `feat: implement security audit foundation` |
| TASK-009 | Session and conversation ownership isolation | `feat: enforce session ownership isolation` |
| TASK-010 | Tool contracts and frozen registry | `feat: implement tool contracts and registry` |
| TASK-011 | Isolated ToolWorker | `feat: implement isolated tool worker` |
| TASK-012 | Model providers and SSE protocol | `feat: implement provider streaming and sse` |
| TASK-013 | C# Agent Loop | `feat: implement agent loop foundation` |
| TASK-014 | Safe read-only SmartQuery | `feat: implement safe smart query` |
| TASK-015 | ASP.NET Core API takeover | `feat: complete csharp api takeover` |
| TASK-016 | Restricted Python worker bridge | `feat: bridge python worker capabilities` |
| TASK-017 | Final integration and security verification | `test: complete migration integration verification` |

## Gate rules

Each task reads the Python contract and current C# implementation, implements production code plus real unit and integration tests, updates migration documentation and state, and runs the full clean/restore/build/test/vulnerability/diff gate. A task is committed and pushed only after its gate passes and the prior accepted behavior remains covered. Tasks are never combined into one commit and work never advances past a failed gate.

Python and the web frontend remain unchanged until their explicitly assigned tasks. Real secrets, production configuration, user data, and SQLite artifacts are never committed.

## Current gate

TASK-013 R4 is accepted at `ed52a87966a1754d9eca8a91b9be511368980b87`. `ToolStarted` only means `IToolExecutionPipeline` is about to execute; pre-execution rejection emits only a failed `ToolCompleted` before the Agent terminal pair, and ownership is rechecked before any tool event or audit. Provider `Error` after `ChoiceFinished` or `Usage` preserves its safe mapped classification without requiring `Done`. Existing effective run context, strict UTF-8, streaming, cancellation, session gate, bounded context, usage, transcript, audit, and TASK-012 SSE behavior remains intact. Agent tests pass 196/196, Integration tests pass 184/184 (including 24 real Agent composition paths), Providers tests pass 202/202, Streaming tests pass 156/156, and the full solution passes 2189/2189 locally with zero failures, skips, build warnings, build errors, or known NuGet vulnerabilities across 27 projects. Remote GitHub CI status was unavailable. Formal C# completion remains 60%. TASK-014 is `not_started`.
