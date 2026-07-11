# API 兼容性文档

## 1. 文档目的

- 本文是 Python `main` 分支的 API 行为快照，记录当前所有 HTTP 接口的请求/响应契约、鉴权方式和行为细节
- 后续 C# 实现必须以此作为兼容基线，确保行为等价
- 本文记录的是 **当前实际行为**，不代表当前行为一定安全
- 安全缺陷会在后续迁移任务中修复，本文不进行修复

## 2. 全局信息

| 项目 | 值 |
|------|-----|
| FastAPI 应用入口 | `main.py` → `app = FastAPI(title="科吉 AI 助手", docs_url=None, redoc_url=None)` |
| 框架版本 | FastAPI（基于 Starlette）+ uvicorn |
| 全局中间件 | `APIKeyMiddleware` — 统一鉴权中间件（JWT + API Key） |
| 静态文件挂载 | `app.mount("/static", StaticFiles(directory="web/"))` |
| 前端页面 | `GET /` 返回 `web/index.html`（内嵌缓存破片版本号） |
| SSE 使用方式 | `text/event-stream`，事件格式 `data: {json}\n\n`，用于流式聊天和流式智能问数 |
| 默认响应格式 | JSON（`application/json`），部分接口返回纯文本 SSE 流 |
| OpenAPI 文档 | 已关闭（`docs_url=None, redoc_url=None`） |

### 已注册 Router

| 前缀 | 来源文件 | 作用 |
|------|----------|------|
| 无前缀（直接注册 @app） | `main.py` | 主页、聊天、工具、会话、健康检查 |
| `/api` | `core/routes.py` | 知识库、文件浏览、对话管理、设置、MCP、Ollama、数据库、智能问数、技能、统计 |
| `/api/auth` | `core/routes_auth.py` | 登录、当前用户 |
| `/api/admin` | `core/routes_admin.py` | 用户管理、管理员对话查看 |
| `/api/security` | `core/routes_security.py` | 安全状态、审计日志 |
| `/api/work` | `core/wechat/work_bridge.py` | 企业微信桥接配置与回调 |

### 当前鉴权模式概述

鉴权由 `core/security/auth.py` 中的 `APIKeyMiddleware` 统一控制：

1. **SecuritySettings** 从 `config.yaml` 读取，支持的字段：
   - `enabled`（bool）：是否启用鉴权，默认 `True`
   - `auth_mode`：`both`（默认）| `user_only` | `api_key_only`
   - `api_key`：服务端 API Key（支持 `${ENV_VAR}` 引用）
   - `allow_localhost_without_auth`：本机请求跳过鉴权
   - `public_paths`：额外公开路径白名单
2. **默认公开路径**（无需鉴权）：
   - `GET /`（首页）
   - `GET /health`
   - `GET /favicon.ico`
   - `/static/*`
   - `GET /api/security/status`
   - `POST /api/auth/login`
   - `/api/work/*`（企业微信回调）
3. **鉴权失败**返回 `401` JSON：`{"detail": "未授权：请登录（/api/auth/login）或使用有效 API Key"}`
4. **JWT 登录**：`POST /api/auth/login` 返回 JWT token，后续请求通过 `Authorization: Bearer <token>` 或 `X-API-Key` 头传递
5. **API Key**：通过 `Authorization: Bearer <key>`、`X-API-Key` 头或 `?api_key=` 查询参数传递
6. **鉴权关闭时**（`enabled=False`）：所有请求视为 `admin` 角色用户 `anonymous/guest`
7. **角色系统**：`admin`、`member`、`readonly`
8. **角色限制**（在 `permissions.py` 中）：`readonly` 禁止写入类工具

> **安全注意事项**：
> - 鉴权关闭时任何人都可以任意调用所有接口，包括管理接口
> - `allow_localhost_without_auth` 开启后本机请求完全跳过鉴权
> - `/api/security/status` 公开暴露鉴权配置状态和当前用户信息
> - 企业微信回调路由 `/api/work/*` 完全公开（含 `/api/work/configure` 配置接口）
> - `POST /api/upload` 和 `POST /api/files/upload` 上传不做文件类型校验

## 3. 接口总表

