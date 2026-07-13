# 授权系统（TASK-006）

## 目标

科吉 C# 授权层采用强类型、默认拒绝模型。认证只负责建立 `CurrentUser`；HTTP、工具和后续 Agent 操作必须分别通过明确的授权决策。普通拒绝返回结构化决策，不以异常控制流程。

## 强类型权限

`KejiPermission` 是包含以下 25 项的 `enum`：

| 权限 | admin | member | readonly |
|------|:-----:|:------:|:--------:|
| `AccountSelfRead` | ✓ | ✓ | ✓ |
| `ChatUse` | ✓ | ✓ | ✓ |
| `ConversationRead` | ✓ | ✓ | ✓ |
| `ConversationWrite` | ✓ | ✓ | ✓ |
| `FileRead` | ✓ | ✓ | ✓ |
| `FileWrite` | ✓ | ✓ |  |
| `KnowledgeRead` | ✓ | ✓ | ✓ |
| `KnowledgeWrite` | ✓ | ✓ |  |
| `ToolCatalogRead` | ✓ | ✓ | ✓ |
| `ToolExecuteRead` | ✓ | ✓ | ✓ |
| `ToolExecuteWrite` | ✓ | ✓ |  |
| `SmartQueryExecute` | ✓ | ✓ | ✓ |
| `SettingsRead` | ✓ | ✓ | ✓ |
| `SettingsWrite` | ✓ |  |  |
| `ModelManage` | ✓ |  |  |
| `McpManage` | ✓ |  |  |
| `DatabaseRead` | ✓ | ✓ | ✓ |
| `DatabaseManage` | ✓ |  |  |
| `SkillsRead` | ✓ | ✓ | ✓ |
| `SkillsManage` | ✓ |  |  |
| `SystemRead` | ✓ | ✓ | ✓ |
| `SystemManage` | ✓ |  |  |
| `AdminUsers` | ✓ |  |  |
| `AdminConversations` | ✓ |  |  |
| `AuditRead` | ✓ |  |  |

完整集合大小为 admin 25、member 16、readonly 13。角色只接受 Ordinal 严格匹配的小写 `admin`、`member`、`readonly`；null、空、空白、大小写变体和其他角色均无权限。对外暴露的角色和权限集合不可修改。

权限声明只能使用强类型构造函数：

```csharp
[KejiRequirePermission(KejiPermission.AccountSelfRead)]
```

多个 `KejiRequirePermissionAttribute` 使用 AND 语义。`KejiAllowAnonymousAttribute` 实现 ASP.NET Core `IAllowAnonymous`；授权链同时支持自定义匿名特性和标准 `[AllowAnonymous]`。

## HTTP 默认拒绝

生产中间件顺序固定为：

```csharp
app.UseMiddleware<KejiApiExceptionMiddleware>();
app.UseRouting();
app.UseMiddleware<KejiAuthenticationMiddleware>();
app.UseMiddleware<KejiAuthorizationMiddleware>();
app.MapControllers();
```

授权规则：

| 条件 | 结果 |
|------|------|
| Security 关闭 | 跳过授权 |
| Endpoint 为 null | 调用后续管道，由路由产生最终结果 |
| 仅有 `IAllowAnonymous` | 放行 |
| 匿名和权限元数据同时存在 | 抛出安全配置异常，由全局边界返回 500 `{"detail":"服务器内部错误"}` |
| 缺少匿名和权限元数据 | 403 `{"detail":"权限不足"}`，admin 也不能绕过 |
| 存在权限元数据但无用户 | 401 `{"detail":"未登录，请先登录"}` |
| readonly 请求任意写权限 | 403 `{"detail":"当前账号无写入权限"}` |
| 非 admin 请求 admin-only 权限 | 403 `{"detail":"需要管理员权限"}` |
| 其他拒绝 | 403 `{"detail":"权限不足"}` |

`AuthController.Login` 使用 `[KejiAllowAnonymous]`，`AuthController.Me` 使用 `[KejiRequirePermission(KejiPermission.AccountSelfRead)]`。登录与 Me 的 TASK-005 请求响应契约不变。

## 授权决策

`KejiAuthorizationDecision` 包含允许状态和 `KejiAuthorizationFailureReason`。失败原因包括：

- `None`
- `Unauthenticated`
- `UnknownRole`
- `MissingPermissionMetadata`
- `InvalidPermissionMetadata`
- `PermissionDenied`
- `AdminRequired`
- `ReadonlyWriteDenied`
- `UnknownTool`
- `InvalidToolDescriptor`

null 用户、未知角色、无权限、非法 enum 和缺失元数据均返回拒绝决策。只有互相冲突的 Endpoint 元数据属于服务器配置错误并进入安全 500 边界。

## Legacy 工具分类

`KejiLegacyToolPermissionCatalog` 是 Python 迁移兼容适配器，不是工具注册中心：

- 显式迁移 Python legacy 名称及其 Read/Write/Admin 分类。
- Python 列出的 10 个 `mcp_filesystem_*` 读取工具分类为 Read。
- 其他 `mcp_filesystem_*` 名称按 fail-safe 原则分类为 Write。
- 完全未知的普通工具无法解析。
- 分类成功不等于已注册；TASK-010 的冻结 Registry 将成为注册事实来源。

`KejiToolPermissionDescriptor` 不可变，工具名不允许 null、空、空白、隐式 Trim 或超过 256 字符，AccessLevel 必须是有效枚举。

工具用户授权由独立的 `IKejiToolAuthorizationService` / `KejiToolAuthorizationService` 执行：null 用户、null/非法 Descriptor、未知工具和未知角色全部拒绝；Read 对三个合法角色开放，Write 仅 admin/member，Admin 仅 admin。admin 不能绕过未知或缺失 Descriptor。

## 依赖注入

`AddKejiSecurityFoundation` 注册：

- `IKejiRolePermissionMatrix` → `KejiRolePermissionMatrix`
- `IKejiAuthorizationService` → `KejiAuthorizationService`
- `IKejiToolAuthorizationService` → `KejiToolAuthorizationService`
- `IKejiRolePermissionHintProvider` → `KejiRolePermissionHintProvider`

## 测试边界

TASK-005 的 Security 262 项和 Integration 48 项测试原样保留。TASK-006 测试位于独立 `Authorization/` 目录，新增 Security 241 项和 Integration 23 项，覆盖完整集合、失败原因、工具分类、HTTP 精确响应、API Key、Localhost、Enabled=false、未知路由、冲突元数据和生产 Controller 元数据扫描。

测试专用 Probe Controller 只存在于 Integration 测试程序集，精确包含以下 Action：

- `anonymous`
- `standard-anonymous`
- `no-metadata`
- `account-read`
- `file-read`
- `file-write`
- `admin-users`
- `admin-conversations`
- `multiple-permissions`
- `conflicting-metadata`
- `throwing`

生产 Controller 元数据测试动态扫描 `typeof(Program).Assembly` 中所有公开 Controller Action；只检查带 `HttpMethodAttribute` 的 Action，并要求每个 Action 恰好选择一种安全模式：实现 `IAllowAnonymous`，或包含至少一个值有效的 `KejiRequirePermissionAttribute`。扫描不硬编码 `AuthController`，测试 Probe 也不在生产程序集内。

Integration 授权测试使用独立临时配置和 SQLite 数据库，保存并恢复测试环境变量，并在清理前调用 `SqliteConnection.ClearAllPools()`；不会读取或写入真实 `.env`、`config.yaml` 或 `data/keji.db`。

全解决方案验证结果为 715/715，0 skipped；构建 0 warning / 0 error；NuGet 已知漏洞 0。
