# C# Migration Master Plan

## Baselines

- Repository: `dsja-a/newKJ`
- Branch: `rewrite/csharp-core`
- Python reference: `main` at `aad0afab7181e529a53691a4f2801f295025a2c7`
- Last accepted C# baseline: `a048c1d42b94270f833fa71e335c2c21bd3e2c23`
- Accepted solution test baseline: 2110
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

TASK-013 R1 is accepted at `a048c1d42b94270f833fa71e335c2c21bd3e2c23`. The production Agent Loop is StreamAsync-only and exposes a strongly typed bounded event stream plus Agent SSE adapter. It enforces a per-user/conversation concurrency gate, run timeout, cumulative usage, run transcript and UTC timing, bounded empty/Length recovery, explicit context-limit failures, safe Agent auditing, source-specific error mapping, repeated ownership checks, and sequential tool execution only through `IToolExecutionPipeline`. Agent tests pass 141/141, Integration tests pass 160/160, Providers tests pass 202/202, Streaming tests pass 156/156, and the full solution passes 2110/2110 with zero failures, skips, build warnings, build errors, or known NuGet vulnerabilities across 27 projects. Formal C# completion remains 60%. The next gate is TASK-014, which is `not_started`.