| 编号 | HTTP 方法 | 完整 URL | 来源文件 | 函数名 | 鉴权 | 允许角色 | 请求类型 | 响应类型 | 是否流式 | C# 迁移状态 |
|------|-----------|----------|----------|--------|------|----------|----------|----------|----------|------------|
| 1 | GET | `/` | main.py | `home` | Public | anonymous | 无 | HTML | 否 | 未开始 |
| 2 | POST | `/chat` | main.py | `chat` | Login required | authenticated | JSON | JSON | 否 | 未开始 |
| 3 | POST | `/chat/stream` | main.py | `chat_stream` | Login required | authenticated | JSON | SSE | 是 | 未开始 |
| 4 | POST | `/chat/stop` | main.py | `stop_chat` | Login required | authenticated | Form/Query | JSON | 否 | 未开始 |
| 5 | POST | `/chat/reset` | main.py | `reset_chat` | Login required | authenticated | JSON | JSON | 否 | 未开始 |
| 6 | GET | `/tools` | main.py | `list_tools` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 7 | GET | `/sessions` | main.py | `get_sessions` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 8 | GET | `/favicon.ico` | main.py | `favicon` | Public | anonymous | 无 | 204 | 否 | 未开始 |
| 9 | GET | `/chat/mode` | main.py | `chat_mode` | Login required | authenticated | Query | JSON | 否 | 未开始 |
| 10 | GET | `/health` | main.py | `health` | Public | anonymous | 无 | JSON | 否 | 未开始 |
| 11 | GET | `/api/knowledge/documents` | core/routes.py | `list_documents` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 12 | POST | `/api/knowledge/index` | core/routes.py | `index_document` | Login required | authenticated | JSON | JSON | 否 | 未开始 |
| 13 | POST | `/api/knowledge/cancel` | core/routes.py | `cancel_indexing` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 14 | GET | `/api/knowledge/is_indexing` | core/routes.py | `check_indexing` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 15 | POST | `/api/knowledge/clear` | core/routes.py | `clear_knowledge` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 16 | DELETE | `/api/knowledge/document/{doc_id}` | core/routes.py | `delete_document` | Login required | authenticated | Path | JSON | 否 | 未开始 |
| 17 | GET | `/api/knowledge/search` | core/routes.py | `search_knowledge` | Login required | authenticated | Query | JSON | 否 | 未开始 |
| 18 | GET | `/api/knowledge/stats` | core/routes.py | `knowledge_stats` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 19 | GET | `/api/files/roots` | core/routes.py | `file_workspace_roots` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 20 | GET | `/api/files/list` | core/routes.py | `list_files` | Login required | authenticated | Query | JSON | 否 | 未开始 |
| 21 | GET | `/api/files/drives` | core/routes.py | `list_drives` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 22 | GET | `/api/files/info` | core/routes.py | `file_info` | Login required | authenticated | Query | JSON | 否 | 未开始 |
| 23 | POST | `/api/files/open` | core/routes.py | `open_file` | Login required | authenticated | Query | JSON | 否 | 未开始 |
| 24 | POST | `/api/files/mkdir` | core/routes.py | `files_mkdir` | Login required | authenticated | JSON | JSON | 否 | 未开始 |
| 25 | POST | `/api/files/upload` | core/routes.py | `files_upload` | Login required | authenticated | Multipart | JSON | 否 | 未开始 |
| 26 | GET | `/api/conversations` | core/routes.py | `list_conversations` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 27 | GET | `/api/conversations/{conv_id}` | core/routes.py | `get_conversation` | Login required | authenticated | Path | JSON | 否 | 未开始 |
| 28 | POST | `/api/chat/load` | core/routes.py | `load_conversation` | Login required | authenticated | Form/Query | JSON | 否 | 未开始 |
| 29 | DELETE | `/api/conversations/{conv_id}` | core/routes.py | `delete_conversation` | Login required | authenticated | Path | JSON | 否 | 未开始 |
| 30 | GET | `/api/settings` | core/routes.py | `get_settings` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 31 | POST | `/api/settings` | core/routes.py | `update_settings` | Login required | authenticated | JSON | JSON | 否 | 未开始 |
| 32 | POST | `/api/mcp/reload` | core/routes.py | `reload_mcp_servers` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 33 | GET | `/api/mcp/filesystem-dirs` | core/routes.py | `get_mcp_filesystem_dirs` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 34 | POST | `/api/models/test` | core/routes.py | `test_model_connection` | Login required | authenticated | JSON | JSON | 否 | 未开始 |
| 35 | GET | `/api/ollama/check` | core/routes.py | `ollama_check` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 36 | POST | `/api/ollama/pull` | core/routes.py | `ollama_pull` | Login required | authenticated | JSON | JSON | 否 | 未开始 |
| 37 | GET | `/api/tools/display` | core/routes.py | `get_tools_display` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 38 | POST | `/api/upload` | core/routes.py | `upload_file` | Login required | authenticated | Multipart | JSON | 否 | 未开始 |
| 39 | GET | `/api/upload/cleanup` | core/routes.py | `cleanup_uploads` | Login required | authenticated | Query | JSON | 否 | 未开始 |
| 40 | GET | `/api/status` | core/routes.py | `system_status` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 41 | GET | `/api/debug/logs` | core/routes.py | `debug_logs` | Login required | authenticated | Query | JSON | 否 | 未开始 |
| 42 | GET | `/api/debug/agent-state` | core/routes.py | `debug_agent_state` | Login required | authenticated | Query | JSON | 否 | 未开始 |
| 43 | GET | `/api/mcp/servers` | core/routes.py | `list_mcp_servers` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 44 | GET | `/api/mcp/status` | core/routes.py | `mcp_status` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 45 | POST | `/api/mcp/servers` | core/routes.py | `reload_mcp_servers_alias` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 46 | GET | `/api/database/configs` | core/routes.py | `list_db_configs` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 47 | POST | `/api/database/configs` | core/routes.py | `create_db_config` | Login required | authenticated | JSON | JSON | 否 | 未开始 |
| 48 | GET | `/api/database/configs/{config_id}` | core/routes.py | `get_db_config` | Login required | authenticated | Path | JSON | 否 | 未开始 |
| 49 | PUT | `/api/database/configs/{config_id}` | core/routes.py | `update_db_config` | Login required | authenticated | JSON+Path | JSON | 否 | 未开始 |
| 50 | DELETE | `/api/database/configs/{config_id}` | core/routes.py | `delete_db_config` | Login required | authenticated | Path | JSON | 否 | 未开始 |
| 51 | POST | `/api/database/configs/{config_id}/test` | core/routes.py | `test_db_config` | Login required | authenticated | Path | JSON | 否 | 未开始 |
| 52 | POST | `/api/database/configs/{config_id}/scan` | core/routes.py | `scan_table_metadata` | Login required | authenticated | Path | JSON | 否 | 未开始 |
| 53 | GET | `/api/database/configs/{config_id}/metadata` | core/routes.py | `get_table_metadata` | Login required | authenticated | Path | JSON | 否 | 未开始 |
| 54 | PUT | `/api/database/metadata/{meta_id}` | core/routes.py | `update_table_qa` | Login required | authenticated | JSON+Path | JSON | 否 | 未开始 |
| 55 | POST | `/api/smart-query` | core/routes.py | `smart_query` | Login required | authenticated | JSON | JSON | 否 | 未开始 |
| 56 | POST | `/api/smart-query/with-steps` | core/routes.py | `smart_query_with_steps` | Login required | authenticated | JSON | JSON | 否 | 未开始 |
| 57 | POST | `/api/smart-query/stream` | core/routes.py | `smart_query_stream` | Login required | authenticated | JSON | SSE | 是 | 未开始 |
| 58 | GET | `/api/command/status` | core/routes.py | `cmd_status` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 59 | GET | `/api/command/selfcheck` | core/routes.py | `cmd_selfcheck` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 60 | GET | `/api/command/cost` | core/routes.py | `cmd_cost` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 61 | GET | `/api/command/tools` | core/routes.py | `cmd_tools` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 62 | GET | `/api/command/knowledge` | core/routes.py | `cmd_knowledge` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 63 | POST | `/api/compact` | core/routes.py | `cmd_compact` | Login required | authenticated | JSON | JSON | 否 | 未开始 |
| 64 | GET | `/api/stats/tokens` | core/routes.py | `get_token_stats` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 65 | GET | `/api/skills` | core/routes.py | `list_skills` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 66 | GET | `/api/skills/{name}` | core/routes.py | `get_skill` | Login required | authenticated | Path | JSON | 否 | 未开始 |
| 67 | POST | `/api/skills/activate` | core/routes.py | `activate_skill` | Login required | authenticated | JSON | JSON | 否 | 未开始 |
| 68 | POST | `/api/skills/active` | core/routes.py | `get_active_skills` | Login required | authenticated | JSON | JSON | 否 | 未开始 |
| 69 | POST | `/api/skills/deactivate` | core/routes.py | `deactivate_skill` | Login required | authenticated | JSON | JSON | 否 | 未开始 |
| 70 | POST | `/api/skills/set` | core/routes.py | `set_active_skills` | Login required | authenticated | JSON | JSON | 否 | 未开始 |
| 71 | GET | `/api/stats/tools` | core/routes.py | `tool_stats` | Login required | authenticated | Query | JSON | 否 | 未开始 |
| 72 | GET | `/api/stats/cost` | core/routes.py | `cost_summary` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 73 | GET | `/api/stats/session/{session_id}` | core/routes.py | `session_cost` | Login required | authenticated | Path | JSON | 否 | 未开始 |
| 74 | POST | `/api/auth/login` | core/routes_auth.py | `login` | Public | anonymous | JSON | JSON | 否 | 未开始 |
| 75 | GET | `/api/auth/me` | core/routes_auth.py | `auth_me` | Login required | authenticated | 无 | JSON | 否 | 未开始 |
| 76 | GET | `/api/admin/users` | core/routes_admin.py | `admin_list_users` | Admin required | admin | 无 | JSON | 否 | 未开始 |
| 77 | POST | `/api/admin/users` | core/routes_admin.py | `admin_create_user` | Admin required | admin | JSON | JSON | 否 | 未开始 |
| 78 | DELETE | `/api/admin/users/{user_id}` | core/routes_admin.py | `admin_delete_user` | Admin required | admin | Path | JSON | 否 | 未开始 |
| 79 | PATCH | `/api/admin/users/{user_id}` | core/routes_admin.py | `admin_update_user` | Admin required | admin | JSON+Path | JSON | 否 | 未开始 |
| 80 | GET | `/api/admin/conversations` | core/routes_admin.py | `admin_list_all_conversations` | Admin required | admin | Query | JSON | 否 | 未开始 |
| 81 | GET | `/api/admin/conversations/{conv_id}` | core/routes_admin.py | `admin_get_conversation` | Admin required | admin | Path | JSON | 否 | 未开始 |
| 82 | GET | `/api/security/status` | core/routes_security.py | `security_status` | Public | anonymous | 无 | JSON | 否 | 未开始 |
| 83 | GET | `/api/security/audit/logs` | core/routes_security.py | `list_audit_logs` | Admin required | admin | Query | JSON | 否 | 未开始 |
| 84 | POST | `/api/work/configure` | core/wechat/work_bridge.py | `configure_work` | Public | anonymous | JSON | JSON | 否 | 未开始 |
| 85 | GET | `/api/work/status` | core/wechat/work_bridge.py | `work_status` | Public | anonymous | 无 | JSON | 否 | 未开始 |
| 86 | POST | `/api/work/callback` | core/wechat/work_bridge.py | `work_callback` | Public | anonymous | XML Body | XML | 否 | 未开始 |
| 87 | GET | `/api/work/callback` | core/wechat/work_bridge.py | `work_callback_verify` | Public | anonymous | Query | Text | 否 | 未开始 |

