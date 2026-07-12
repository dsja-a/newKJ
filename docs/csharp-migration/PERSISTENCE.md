# Persistence Layer (TASK-004)

## 概述

实现了 SQLite 持久化基础设施，完全兼容 Python 原版的数据库结构和行为。使用 **Microsoft.Data.Sqlite** 直接操作 SQLite，无 EF Core / Dapper 依赖。

## 架构

```
KejiPersistenceOptions → DatabasePathResolver → SqliteConnectionFactory → Repositories
                                                                            ├── SqliteUserRepository
                                                                            ├── SqliteConversationRepository
                                                                            ├── SqliteMessageRepository
                                                                            └── SqliteSettingsRepository
KejiDatabaseInitializer (schema + migration)
IUnixTimeProvider (time abstraction)
DI: ServiceCollectionExtensions.AddKejiPersistenceFoundation()
```

## 关键设计决策

### 数据库连接
- **`SqliteConnectionFactory`**: 打开 `ReadWriteCreate` 连接，池化启用
- 每次连接设置: `PRAGMA foreign_keys=ON`, `PRAGMA busy_timeout=<ms>`
- WAL 日志模式通过 `PRAGMA journal_mode=WAL` 确保一次（`_walEnsured` 标志）

### 时间格式
- Unix 纪元秒（REAL 类型），兼容 Python 的 `time.time()`
- `IUnixTimeProvider` + `UnixTimeProvider`（默认实现）
- 使用 `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0`

### 用户系统
- 用户 ID: 16 字符小写十六进制字符串（`Guid.NewGuid().ToString("N")[..16]`）
- `DuplicateUsernameException` 用于重复用户名
- `UpdateAsync` 仅更新非 null 字段（白名单: `display_name`, `password_hash`, `is_active`, `role`）
- `TouchLoginAsync` 更新 `last_login_at` 并返回行数
- `DeleteAsync` 事务性删除: 消息 → 对话 → 用户

### 对话系统
- `CreateAsync`: `INSERT OR IGNORE`，幂等
- `EnsureOwnedAsync`: 返回 `ConversationOwnershipResult` 枚举（`Created`, `AlreadyOwned`, `ClaimedUnowned`, `OwnedByAnotherUser`）
- `DeleteAsync`: 事务性删除消息 + 对话
- 列表支持按 `owner_user_id` 过滤，按 `updated_at DESC` 排序

### 消息系统
- `AddAsync`: 事务性插入消息 + 递增 `message_count`；外键约束由 SQLite 强制执行
- `ListByConversationAsync`: 按 `created_at ASC, id ASC` 排序，限制 1-1000 条

### 设置系统
- `GetAsync`/`SetAsync`/`GetAllAsync`
- `SetAsync`: `INSERT ... ON CONFLICT(key) DO UPDATE`

### 数据库初始化
- `KejiDatabaseInitializer.InitializeAsync()`: 创建所有表、索引
- 通过 `pragma_table_info('conversations')` 检测 `owner_user_id` 列
- 缺少时执行 `ALTER TABLE` 迁移，记录到 `schema_migrations`（版本 `001_add_owner_user_id`）
- 幂等：重复调用安全

### 异常处理
- `KejiPersistenceException`: 基础异常
- `DuplicateUsernameException`: 重复用户名
- `ConversationOwnershipException`: 所有权冲突

### 依赖注入
- `AddKejiPersistenceFoundation()`: 注册所有服务为 Singleton
- 注册时不执行文件 I/O（延迟初始化）
- 选项使用 `IEnumerable<IConfigureOptions<KejiPersistenceOptions>>` 合并

## 文件清单

| 文件 | 说明 |
|------|------|
| `KejiPersistenceOptions.cs` | 选项模型，`FromConfiguration()` |
| `DatabasePathResolver.cs` | 路径安全解析 |
| `IUnixTimeProvider.cs` / `UnixTimeProvider.cs` | 时间抽象 |
| `ISqliteConnectionFactory.cs` / `SqliteConnectionFactory.cs` | 连接工厂 |
| `IKejiDatabaseInitializer.cs` / `KejiDatabaseInitializer.cs` | 数据库初始化 |
| `Exceptions/*.cs` | 自定义异常 |
| `Models/*.cs` | 记录模型和命令 |
| `Repositories/*.cs` | 四个仓库实现 |
| `DependencyInjection/ServiceCollectionExtensions.cs` | DI 注册 |
| `Tests/PersistenceTests.cs` | ~80 个测试 |
