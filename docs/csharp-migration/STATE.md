# C# Migration State

| Field | Value |
|---|---|
| 当前任务 | TASK-009 |
| 当前状态 | in_progress |
| 当前 C# 完成度 | 34% |
| 下一任务 | TASK-010 |

TASK-009 已实现（待提交）：Conversation 和 Message 的 SQL 级所有权隔离。`GetAsync`、`RenameAsync`、`DeleteAsync`、`AddAsync`（消息）、`ListByConversationAsync` 均支持可选的 `ownerUserId` 参数，过滤条件直接写入 SQL WHERE 子句。测试覆盖 33 个新用例，总计 1196/1196 通过。