## 4. 接口详细说明

### 1. GET / — 首页

| 项目 | 值 |
|------|-----|
| 编号 | 1 |
| HTTP 方法 | GET |
| 完整 URL | `/` |
| Python 来源文件 | `main.py` |
| Python 函数名 | `home` |
| 路由前缀 | 无（直接注册到 app） |
| 是否需要登录 | 否（公开路径，列入 `_DEFAULT_PUBLIC_PREFIXES`） |
| 允许角色 | anonymous |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | 无 |
| Header 参数 | 无 |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | HTML 页面内容（`text/html`），从 `web/index.html` 读取并注入缓存版本号 `v={timestamp}` |
| 可能的 HTTP 状态码 | 200 |
| 可能的错误响应 | `FileNotFoundError` 时返回 `<h1>前端页面未找到</h1>`（状态码 200） |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 是（读取 `web/index.html`） |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 无 |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `HomeController.Index` |

### 2. POST /chat — 普通聊天

| 项目 | 值 |
|------|-----|
| 编号 | 2 |
| HTTP 方法 | POST |
| 完整 URL | `/chat` |
| Python 来源文件 | `main.py` |
| Python 函数名 | `chat` |
| 路由前缀 | 无（直接注册到 app） |
| 是否需要登录 | 是（不在公开前缀中，通过中间件鉴权） |
| 允许角色 | authenticated（admin / member / readonly） |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | 无 |
| Header 参数 | 无 |
| Body JSON | `{"query": "string (必填, min_length=1)", "session_id": "string (可选, 默认'')", "conversation_id": "string (可选, 默认'')", "files": ["string (可选)"]}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"reply": "回复文本", "session_id": "sid", "conversation_id": "conv_id"}` |
| 可能的 HTTP 状态码 | 200, 401, 422 |
| 可能的错误响应 | `422` — query 字段验证错误；`401` — 未登录 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（调用 `resolve_chat_ids` → `ensure_conversation_owned`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（`adapter.chat()`） |
| 当前安全注意事项 | 无 |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `ChatController.Chat` |

### 3. POST /chat/stream — 流式聊天

