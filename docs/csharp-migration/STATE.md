# C# Migration State

TASK-014 R2 final accepted baseline is `6806886a0baa333ac3b833b6c995c1fef932b2f8`. Final production boundaries cover frozen public contracts, deterministic terminal events, cancellation and timeout separation, normalized versioned persistence, private/shared visibility, strict per-execution Secret Reference resolution, one bounded planner repair, typed deterministic SQL compilation, separate MySQL/PostgreSQL read-only execution, bounded typed results and summaries, and safe lifecycle audit. Local Gate: SmartQuery 230/230, Persistence 181/181, Integration 216/216, solution 2462/2462; failures, skips, warnings, errors, and known NuGet vulnerabilities are all zero. C# completion remains 70%; TASK-015 is `not_started`.

| Field | Value |
|---|---|
| 当前任务 | TASK-014 |
| 当前状态 | accepted (final) |
| 当前 C# 完成度 | 70% |
| 下一任务 | TASK-015（未开始） |

TASK-013 R4 最终验收基线为 `f2d0cf7701d908eef9b09e473c6a3e7922e3f0ee`，主生命周期提交为 `ed52a87966a1754d9eca8a91b9be511368980b87`。`ToolStarted` 仅表示 `IToolExecutionPipeline` 即将实际执行；所有执行前拒绝只输出失败 `ToolCompleted`，随后输出 `Error` → `RunCompleted`，且只写 `agent_tool_completed` 工具审计。用户与 Conversation 所有权检查在 `ToolStarted` 前完成，失效时无工具事件、无工具审计且不调用 Pipeline。除调用方取消外，每个 `ToolStarted` 都有且只有一个匹配 `ToolCompleted`；RunTimeout 取消已开始的 Pipeline 时也输出安全失败完成事件。Provider 在 `ChoiceFinished` 或 `Usage` 后的合法 `Error` 保留安全映射并直接输出 Agent `Error` → `RunCompleted`，不要求 Provider `Done`。R3 的 Effective Run Context、严格 UTF-8，以及既有流式、取消、Session Gate、Usage、Context、Transcript、Audit 和 TASK-012 SSE 行为保持不变。Agent 197/197、Integration 184/184（含 24 条真实 Agent 组合链路）、Providers 202/202、Streaming 156/156、全解决方案 2190/2190 本地 Gate 通过；0 失败、0 跳过、0 警告、0 错误、0 已知 NuGet 漏洞。远程无 GitHub CI 状态。TASK-014 未开始。

TASK-014 R1 最终验收基线为 `d81609abd1474fd32071c3a2f022764acbaf829c`。SmartQuery 公开请求仅允许 RunId、DataSourceId、Question 和 RequestedLimit；Provider、Model 与摘要策略由服务端控制。`RunStreamAsync` 是唯一业务主链，`RunAsync` 只聚合终态。持久化目录保存用户隔离的数据源、Dialect、Secret Reference、TLS、QueryEnabled、Sensitive 和 ForeignKey 元数据，不保存密码。MySQL 与 PostgreSQL 使用独立方言和只读 Executor；C# 编译器仅允许启用 ForeignKey Join、有界 AND/OR Filter、投影、聚合、GroupBy、排序和参数化单条 `SELECT`。SmartQuery 199/199、Integration 204/204、全解决方案 2409/2409 本地 Gate 通过；0 失败、0 跳过、0 警告、0 错误、0 已知 NuGet 漏洞。C# 完成度保持 70%，TASK-015 未开始。
