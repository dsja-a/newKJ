# C# Migration State

| Field | Value |
|---|---|
| 当前任务 | TASK-014 |
| 当前状态 | accepted (final) |
| 当前 C# 完成度 | 70% |
| 下一任务 | TASK-015（未开始） |

TASK-013 R4 最终验收基线为 `f2d0cf7701d908eef9b09e473c6a3e7922e3f0ee`，主生命周期提交为 `ed52a87966a1754d9eca8a91b9be511368980b87`。`ToolStarted` 仅表示 `IToolExecutionPipeline` 即将实际执行；所有执行前拒绝只输出失败 `ToolCompleted`，随后输出 `Error` → `RunCompleted`，且只写 `agent_tool_completed` 工具审计。用户与 Conversation 所有权检查在 `ToolStarted` 前完成，失效时无工具事件、无工具审计且不调用 Pipeline。除调用方取消外，每个 `ToolStarted` 都有且只有一个匹配 `ToolCompleted`；RunTimeout 取消已开始的 Pipeline 时也输出安全失败完成事件。Provider 在 `ChoiceFinished` 或 `Usage` 后的合法 `Error` 保留安全映射并直接输出 Agent `Error` → `RunCompleted`，不要求 Provider `Done`。R3 的 Effective Run Context、严格 UTF-8，以及既有流式、取消、Session Gate、Usage、Context、Transcript、Audit 和 TASK-012 SSE 行为保持不变。Agent 197/197、Integration 184/184（含 24 条真实 Agent 组合链路）、Providers 202/202、Streaming 156/156、全解决方案 2190/2190 本地 Gate 通过；0 失败、0 跳过、0 警告、0 错误、0 已知 NuGet 漏洞。远程无 GitHub CI 状态。TASK-014 未开始。

TASK-014 最终验收基线为 `fc2b270902bbfc855f49d463872dac1a7988ee0e`。SmartQuery 强制 `SmartQueryExecute` 与 `DatabaseRead` 双权限、当前用户数据源访问和管理员启用表白名单；模型只生成强类型 QueryPlan，不能提供 SQL。C# 编译器只生成标识符引用和参数化 `SELECT`，SQLite 执行启用 `query_only`。Schema、Plan、列、行、单元格、总结果和可选摘要均有硬上限；审计不记录问题、Plan、SQL、参数、结果、摘要、原始错误、异常、Secret 或连接引用。SmartQuery 31/31、Integration 189/189、全解决方案 2226/2226 本地 Gate 通过；0 失败、0 跳过、0 警告、0 错误、0 已知 NuGet 漏洞。C# 完成度 70%，TASK-015 未开始。