| 项目 | 值 |
|------|-----|
| 编号 | 3 |
| HTTP 方法 | POST |
| 完整 URL | `/chat/stream` |
| Python 来源文件 | `main.py` |
| Python 函数名 | `chat_stream` |
| 路由前缀 | 无 |
| 是否需要登录 | 是 |
| 允许角色 | authenticated |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | 无 |
| Header 参数 | 无 |
| Body JSON | `{"query": "string (必填)", "session_id": "", "conversation_id": "", "files": []}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | SSE 流，格式 `data: {json}\n\n`，每个事件是 Agent 返回的流式事件字符串 |
| 可能的 HTTP 状态码 | 200, 401, 422 |
| 可能的错误响应 | 同上 |
| 是否使用 SSE | 是，`media_type="text/event-stream"` |
| SSE 响应头 | `X-Session-Id`, `X-Conversation-Id` |
| 是否访问数据库 | 是（`resolve_chat_ids`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（`adapter.chat_stream()`） |
| 当前安全注意事项 | 无 |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `ChatController.ChatStream` |

### 4. POST /chat/stop — 停止聊天

| 项目 | 值 |
|------|-----|
| 编号 | 4 |
| HTTP 方法 | POST |
| 完整 URL | `/chat/stop` |
| Python 来源文件 | `main.py` |
| Python 函数名 | `stop_chat` |
| 路由前缀 | 无 |
| 是否需要登录 | 是 |
| 允许角色 | authenticated |
| 请求 Content-Type | `application/x-www-form-urlencoded` 或 Query |
| Path 参数 | 无 |
| Query 参数 | `session_id` (可选), `conversation_id` (可选) |
| Header 参数 | 无 |
| Body JSON | 无 |
| Form 参数 | 无（参数通过 Query 传入） |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "message": "已中断对话", "session_id": "sid"}` 或 `{"status": "warning", "message": "未找到正在执行的对话", "session_id": "sid"}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（`adapter.cancel_chat()`） |
| 当前安全注意事项 | 无 |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `ChatController.StopChat` |

### 5. POST /chat/reset — 重置聊天

| 项目 | 值 |
|------|-----|
| 编号 | 5 |
| HTTP 方法 | POST |
| 完整 URL | `/chat/reset` |
| Python 来源文件 | `main.py` |
| Python 函数名 | `reset_chat` |
| 路由前缀 | 无 |
| 是否需要登录 | 是 |
| 允许角色 | authenticated |
| 请求 Content-Type | `application/json` |
| Body JSON | `{"session_id": "string (可选, 默认'')"}` |
| 成功响应示例 | `{"status": "ok", "message": "会话已重置"}` |
| 是否调用 Agent | 是（`adapter.reset_session()`） |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `ChatController.ResetChat` |

### 6. GET /tools — 工具列表

| 编号 | 6 |
|------|-----|
| HTTP 方法 | GET |
| 完整 URL | `/tools` |
| Python 来源文件 | `main.py` |
| Python 函数名 | `list_tools` |
| 是否需要登录 | 是 |
| 允许角色 | authenticated |
| 请求 Content-Type | 无 |
| Query 参数 | `session_id` (可选) |
| 成功响应示例 | `{"tools": [{"name": "exec", "description": "...", "parameters": {...}, "category": "utility"}, ...]}` |
| 是否调用 Agent | 是（从 nanobot adapter 读取工具列表） |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `ToolsController.ListTools` |

### 7. GET /sessions — 会话统计

| 编号 | 7 |
|------|-----|
| HTTP 方法 | GET |
| 完整 URL | `/sessions` |
| Python 来源文件 | `main.py` |
| Python 函数名 | `get_sessions` |
| 是否需要登录 | 是 |
| 允许角色 | authenticated |
| 成功响应示例 | `{"total_sessions": 5, "sessions": [{"id": "...", "messages": 3, "created_at": "...", "updated_at": "..."}]}` |
| 是否调用 Agent | 是（读取 nanobot session manager） |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `SessionsController.GetSessions` |

### 8. GET /favicon.ico

| 编号 | 8 |
|------|-----|
| HTTP 方法 | GET |
| 完整 URL | `/favicon.ico` |
| Python 来源文件 | `main.py` |
| Python 函数名 | `favicon` |
| 是否需要登录 | 否（公开路径） |
| 响应 | 204 No Content |

### 9. GET /chat/mode — 聊天模式

| 编号 | 9 |
|------|-----|
| HTTP 方法 | GET |
| 完整 URL | `/chat/mode` |
| Python 函数名 | `chat_mode` |
| 是否需要登录 | 是 |
| Query 参数 | `session_id` (可选) |
| 成功响应示例 | `{"session_id": "...", "mode": "react"}` |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `ChatController.GetMode` |

### 10. GET /health — 健康检查

| 编号 | 10 |
|------|-----|
| HTTP 方法 | GET |
| 完整 URL | `/health` |
| Python 函数名 | `health` |
| 是否需要登录 | 否（公开路径） |
| 成功响应示例 | `{"status": "healthy", "engine": "nanobot"}` |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `HealthController.Check` |

### 11. GET /api/knowledge/documents

| 项目 | 值 |
|------|-----|
| 编号 | 11 |
| HTTP 方法 | GET |
| 完整 URL | `/api/knowledge/documents` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `list_documents` |
| 路由前缀 | `/api` |
| 是否需要登录 | 是 |
| 允许角色 | authenticated |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | 无 |
| Header 参数 | 无 |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"documents": [{"id": "...", "file_name": "...", "indexed_at": "2025-01-01 12:00", ...}]}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | 无 |
| 是否使用 SSE | 否 |
| 是否访问数据库 | 是（`db.list_documents()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 任何登录用户可查看所有已索引文档记录 |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `KnowledgeController.ListDocuments` |

### 12. POST /api/knowledge/index

| 项目 | 值 |
|------|-----|
| 编号 | 12 |
| HTTP 方法 | POST |
| 完整 URL | `/api/knowledge/index` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `index_document` |
| 是否需要登录 | 是 |
| Body JSON | `{"path": "string (必填)", "recursive": true (可选)}` |
| 成功响应示例 | 文件: `{"status": "ok", "type": "file", "result": {...}}` 或 目录: `{"status": "ok", "type": "directory", "result": {...}}` |
| 可能的 HTTP 状态码 | 200, 400, 403, 404, 500 |
| 是否访问数据库 | 是 |
| 是否访问文件系统 | 是（文件路径检查、文件存在性检查、文件类型支持检查） |
| 当前安全注意事项 | `check_path` 用于路径安全检查，审计日志记录 `api_knowledge_index` |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `KnowledgeController.IndexDocument` |

### 13. POST /api/knowledge/cancel

| 编号 | 13 |
|------|-----|
| 完整 URL | `/api/knowledge/cancel` |
| 成功响应示例 | `{"status": "ok", "message": "索引任务已取消"}` |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `KnowledgeController.CancelIndexing` |

### 14. GET /api/knowledge/is_indexing

| 编号 | 14 |
|------|-----|
| 成功响应示例 | `{"is_indexing": false}` |

### 15. POST /api/knowledge/clear

| 编号 | 15 |
|------|-----|
| 成功响应示例 | `{"status": "ok", "message": "已清空 N 个文档", "count": N}` |
| 安全提示 | 删除并重建向量集合，清除所有数据库记录 |

### 16. DELETE /api/knowledge/document/{doc_id}

| 编号 | 16 |
|------|-----|
| Path 参数 | `doc_id: str` |
| 成功响应示例 | `{"status": "ok", "message": "文档已删除"}` |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `KnowledgeController.DeleteDocument` |

### 17. GET /api/knowledge/search

| 编号 | 17 |
|------|-----|
| Query 参数 | `query: str (必填)`, `n: int (可选, 默认5)` |
| 成功响应示例 | `{"results": [{"id": "...", "content": "...", "score": 0.95, "source": "...", "file_path": "..."}], "total": N}` |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `KnowledgeController.Search` |

### 18. GET /api/knowledge/stats

| 编号 | 18 |
|------|-----|
| 成功响应示例 | `{"total_documents": N, "total_chunks": N, "vector_count": N, "by_type": {...}}` |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `KnowledgeController.Stats` |

### 19. GET /api/files/roots — 工作区入口

| 项目 | 值 |
|------|-----|
| 编号 | 19 |
| HTTP 方法 | GET |
| 完整 URL | `/api/files/roots` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `file_workspace_roots` |
| 路由前缀 | `/api` |
| 是否需要登录 | 是（`Depends(get_current_user)` — 若未登录返回 401） |
| 允许角色 | authenticated（admin / member / readonly） |
| Depends 依赖 | `user: CurrentUser = Depends(get_current_user)` |
| 成功响应示例 | workspace 模式: `{"mode": "workspace", "roots": [{"id": "...", "name": "...", "path": "...", "can_write": true}]}` 或 legacy 模式: `{"mode": "legacy", "roots": [...]}` |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `FilesController.GetRoots` |

### 20. GET /api/files/list — 列出目录内容

| 项目 | 值 |
|------|-----|
| 编号 | 20 |
| HTTP 方法 | GET |
| 完整 URL | `/api/files/list` |
| Python 函数名 | `list_files` |
| 是否需要登录 | 是（`Depends(get_current_user_optional)`，但中间件已要求登录，user 为 None 时自动再用 `get_current_user_optional` 读取） |
| 允许角色 | authenticated |
| Query 参数 | `path: str (可选, 默认"")` |
| Depends 依赖 | `user: CurrentUser \| None = Depends(get_current_user_optional)` |
| 成功响应示例 | `{"path": "...", "parent": "...", "items": [...], "total": N, "mode": "workspace", "display_path": "...", "can_write": true, "can_upload": true}` |
| 可能的 HTTP 状态码 | 200, 403, 404, 400 |
| 是否访问数据库 | 是（仅 enrich_dir_item 时读用户信息） |
| 是否访问文件系统 | 是（`os.listdir`, `os.stat`, `is_supported`） |
| 当前安全注意事项 | 使用 `_files_check_path` 做路径安全检查，审计日志记录 `api_files_list` |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `FilesController.ListFiles` |

### 21. GET /api/files/drives

| 编号 | 21 |
|------|-----|
| 完整 URL | `/api/files/drives` |
| Python 函数名 | `list_drives` |
| Depends 依赖 | `user: CurrentUser \| None = Depends(get_current_user_optional)` |
| 成功响应示例 | workspace: `{"mode": "workspace", "drives": [...]}` 或 legacy: `{"mode": "legacy", "drives": [...]}` |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `FilesController.ListDrives` |

### 22. GET /api/files/info

| 编号 | 22 |
|------|-----|
| Query 参数 | `path: str (必填)` |
| 成功响应示例 | `{"name": "...", "path": "...", "is_dir": false, "size": N, "modified": "...", "ext": "...", "is_supported": true, "category": "..."}` |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `FilesController.GetFileInfo` |

### 23. POST /api/files/open

| 编号 | 23 |
|------|-----|
| Depends 依赖 | `user: CurrentUser \| None = Depends(get_current_user_optional)` |
| Query 参数 | `path: str (必填)` |
| 成功响应示例 | `{"status": "ok", "message": "已打开: filename"}` |
| 注意 | 在服务器上用系统默认程序打开文件（OS 级别的 `start`/`open`/`xdg-open`） |
| 安全提示 | 可能存在安全风险——打开任意路径上的文件 |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `FilesController.OpenFile` |

### 24. POST /api/files/mkdir

| 编号 | 24 |
|------|-----|
| Body JSON | `{"path": "string (父目录绝对路径, 必填)", "name": "string (文件夹名, 1-128字符)"}` |
| Depends 依赖 | `user: CurrentUser = Depends(get_current_user)` |
| 成功响应示例 | `{"status": "ok", "path": "...", "name": "..."}` |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `FilesController.CreateDirectory` |

### 25. POST /api/files/upload

| 编号 | 25 |
|------|-----|
| 请求 Content-Type | `multipart/form-data` |
| Query 参数 | `path: str (目标目录, 必填)` |
| Form 参数 | `file: UploadFile (必填)` |
| Depends 依赖 | `user: CurrentUser = Depends(get_current_user)` |
| 成功响应示例 | `{"status": "ok", "file_name": "...", "file_path": "...", "size": N, "size_str": "...", "ext": "...", "is_supported": true}` |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `FilesController.UploadFile` |

### 26. GET /api/conversations — 对话列表

| 项目 | 值 |
|------|-----|
| 编号 | 26 |
| HTTP 方法 | GET |
| 完整 URL | `/api/conversations` |
| Python 函数名 | `list_conversations` |
| 路由前缀 | `/api` |
| 是否需要登录 | 是（`Depends(get_current_user)`） |
| Depends 依赖 | `user: CurrentUser = Depends(get_current_user)` |
| 成功响应示例 | `{"conversations": [{"id": "...", "title": "...", "created_at": "...", "updated_at": "...", "message_count": N, "owner_user_id": "..."}]}` |
| 数据库访问 | 是（`db.list_conversations()`） |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `ConversationsController.List` |

### 27. GET /api/conversations/{conv_id}

| 编号 | 27 |
|------|-----|
| Path 参数 | `conv_id: str` |
| Depends 依赖 | `user: CurrentUser = Depends(get_current_user)` |
| 成功响应示例 | `{"conversation": {...}, "messages": [...]}` 或 nanobot session 格式 |
| 错误 | `403` — 无权访问；`404` — 对话不存在 |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `ConversationsController.Get` |

### 28. POST /api/chat/load

| 编号 | 28 |
|------|-----|
| 参数 | `conv_id: str`, `session_id: str (可选)` — 均通过 Query 或 Form |
| 成功响应示例 | `{"status": "ok", "message": "会话切换完成"}` |
| 注意 | 该接口不做实际加载操作，仅返回成功 |

### 29. DELETE /api/conversations/{conv_id}

| 编号 | 29 |
|------|-----|
| Depends 依赖 | `user: CurrentUser = Depends(get_current_user)` |
| 成功响应示例 | `{"status": "ok", "message": "对话已删除"}` |
| 错误 | `403` — 无权访问；`404` — 对话不存在 |

### 30. GET /api/settings — 读取设置

| 项目 | 值 |
|------|-----|
| 编号 | 30 |
| 完整 URL | `/api/settings` |
| 是否需要登录 | 是（中间件鉴权，函数内读取 `request.state.user`） |
| 成功响应示例 | `{"config": {...}, "db_settings": {"model_type": "...", "ollama_url": "...", ...}}` |
| 数据库访问 | 是（`db.get_all_settings()`） |
| 文件系统访问 | 是（读取 `config.yaml`） |
| 安全提示 | 返回 `openai_api_key` 为空字符串但 `openai_api_key_configured` 指示是否已配置 |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `SettingsController.Get` |

### 31. POST /api/settings — 保存设置

| 编号 | 31 |
|------|-----|
| Body JSON | `{"settings": {"model_type": "ollama", ...}}` |
| 成功响应示例 | `{"status": "ok", "message": "设置已保存；..."}` |
| 安全提示 | API Key 存储到 `config.yaml`，通过 `persist_provider_api_key` 函数处理 |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `SettingsController.Update` |

### 32. POST /api/mcp/reload

| 编号 | 32 |
|------|-----|
| 成功响应示例 | `{"status": "ok", "filesystem_dirs": [...]}` |
| 是否调用 Agent | 是（重载 adapter 配置和 MCP 文件系统目录） |

### 33. GET /api/mcp/filesystem-dirs

| 编号 | 33 |
|------|-----|
| 成功响应示例 | `{"directories": [...]}` |

### 34. POST /api/models/test

| 编号 | 34 |
|------|-----|
| Body JSON | `{"model_type": "ollama", "base_url": "", "api_key": "", "model": ""}` |
| 成功响应示例 | `{"status": "ok", "message": "Ollama 连接成功"}` 或 `{"status": "error", "message": "..."}` |
| 注意 | 直接发起 HTTP 请求到配置的模型服务器 |

### 35. GET /api/ollama/check

| 编号 | 35 |
|------|-----|
| 成功响应示例 | `{"ollama_running": true, "models": ["qwen2.5:7b"], "default_model_ready": true, "message": ""}` |

### 36. POST /api/ollama/pull

| 编号 | 36 |
|------|-----|
| Body JSON | `{"model": "qwen2.5:7b"}` |
| 成功响应示例 | `{"status": "ok", "model": "...", "detail": "..."}` |

### 37. GET /api/tools/display

| 编号 | 37 |
|------|-----|
| 成功响应示例 | `{"tools": {"exec": "执行命令", "web_search": "网页搜索", ...}}` |

### 38. POST /api/upload — 文件上传

| 编号 | 38 |
|------|-----|
| 请求 Content-Type | `multipart/form-data` |
| Form 参数 | `file: UploadFile (必填)` |
| 成功响应示例 | `{"status": "ok", "file_name": "...", "file_path": "...", "size": N, "size_str": "...", "ext": "...", "is_supported": true}` |
| 安全提示 | 无文件类型校验，文件名安全化使用 `uuid_original` 模式 |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `UploadController.Upload` |

### 39. GET /api/upload/cleanup

| 编号 | 39 |
|------|-----|
| Query 参数 | `hours: int (可选, 默认24)` |
| 成功响应示例 | `{"status": "ok", "deleted": N}` |

### 40. GET /api/status — 系统状态

| 编号 | 40 |
|------|-----|
| 成功响应示例 | `{"ollama": {...}, "model": {...}, "knowledge": {...}, "sessions": 0, "version": "1.0.0.1-Beta"}` |

### 41. GET /api/debug/logs

| 编号 | 41 |
|------|-----|
| Query 参数 | `since: float (可选, 默认0)`, `limit: int (可选, 默认100)` |
| 成功响应示例 | `{"logs": [...]}` |

### 42. GET /api/debug/agent-state

| 编号 | 42 |
|------|-----|
| Query 参数 | `session_id: str (可选)` |
| 成功响应示例 | `{"session_id": "...", "memory_size": N, "messages": [...]}` |

### 43. GET /api/mcp/servers

| 编号 | 43 |
|------|-----|
| 成功响应示例 | `{"servers": [...], "error": "..."}` |

### 44. GET /api/mcp/status

| 编号 | 44 |
|------|-----|
| 成功响应示例 | `{"connected": [...], "error": "..."}` |

### 45. POST /api/mcp/servers — reload 别名

| 编号 | 45 |
|------|-----|
| 说明 | 与 `POST /api/mcp/reload` 行为相同 |

### 46. GET /api/database/configs

| 项目 | 值 |
|------|-----|
| 编号 | 46 |
| HTTP 方法 | GET |
| 完整 URL | `/api/database/configs` |
| Python 函数名 | `list_db_configs` |
| 是否需要登录 | 是 |
| 允许角色 | authenticated |
| 成功响应示例 | `{"configs": [...]}` |
| 漏洞 | 无鉴权控制——任何登录用户可查看所有数据库配置（含加密密码字段） |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `DatabaseConfigController.List` |

### 47. POST /api/database/configs

| 编号 | 47 |
|------|-----|
| Body JSON | `{"name": "...", "db_type": "mysql|postgresql", "host": "...", "port": 3306, "database_name": "...", "username": "...", "password": "..."}` |
| 成功响应示例 | `{"status": "ok", "config": {...}}` |
| 漏洞 | 任何登录用户可创建数据库配置——无管理员限制 |

### 48. GET /api/database/configs/{config_id}

| 编号 | 48 |
|------|-----|
| Path 参数 | `config_id: int` |
| 成功响应示例 | `{"config": {...}}`（不含 `password_encrypted`） |

### 49. PUT /api/database/configs/{config_id}

| 编号 | 49 |
|------|-----|
| Body JSON | 可更新字段: `name`, `host`, `port`, `database_name`, `username`, `password` |

### 50. DELETE /api/database/configs/{config_id}

| 编号 | 50 |
|------|-----|
| 成功响应示例 | `{"status": "ok"}` |

### 51. POST /api/database/configs/{config_id}/test

| 编号 | 51 |
|------|-----|
| 成功响应示例 | `{"status": "ok", "message": "..."}` 或 `{"status": "error", "message": "..."}` |

### 52. POST /api/database/configs/{config_id}/scan

| 编号 | 52 |
|------|-----|
| 成功响应示例 | `{"status": "ok", "message": "已扫描 N 个表", "count": N}` |

### 53. GET /api/database/configs/{config_id}/metadata

| 编号 | 53 |
|------|-----|
| 成功响应示例 | `{"metadata": [...]}` |

### 54. PUT /api/database/metadata/{meta_id}

| 编号 | 54 |
|------|-----|
| Path 参数 | `meta_id: int` |
| Body JSON | `{"qa_enabled": 1, "business_context": "..."}` |

### 55. POST /api/smart-query — 智能问数

| 项目 | 值 |
|------|-----|
| 编号 | 55 |
| HTTP 方法 | POST |
| 完整 URL | `/api/smart-query` |
| Python 函数名 | `smart_query` |
| Body JSON | `{"query": "string", "config_id": int}` |
| 成功响应示例 | `{"result": {...}}` |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `SmartQueryController.Query` |

### 56. POST /api/smart-query/with-steps

| 编号 | 56 |
|------|-----|
| Body JSON | `{"query": "string", "config_id": int}` |
| 成功响应示例 | `{"steps": [...], "result": {...}, "error": "..."}` |

### 57. POST /api/smart-query/stream — 流式智能问数

| 编号 | 57 |
|------|-----|
| Body JSON | `{"query": "string", "config_id": int}` |
| 响应 | SSE 流，每个事件 `data: {json}\n\n`，事件格式: `{"type": "step|result|error", ...}` |
| 是否使用 SSE | 是，`media_type="text/event-stream"` |

### 58-62. GET /api/command/* — 系统命令接口

| 编号 | 完整 URL | 函数名 | 描述 | 成功响应示例 |
|------|----------|--------|------|------------|
| 58 | `/api/command/status` | `cmd_status` | 系统状态概览 | `{"text": "━━━ 科吉系统状态 ..."}` |
| 59 | `/api/command/selfcheck` | `cmd_selfcheck` | 运行系统自检 | `{"text": "..."}` |
| 60 | `/api/command/cost` | `cmd_cost` | 会话统计 | `{"text": "..."}` |
| 61 | `/api/command/tools` | `cmd_tools` | 列出所有可用工具 | `{"text": "..."}` |
| 62 | `/api/command/knowledge` | `cmd_knowledge` | 知识库统计 | `{"text": "..."}` |

### 63. POST /api/compact — 会话压缩

| 项目 | 值 |
|------|-----|
| 编号 | 63 |
| HTTP 方法 | POST |
| 完整 URL | `/api/compact` |
| Python 函数名 | `cmd_compact` |
| Body JSON | `{"session_id": "string (必填)"}` |
| 成功响应示例 | `{"text": "...", "new_session_id": "..."}` |

### 64. GET /api/stats/tokens — Token 统计

| 项目 | 值 |
|------|-----|
| 编号 | 64 |
| 完整 URL | `/api/stats/tokens` |
| Python 函数名 | `get_token_stats` |
| 成功响应示例 | `{"total": {...}, "conversations": [...], "model": "...", "is_local": true}` |

### 65-70. /api/skills/* — 技能系统

| 编号 | 完整 URL | 方法 | 函数名 | 说明 |
|------|----------|------|--------|------|
| 65 | `/api/skills` | GET | `list_skills` | 列出所有可用技能 |
| 66 | `/api/skills/{name}` | GET | `get_skill` | 获取单个技能详情 |
| 67 | `/api/skills/activate` | POST | `activate_skill` | 激活一个技能 |
| 68 | `/api/skills/active` | POST | `get_active_skills` | 查询当前会话已激活的技能 |
| 69 | `/api/skills/deactivate` | POST | `deactivate_skill` | 卸载技能 |
| 70 | `/api/skills/set` | POST | `set_active_skills` | 批量设置当前会话的技能 |

### 71. GET /api/stats/tools

| 编号 | 71 |
|------|-----|
| Query 参数 | `days: int (可选, 默认7, 范围1-365)` |
| 成功响应示例 | 工具调用统计数据 |

### 72. GET /api/stats/cost

| 编号 | 72 |
|------|-----|
| 成功响应示例 | 今日、本月、全部的成本汇总 |

### 73. GET /api/stats/session/{session_id}

| 编号 | 73 |
|------|-----|
| Path 参数 | `session_id: str` |
| 成功响应示例 | 单个会话的成本 |

### 74. POST /api/auth/login — 登录

| 项目 | 值 |
|------|-----|
| 编号 | 74 |
| HTTP 方法 | POST |
| 完整 URL | `/api/auth/login` |
| Python 来源文件 | `core/routes_auth.py` |
| Python 函数名 | `login` |
| 路由前缀 | `/api/auth` |
| 是否需要登录 | 否（公开路径，列入 `_DEFAULT_PUBLIC_PREFIXES`） |
| 允许角色 | anonymous |
| 请求 Content-Type | `application/json` |
| Body JSON | `{"username": "string (必填, 1-64字符)", "password": "string (必填, 1-128字符)"}` |
| 成功响应示例 | `{"token": "jwt_token", "expires_in": 259200, "user": {"id": "...", "username": "...", "role": "admin", "display_name": "..."}}` |
| 可能的 HTTP 状态码 | 200, 401, 422 |
| 可能的错误响应 | `401` — `{"detail": "用户名或密码错误"}` |
| 是否访问数据库 | 是（`db.get_user_by_username()`） |
| 安全提示 | 返回的 token 默认有效期 72 小时 |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `AuthController.Login` |

### 75. GET /api/auth/me — 当前用户

| 项目 | 值 |
|------|-----|
| 编号 | 75 |
| HTTP 方法 | GET |
| 完整 URL | `/api/auth/me` |
| Python 来源文件 | `core/routes_auth.py` |
| Python 函数名 | `auth_me` |
| 是否需要登录 | 是（`Depends(get_current_user)`） |
| Depends 依赖 | `user: CurrentUser = Depends(get_current_user)` |
| 成功响应示例 | `{"user": {"id": "...", "username": "...", "role": "admin", "display_name": "..."}}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 账号已禁用或不存在 |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `AuthController.Me` |

### 76. GET /api/admin/users — 用户列表

| 项目 | 值 |
|------|-----|
| 编号 | 76 |
| 完整 URL | `/api/admin/users` |
| Python 来源文件 | `core/routes_admin.py` |
| Python 函数名 | `admin_list_users` |
| 是否需要登录 | 是 |
| 允许角色 | admin |
| Depends 依赖 | `_admin: CurrentUser = Depends(require_admin)` |
| 成功响应示例 | `{"users": [{"id": "...", "username": "...", "role": "member", ...}]}` |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `AdminController.ListUsers` |

### 77. POST /api/admin/users — 创建用户

| 编号 | 77 |
|------|-----|
| Body JSON | `{"username": "...", "password": "...", "role": "member", "display_name": ""}` |
| Depends 依赖 | `admin: CurrentUser = Depends(require_admin)` |
| 成功响应示例 | `{"status": "ok", "user": {...}}` |
| 错误 | `400` — 无效角色或用户名已存在 |

### 78. DELETE /api/admin/users/{user_id}

| 编号 | 78 |
|------|-----|
| Depends 依赖 | `admin: CurrentUser = Depends(require_admin)` |
| 限制 | 不能删除当前登录管理员；不能删除唯一管理员 |

### 79. PATCH /api/admin/users/{user_id}

| 编号 | 79 |
|------|-----|
| Body JSON | `{"display_name": "...", "role": "...", "is_active": true, "password": "..."}` |
| 限制 | 不能禁用当前登录管理员自己 |

### 80. GET /api/admin/conversations

| 编号 | 80 |
|------|-----|
| Query 参数 | `limit: int (默认100, 最大500)`, `user_id: str (可选, 按用户筛选)` |
| 成功响应示例 | `{"conversations": [...]}`（包含所有用户的对话，含 nanobot 会话合并） |

### 81. GET /api/admin/conversations/{conv_id}

| 编号 | 81 |
|------|-----|
| Path 参数 | `conv_id: str` |
| 说明 | 委托给 `core/routes.py` 中的 `get_conversation` |

### 82. GET /api/security/status — 安全状态

| 项目 | 值 |
|------|-----|
| 编号 | 82 |
| HTTP 方法 | GET |
| 完整 URL | `/api/security/status` |
| Python 来源文件 | `core/routes_security.py` |
| Python 函数名 | `security_status` |
| 路由前缀 | `/api/security` |
| 是否需要登录 | 否（公开路径，列入 `_DEFAULT_PUBLIC_PREFIXES`） |
| 允许角色 | anonymous |
| 成功响应示例 | `{"auth_enabled": true, "auth_mode": "both", "allow_localhost_without_auth": false, "authenticated": true, "audit_enabled": true, "user_login": true}` |
| 安全提示 | 公开暴露鉴权配置状态，含当前用户信息（若已登录） |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `SecurityController.Status` |

### 83. GET /api/security/audit/logs — 审计日志

| 项目 | 值 |
|------|-----|
| 编号 | 83 |
| HTTP 方法 | GET |
| 完整 URL | `/api/security/audit/logs` |
| Python 函数名 | `list_audit_logs` |
| 是否需要登录 | 是 |
| 允许角色 | admin |
| Depends 依赖 | `_admin: CurrentUser = Depends(require_admin)` |
| Query 参数 | `event_type: str (可选, tool_call\|file_access)`, `limit: int (默认100, 最大500)`, `offset: int (默认0)` |
| 成功响应示例 | `{"events": [...], "limit": 100, "offset": 0}` |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `SecurityController.AuditLogs` |

### 84. POST /api/work/configure — 企业微信配置

| 项目 | 值 |
|------|-----|
| 编号 | 84 |
| HTTP 方法 | POST |
| 完整 URL | `/api/work/configure` |
| Python 来源文件 | `core/wechat/work_bridge.py` |
| Python 函数名 | `configure_work` |
| 路由前缀 | `/api/work` |
| 是否需要登录 | 否（公开路径前缀 `/api/work`） |
| 允许角色 | anonymous |
| Body JSON | `{"corp_id": "string", "agent_id": "string", "corp_secret": "string"}` |
| 成功响应示例 | `{"status": "ok", "message": "配置已保存"}` |
| 安全提示 | **完全公开**——任何未认证用户可以配置企业微信参数 |
| 后续建议对应的 C# Controller 或 Endpoint 名称 | `WeChatWorkController.Configure` |

### 85. GET /api/work/status — 企业微信状态

| 编号 | 85 |
|------|-----|
| 成功响应示例 | `{"configured": true, "connected": true, "message": "..."}` 或 `{"configured": false}` |
| 安全提示 | 公开暴露企业微信连接状态 |

### 86. POST /api/work/callback — 企业微信回调

| 项目 | 值 |
|------|-----|
| 编号 | 86 |
| HTTP 方法 | POST |
| 完整 URL | `/api/work/callback` |
| Python 函数名 | `work_callback` |
| 请求 Content-Type | `application/xml`（企业微信 XML 格式） |
| Body | XML 格式的企业微信回调消息 |
| 响应 | `"ok"`（text/plain, 200） |
| 说明 | 解析 XML，提取 `MsgType`, `Content`, `FromUserName`，调用 Agent 处理并回复 |

### 87. GET /api/work/callback — 企业微信验证

| 编号 | 87 |
|------|-----|
| Query 参数 | `msg_signature`, `timestamp`, `nonce`, `echostr` |
| 响应 | 直接返回 `echostr` 明文（URL 验证） |

## 接口统计

### 按来源文件统计

| 文件 | 接口数量 |
|------|---------|
| `main.py` | 10 |
| `core/routes.py` | 63 |
| `core/routes_auth.py` | 2 |
| `core/routes_admin.py` | 6 |
| `core/routes_security.py` | 2 |
| `core/wechat/work_bridge.py` | 4 |
| **总计** | **87** |

### 核对

**第一次核对（装饰器数量）**：
- `main.py`：10 个 `@app.*` 装饰器
- `core/routes.py`：63 个 `@router.*` 装饰器
- `core/routes_auth.py`：2 个 `@router.*` 装饰器
- `core/routes_admin.py`：6 个 `@router.*` 装饰器
- `core/routes_security.py`：2 个 `@router.*` 装饰器
- `core/wechat/work_bridge.py`：4 个 `@router.*` 装饰器
- 合计：87 ✓

**第二次核对（接口编号）**：
- 编号范围：1-87
- 总计：87 ✓

**一致性检查**：总表行数 87 = 详细说明节数 87 = 装饰器计数 87 ✓
