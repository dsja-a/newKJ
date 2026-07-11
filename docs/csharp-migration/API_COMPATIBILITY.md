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
6. **鉴权关闭时**（`enabled=False`）：
   - `APIKeyMiddleware` 直接调用后续接口，不再执行鉴权逻辑
   - 中间件不会设置 `request.state.user`
   - 没有显式 Depends 依赖的接口可以匿名执行
   - `Depends(get_current_user)` 接口会因为 `request.state.user` 不存在而返回 401
   - `Depends(require_admin)` 同样无法获得当前用户并返回 401
   - `Depends(get_current_user_optional)` 会得到 `None`
   - `authenticate_request()` 自身可以直接调用时构造 `anonymous`/`admin`，但中间件关闭鉴权的直接放行分支**不会**调用 `authenticate_request()`
7. **角色系统**：`admin`、`member`、`readonly`
8. **角色限制**（在 `permissions.py` 中）：`readonly` 禁止写入类工具

> **安全注意事项**：
> - 鉴权关闭时，无显式身份依赖的接口可匿名执行；使用 get_current_user 或 require_admin 的接口仍会因为 request.state.user 不存在而返回 401；使用 get_current_user_optional 的接口会获得 None。
> - `allow_localhost_without_auth` 开启后本机请求完全跳过鉴权
> - `/api/security/status` 公开暴露鉴权配置状态和当前用户信息
> - 企业微信回调路由 `/api/work/*` 完全公开（含 `/api/work/configure` 配置接口）
> - `POST /api/upload` 和 `POST /api/files/upload` 上传不做文件类型校验

## 3. 接口总表

| 编号 | HTTP 方法 | 完整 URL | 来源文件 | 函数名 | 鉴权类型 | 允许身份和角色 | 请求类型 | 响应类型 | 是否流式 | Header 参数 | C# 迁移状态 |
|------|-----------|----------|----------|--------|----------|----------------|----------|----------|----------|-------------|------------|
| 1 | GET | `/` | main.py | `home` | Public | anonymous | 无 | HTML | 否 | 无 | 未开始 |
| 2 | POST | `/chat` | main.py | `chat` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | JSON | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 3 | POST | `/chat/stream` | main.py | `chat_stream` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | JSON | SSE | 是 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 4 | POST | `/chat/stop` | main.py | `stop_chat` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | Query | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 5 | POST | `/chat/reset` | main.py | `reset_chat` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | JSON | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 6 | GET | `/tools` | main.py | `list_tools` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 7 | GET | `/sessions` | main.py | `get_sessions` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 8 | GET | `/favicon.ico` | main.py | `favicon` | Public | anonymous | 无 | 204 | 否 | 无 | 未开始 |
| 9 | GET | `/chat/mode` | main.py | `chat_mode` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | Query | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 10 | GET | `/health` | main.py | `health` | Public | anonymous | 无 | JSON | 否 | 无 | 未开始 |
| 11 | GET | `/api/knowledge/documents` | core/routes.py | `list_documents` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 12 | POST | `/api/knowledge/index` | core/routes.py | `index_document` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | JSON | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 13 | POST | `/api/knowledge/cancel` | core/routes.py | `cancel_indexing` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 14 | GET | `/api/knowledge/is_indexing` | core/routes.py | `check_indexing` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 15 | POST | `/api/knowledge/clear` | core/routes.py | `clear_knowledge` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 16 | DELETE | `/api/knowledge/document/{doc_id}` | core/routes.py | `delete_document` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | Path | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 17 | GET | `/api/knowledge/search` | core/routes.py | `search_knowledge` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | Query | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 18 | GET | `/api/knowledge/stats` | core/routes.py | `knowledge_stats` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 19 | GET | `/api/files/roots` | core/routes.py | `file_workspace_roots` | CurrentUser dependency | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 20 | GET | `/api/files/list` | core/routes.py | `list_files` | Optional user dependency | JWT admin/member/readonly, API Key service/admin, localhost/admin（鉴权关闭时 user 为 None） | Query | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 21 | GET | `/api/files/drives` | core/routes.py | `list_drives` | Optional user dependency | JWT admin/member/readonly, API Key service/admin, localhost/admin（鉴权关闭时 user 为 None） | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 22 | GET | `/api/files/info` | core/routes.py | `file_info` | Optional user dependency | JWT admin/member/readonly, API Key service/admin, localhost/admin（鉴权关闭时 user 为 None） | Query | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 23 | POST | `/api/files/open` | core/routes.py | `open_file` | Optional user dependency | JWT admin/member/readonly, API Key service/admin, localhost/admin（鉴权关闭时 user 为 None） | Query | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 24 | POST | `/api/files/mkdir` | core/routes.py | `files_mkdir` | CurrentUser dependency | JWT admin/member/readonly, API Key service/admin, localhost/admin | JSON | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 25 | POST | `/api/files/upload` | core/routes.py | `files_upload` | CurrentUser dependency | JWT admin/member/readonly, API Key service/admin, localhost/admin | Multipart | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 26 | GET | `/api/conversations` | core/routes.py | `list_conversations` | CurrentUser dependency | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 27 | GET | `/api/conversations/{conv_id}` | core/routes.py | `get_conversation` | CurrentUser dependency | JWT admin/member/readonly, API Key service/admin, localhost/admin | Path | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 28 | POST | `/api/chat/load` | core/routes.py | `load_conversation` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | Form/Query | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 29 | DELETE | `/api/conversations/{conv_id}` | core/routes.py | `delete_conversation` | CurrentUser dependency | JWT admin/member/readonly, API Key service/admin, localhost/admin | Path | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 30 | GET | `/api/settings` | core/routes.py | `get_settings` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 31 | POST | `/api/settings` | core/routes.py | `update_settings` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | JSON | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 32 | POST | `/api/mcp/reload` | core/routes.py | `reload_mcp_servers` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 33 | GET | `/api/mcp/filesystem-dirs` | core/routes.py | `get_mcp_filesystem_dirs` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 34 | POST | `/api/models/test` | core/routes.py | `test_model_connection` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | JSON | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 35 | GET | `/api/ollama/check` | core/routes.py | `ollama_check` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 36 | POST | `/api/ollama/pull` | core/routes.py | `ollama_pull` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | JSON | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 37 | GET | `/api/tools/display` | core/routes.py | `get_tools_display` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 38 | POST | `/api/upload` | core/routes.py | `upload_file` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | Multipart | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 39 | GET | `/api/upload/cleanup` | core/routes.py | `cleanup_uploads` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | Query | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 40 | GET | `/api/status` | core/routes.py | `system_status` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 41 | GET | `/api/debug/logs` | core/routes.py | `debug_logs` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | Query | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 42 | GET | `/api/debug/agent-state` | core/routes.py | `debug_agent_state` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | Query | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 43 | GET | `/api/mcp/servers` | core/routes.py | `list_mcp_servers` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 44 | GET | `/api/mcp/status` | core/routes.py | `mcp_status` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 45 | POST | `/api/mcp/servers` | core/routes.py | `reload_mcp_servers_alias` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 46 | GET | `/api/database/configs` | core/routes.py | `list_db_configs` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 47 | POST | `/api/database/configs` | core/routes.py | `create_db_config` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | JSON | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 48 | GET | `/api/database/configs/{config_id}` | core/routes.py | `get_db_config` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | Path | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 49 | PUT | `/api/database/configs/{config_id}` | core/routes.py | `update_db_config` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | JSON+Path | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 50 | DELETE | `/api/database/configs/{config_id}` | core/routes.py | `delete_db_config` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | Path | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 51 | POST | `/api/database/configs/{config_id}/test` | core/routes.py | `test_db_config` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | Path | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 52 | POST | `/api/database/configs/{config_id}/scan` | core/routes.py | `scan_table_metadata` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | Path | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 53 | GET | `/api/database/configs/{config_id}/metadata` | core/routes.py | `get_table_metadata` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | Path | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 54 | PUT | `/api/database/metadata/{meta_id}` | core/routes.py | `update_table_qa` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | JSON+Path | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 55 | POST | `/api/smart-query` | core/routes.py | `smart_query` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | JSON | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 56 | POST | `/api/smart-query/with-steps` | core/routes.py | `smart_query_with_steps` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | JSON | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 57 | POST | `/api/smart-query/stream` | core/routes.py | `smart_query_stream` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | JSON | SSE | 是 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 58 | GET | `/api/command/status` | core/routes.py | `cmd_status` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 59 | GET | `/api/command/selfcheck` | core/routes.py | `cmd_selfcheck` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 60 | GET | `/api/command/cost` | core/routes.py | `cmd_cost` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 61 | GET | `/api/command/tools` | core/routes.py | `cmd_tools` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 62 | GET | `/api/command/knowledge` | core/routes.py | `cmd_knowledge` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 63 | POST | `/api/compact` | core/routes.py | `cmd_compact` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | JSON | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 64 | GET | `/api/stats/tokens` | core/routes.py | `get_token_stats` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 65 | GET | `/api/skills` | core/routes.py | `list_skills` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 66 | GET | `/api/skills/{name}` | core/routes.py | `get_skill` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | Path | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 67 | POST | `/api/skills/activate` | core/routes.py | `activate_skill` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | JSON | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 68 | POST | `/api/skills/active` | core/routes.py | `get_active_skills` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | JSON | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 69 | POST | `/api/skills/deactivate` | core/routes.py | `deactivate_skill` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | JSON | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 70 | POST | `/api/skills/set` | core/routes.py | `set_active_skills` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | JSON | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 71 | GET | `/api/stats/tools` | core/routes.py | `tool_stats` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | Query | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 72 | GET | `/api/stats/cost` | core/routes.py | `cost_summary` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 73 | GET | `/api/stats/session/{session_id}` | core/routes.py | `session_cost` | Protected by middleware | JWT admin/member/readonly, API Key service/admin, localhost/admin | Path | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 74 | POST | `/api/auth/login` | core/routes_auth.py | `login` | Public | anonymous | JSON | JSON | 否 | 无 | 未开始 |
| 75 | GET | `/api/auth/me` | core/routes_auth.py | `auth_me` | CurrentUser dependency | JWT admin/member/readonly, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 76 | GET | `/api/admin/users` | core/routes_admin.py | `admin_list_users` | Admin role dependency | JWT admin, API Key service/admin, localhost/admin | 无 | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 77 | POST | `/api/admin/users` | core/routes_admin.py | `admin_create_user` | Admin role dependency | JWT admin, API Key service/admin, localhost/admin | JSON | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 78 | DELETE | `/api/admin/users/{user_id}` | core/routes_admin.py | `admin_delete_user` | Admin role dependency | JWT admin, API Key service/admin, localhost/admin | Path | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 79 | PATCH | `/api/admin/users/{user_id}` | core/routes_admin.py | `admin_update_user` | Admin role dependency | JWT admin, API Key service/admin, localhost/admin | JSON+Path | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 80 | GET | `/api/admin/conversations` | core/routes_admin.py | `admin_list_all_conversations` | Admin role dependency | JWT admin, API Key service/admin, localhost/admin | Query | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 81 | GET | `/api/admin/conversations/{conv_id}` | core/routes_admin.py | `admin_get_conversation` | Admin role dependency | JWT admin, API Key service/admin, localhost/admin | Path | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 82 | GET | `/api/security/status` | core/routes_security.py | `security_status` | Public | anonymous | 无 | JSON | 否 | 无 | 未开始 |
| 83 | GET | `/api/security/audit/logs` | core/routes_security.py | `list_audit_logs` | Admin role dependency | JWT admin, API Key service/admin, localhost/admin | Query | JSON | 否 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` | 未开始 |
| 84 | POST | `/api/work/configure` | core/wechat/work_bridge.py | `configure_work` | Public | anonymous | JSON | JSON | 否 | 无 | 未开始 |
| 85 | GET | `/api/work/status` | core/wechat/work_bridge.py | `work_status` | Public | anonymous | 无 | JSON | 否 | 无 | 未开始 |
| 86 | POST | `/api/work/callback` | core/wechat/work_bridge.py | `work_callback` | Public | anonymous | XML Body | XML | 否 | 无 | 未开始 |
| 87 | GET | `/api/work/callback` | core/wechat/work_bridge.py | `work_callback_verify` | Public | anonymous | Query | Text | 否 | 无 | 未开始 |

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
| 鉴权类型 | Public |
| 是否需要账号登录 | 否（公开路径，列入 `_DEFAULT_PUBLIC_PREFIXES`） |
| 允许身份和角色 | anonymous |
| Depends 依赖 | 无 |
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
| 后续建议对应的 C# Endpoint 名称 | `HomeController.Index` |

### 2. POST /chat — 普通聊天

| 项目 | 值 |
|------|-----|
| 编号 | 2 |
| HTTP 方法 | POST |
| 完整 URL | `/chat` |
| Python 来源文件 | `main.py` |
| Python 函数名 | `chat` |
| 路由前缀 | 无（直接注册到 app） |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | `{"query": "string (必填, min_length=1)", "session_id": "string (可选, 默认'')", "conversation_id": "string (可选, 默认'')", "files": ["string (可选)"]}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"reply": "回复文本", "session_id": "sid", "conversation_id": "conv_id"}` |
| 可能的 HTTP 状态码 | 200, 401, 422 |
| 可能的错误响应 | `422` — query 字段验证错误；`401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（调用 `resolve_chat_ids` → `ensure_conversation_owned`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（`adapter.chat()`） |
| 当前安全注意事项 | 无 |
| 后续建议对应的 C# Endpoint 名称 | `ChatController.Chat` |

### 3. POST /chat/stream — 流式聊天

| 项目 | 值 |
|------|-----|
| 编号 | 3 |
| HTTP 方法 | POST |
| 完整 URL | `/chat/stream` |
| Python 来源文件 | `main.py` |
| Python 函数名 | `chat_stream` |
| 路由前缀 | 无（直接注册到 app） |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
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
| 后续建议对应的 C# Endpoint 名称 | `ChatController.ChatStream` |

### 4. POST /chat/stop — 停止聊天

| 项目 | 值 |
|------|-----|
| 编号 | 4 |
| HTTP 方法 | POST |
| 完整 URL | `/chat/stop` |
| Python 来源文件 | `main.py` |
| Python 函数名 | `stop_chat` |
| 路由前缀 | 无（直接注册到 app） |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无特殊要求，参数来自 Query |
| Path 参数 | 无 |
| Query 参数 | `conversation_id: str (可选, 默认"")`, `session_id: str (可选, 默认"", 用于取消指定会话)`, `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "message": "已中断对话", "session_id": "sid"}` 或 `{"status": "warning", "message": "未找到正在执行的对话", "session_id": "sid"}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（`adapter.cancel_chat(sid)`） |
| 当前安全注意事项 | **无会话所有权检查** — 传递任意 session_id 可取消其他用户的对话（仅根据 conversation_id 或 session_id 取消，不验证请求者身份） |
| 后续建议对应的 C# Endpoint 名称 | `ChatController.StopChat` |

### 5. POST /chat/reset — 重置聊天

| 项目 | 值 |
|------|-----|
| 编号 | 5 |
| HTTP 方法 | POST |
| 完整 URL | `/chat/reset` |
| Python 来源文件 | `main.py` |
| Python 函数名 | `reset_chat` |
| 路由前缀 | 无（直接注册到 app） |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | `{"session_id": "string (可选, 默认'')"}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "message": "会话已重置"}` |
| 可能的 HTTP 状态码 | 200, 401, 422 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（`adapter.reset_session(session_id)`） |
| 当前安全注意事项 | **无会话所有权检查** — 直接使用 Body 中的 session_id，不调用 resolve_chat_ids，未验证请求者身份，可能重置其他用户的会话 |
| 后续建议对应的 C# Endpoint 名称 | `ChatController.ResetChat` |

### 6. GET /tools — 工具列表

| 项目 | 值 |
|------|-----|
| 编号 | 6 |
| HTTP 方法 | GET |
| 完整 URL | `/tools` |
| Python 来源文件 | `main.py` |
| Python 函数名 | `list_tools` |
| 路由前缀 | 无（直接注册到 app） |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `session_id: str (可选, 默认"")`, `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | 返回 `adapter.tools._tools` 的完整注册表，包含每个工具的 name、description、parameters（JSON Schema）、category 等字段 |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（从 adapter.tools._tools 读取工具完整注册表） |
| 当前安全注意事项 | **无角色过滤** — 返回所有已注册工具（包括懒加载后暴露的隐藏工具），readonly 用户也能看到高风险的写类工具（如 exec、filesystem 等）；仅前端做展示过滤 |
| 后续建议对应的 C# Endpoint 名称 | `ToolsController.ListTools` |

### 7. GET /sessions — 会话统计

| 项目 | 值 |
|------|-----|
| 编号 | 7 |
| HTTP 方法 | GET |
| 完整 URL | `/sessions` |
| Python 来源文件 | `main.py` |
| Python 函数名 | `get_sessions` |
| 路由前缀 | 无（直接注册到 app） |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"total_sessions": 5, "sessions": [{"id": "...", "messages": 3, "created_at": "...", "updated_at": "..."}]}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（读取 nanobot session manager 的 `_cache`） |
| 当前安全注意事项 | **公开所有缓存的会话** — 直接从 `adapter.session_manager._cache` 读取，无用户隔离，任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可查看所有会话的消息数和时间戳 |
| 后续建议对应的 C# Endpoint 名称 | `SessionsController.GetSessions` |

### 8. GET /favicon.ico

| 项目 | 值 |
|------|-----|
| 编号 | 8 |
| HTTP 方法 | GET |
| 完整 URL | `/favicon.ico` |
| Python 来源文件 | `main.py` |
| Python 函数名 | `favicon` |
| 路由前缀 | 无（直接注册到 app） |
| 鉴权类型 | Public |
| 是否需要账号登录 | 否（公开路径，列入 `_DEFAULT_PUBLIC_PREFIXES`） |
| 允许身份和角色 | anonymous |
| Depends 依赖 | 无 |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | 无 |
| Header 参数 | 无 |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | 204 No Content（空响应体） |
| 可能的 HTTP 状态码 | 204 |
| 可能的错误响应 | 无 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 无 |
| 后续建议对应的 C# Endpoint 名称 | `HomeController.Favicon` |

### 9. GET /chat/mode — 聊天模式

| 项目 | 值 |
|------|-----|
| 编号 | 9 |
| HTTP 方法 | GET |
| 完整 URL | `/chat/mode` |
| Python 来源文件 | `main.py` |
| Python 函数名 | `chat_mode` |
| 路由前缀 | 无（直接注册到 app） |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `session_id: str (可选, 默认"")`, `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"session_id": "...", "mode": "react"}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 无 |
| 后续建议对应的 C# Endpoint 名称 | `ChatController.GetMode` |

### 10. GET /health — 健康检查

| 项目 | 值 |
|------|-----|
| 编号 | 10 |
| HTTP 方法 | GET |
| 完整 URL | `/health` |
| Python 来源文件 | `main.py` |
| Python 函数名 | `health` |
| 路由前缀 | 无（直接注册到 app） |
| 鉴权类型 | Public |
| 是否需要账号登录 | 否（公开路径，列入 `_DEFAULT_PUBLIC_PREFIXES`） |
| 允许身份和角色 | anonymous |
| Depends 依赖 | 无 |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | 无 |
| Header 参数 | 无 |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "healthy", "engine": "nanobot"}` |
| 可能的 HTTP 状态码 | 200 |
| 可能的错误响应 | 无 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 无 |
| 后续建议对应的 C# Endpoint 名称 | `HealthController.Check` |

### 11. GET /api/knowledge/documents — 文档列表

| 项目 | 值 |
|------|-----|
| 编号 | 11 |
| HTTP 方法 | GET |
| 完整 URL | `/api/knowledge/documents` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `list_documents` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"documents": [{"id": "...", "file_name": "...", "indexed_at": "2025-01-01 12:00", ...}]}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.list_documents()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可查看所有已索引文档记录 |
| 后续建议对应的 C# Endpoint 名称 | `KnowledgeController.ListDocuments` |

### 12. POST /api/knowledge/index — 索引文档

| 项目 | 值 |
|------|-----|
| 编号 | 12 |
| HTTP 方法 | POST |
| 完整 URL | `/api/knowledge/index` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `index_document` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | `{"path": "string (必填)", "recursive": true (可选, 默认true)}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | 文件: `{"status": "ok", "type": "file", "result": {...}}` 或 目录: `{"status": "ok", "type": "directory", "result": {...}}` |
| 可能的 HTTP 状态码 | 200, 400, 401, 403, 404, 500 |
| 可能的错误响应 | `400` — 不支持的文件类型；`403` — 路径安全检查失败；`404` — 路径不存在；`500` — 索引失败 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（索引后存储文档记录） |
| 是否访问文件系统 | 是（文件路径检查、文件存在性检查、文件类型支持检查） |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | `check_path` 用于路径安全检查，审计日志记录 `api_knowledge_index` |
| 后续建议对应的 C# Endpoint 名称 | `KnowledgeController.IndexDocument` |

### 13. POST /api/knowledge/cancel — 取消索引

| 项目 | 值 |
|------|-----|
| 编号 | 13 |
| HTTP 方法 | POST |
| 完整 URL | `/api/knowledge/cancel` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `cancel_indexing` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "message": "索引任务已取消"}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可取消索引任务，无管理员限制 |
| 后续建议对应的 C# Endpoint 名称 | `KnowledgeController.CancelIndexing` |

### 14. GET /api/knowledge/is_indexing — 检查索引状态

| 项目 | 值 |
|------|-----|
| 编号 | 14 |
| HTTP 方法 | GET |
| 完整 URL | `/api/knowledge/is_indexing` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `check_indexing` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"is_indexing": false}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 无 |
| 后续建议对应的 C# Endpoint 名称 | `KnowledgeController.CheckIndexing` |

### 15. POST /api/knowledge/clear — 清空知识库

| 项目 | 值 |
|------|-----|
| 编号 | 15 |
| HTTP 方法 | POST |
| 完整 URL | `/api/knowledge/clear` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `clear_knowledge` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "message": "已清空 N 个文档", "count": N}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（删除并重建向量集合 collection，清除 documents 表） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | **破坏性操作** — 任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可清空整个知识库（删除并重建向量集合 + 清除数据库记录），无管理员限制 |
| 后续建议对应的 C# Endpoint 名称 | `KnowledgeController.ClearKnowledge` |

### 16. DELETE /api/knowledge/document/{doc_id} — 删除文档

| 项目 | 值 |
|------|-----|
| 编号 | 16 |
| HTTP 方法 | DELETE |
| 完整 URL | `/api/knowledge/document/{doc_id}` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `delete_document` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | `doc_id: str` |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "message": "文档已删除"}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（删除向量库中的文档 + 数据库中的文档记录） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可删除任意文档，无用户级隔离 |
| 后续建议对应的 C# Endpoint 名称 | `KnowledgeController.DeleteDocument` |

### 17. GET /api/knowledge/search — 搜索知识库

| 项目 | 值 |
|------|-----|
| 编号 | 17 |
| HTTP 方法 | GET |
| 完整 URL | `/api/knowledge/search` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `search_knowledge` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `query: str (必填)`, `n: int (可选, 默认5, 返回结果数)`, `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"results": [{"id": "...", "content": "...", "score": 0.95, "source": "...", "file_path": "..."}], "total": N}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（向量搜索知识库） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可搜索所有知识库内容，无用户级隔离 |
| 后续建议对应的 C# Endpoint 名称 | `KnowledgeController.Search` |

### 18. GET /api/knowledge/stats — 知识库统计

| 项目 | 值 |
|------|-----|
| 编号 | 18 |
| HTTP 方法 | GET |
| 完整 URL | `/api/knowledge/stats` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `knowledge_stats` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"total_documents": N, "total_chunks": N, "vector_count": N, "by_type": {...}}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（查询文档统计和向量库计数） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 公开暴露知识库文档数量和向量规模 |
| 后续建议对应的 C# Endpoint 名称 | `KnowledgeController.Stats` |

### 19. GET /api/files/roots — 工作区入口

| 项目 | 值 |
|------|-----|
| 编号 | 19 |
| HTTP 方法 | GET |
| 完整 URL | `/api/files/roots` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `file_workspace_roots` |
| 路由前缀 | `/api` |
| 鉴权类型 | CurrentUser dependency |
| 是否需要账号登录 | 是（Depends(get_current_user) — 若未登录返回 401） |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | `user: CurrentUser = Depends(get_current_user)` |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | workspace 模式: `{"mode": "workspace", "roots": [{"id": "...", "name": "...", "path": "...", "can_write": true}]}` 或 legacy 模式: `{"mode": "legacy", "roots": [...]}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 是（检测工作区根目录和盘符） |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 无 |
| 后续建议对应的 C# Endpoint 名称 | `FilesController.GetRoots` |

### 20. GET /api/files/list — 列出目录内容

| 项目 | 值 |
|------|-----|
| 编号 | 20 |
| HTTP 方法 | GET |
| 完整 URL | `/api/files/list` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `list_files` |
| 路由前缀 | `/api` |
| 鉴权类型 | Optional user dependency |
| 是否需要账号登录 | 是（Depends(get_current_user_optional) — 未登录时 user 为 None） |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin（鉴权关闭时 user 为 None） |
| Depends 依赖 | `user: CurrentUser | None = Depends(get_current_user_optional)` |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `path: str (可选, 默认"")`, `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"path": "...", "parent": "...", "items": [...], "total": N, "mode": "workspace", "display_path": "...", "can_write": true, "can_upload": true}` |
| 可能的 HTTP 状态码 | 200, 400, 401, 403, 404 |
| 可能的错误响应 | `400` — 不是文件夹；`403` — 路径安全检查失败；`404` — 路径不存在 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是无（仅 enrich_dir_item 时读用户信息） |
| 是否访问文件系统 | 是（`os.listdir`, `os.stat`, `is_supported`） |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 使用 `_files_check_path` 做路径安全检查，审计日志记录 `api_files_list` |
| 后续建议对应的 C# Endpoint 名称 | `FilesController.ListFiles` |

### 21. GET /api/files/drives — 驱动器列表

| 项目 | 值 |
|------|-----|
| 编号 | 21 |
| HTTP 方法 | GET |
| 完整 URL | `/api/files/drives` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `list_drives` |
| 路由前缀 | `/api` |
| 鉴权类型 | Optional user dependency |
| 是否需要账号登录 | 是（Depends(get_current_user_optional) — 未登录时 user 为 None） |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin（鉴权关闭时 user 为 None） |
| Depends 依赖 | `user: CurrentUser | None = Depends(get_current_user_optional)` |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | workspace: `{"mode": "workspace", "drives": [{"name": "...", "path": "...", "label": "...", "id": "..."}]}` 或 legacy: `{"mode": "legacy", "drives": [{"name": "C:\\", "path": "C:\\", "label": "C:\\"}]}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件（中间件拒绝） |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 是（检测盘符和目录存在性） |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 无 |
| 后续建议对应的 C# Endpoint 名称 | `FilesController.ListDrives` |

### 22. GET /api/files/info — 文件信息

| 项目 | 值 |
|------|-----|
| 编号 | 22 |
| HTTP 方法 | GET |
| 完整 URL | `/api/files/info` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `file_info` |
| 路由前缀 | `/api` |
| 鉴权类型 | Optional user dependency |
| 是否需要账号登录 | 是（Depends(get_current_user_optional) — 未登录时 user 为 None） |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin（鉴权关闭时 user 为 None） |
| Depends 依赖 | `user: CurrentUser | None = Depends(get_current_user_optional)` |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `path: str (必填, 文件/目录路径)`, `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"name": "...", "path": "...", "is_dir": false, "size": N, "size_str": "...", "modified": "...", "created": "...", "ext": "...", "is_supported": true, "category": "..."}` |
| 可能的 HTTP 状态码 | 200, 401, 403, 404 |
| 可能的错误响应 | `403` — 路径安全检查失败；`404` — 文件不存在 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 是（`os.stat`, `get_file_metadata`） |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 使用 `_files_check_path` 做路径安全检查 |
| 后续建议对应的 C# Endpoint 名称 | `FilesController.GetFileInfo` |

### 23. POST /api/files/open — 打开文件

| 项目 | 值 |
|------|-----|
| 编号 | 23 |
| HTTP 方法 | POST |
| 完整 URL | `/api/files/open` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `open_file` |
| 路由前缀 | `/api` |
| 鉴权类型 | Optional user dependency |
| 是否需要账号登录 | 是（Depends(get_current_user_optional) — 未登录时 user 为 None） |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin（鉴权关闭时 user 为 None） |
| Depends 依赖 | `user: CurrentUser | None = Depends(get_current_user_optional)` |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `path: str (必填, 文件路径)`, `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "message": "已打开: filename"}` |
| 可能的 HTTP 状态码 | 200, 401, 403, 404, 400, 500 |
| 可能的错误响应 | `403` — 路径安全检查失败；`404` — 文件不存在；`400` — 不支持打开文件夹；`500` — 打开文件失败 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 是（在服务器上用系统默认程序打开文件） |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | **服务器端文件打开风险** — 在服务器上用 OS 级别 `start`/`open`/`xdg-open` 打开任意路径上的文件；审计日志记录 `api_files_open` |
| 后续建议对应的 C# Endpoint 名称 | `FilesController.OpenFile` |

### 24. POST /api/files/mkdir — 创建文件夹

| 项目 | 值 |
|------|-----|
| 编号 | 24 |
| HTTP 方法 | POST |
| 完整 URL | `/api/files/mkdir` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `files_mkdir` |
| 路由前缀 | `/api` |
| 鉴权类型 | CurrentUser dependency |
| 是否需要账号登录 | 是（Depends(get_current_user) — 若未登录返回 401） |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | `user: CurrentUser = Depends(get_current_user)` |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | `{"path": "string (父目录绝对路径, 必填)", "name": "string (文件夹名, 1-128字符)"}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "path": "...", "name": "..."}` |
| 可能的 HTTP 状态码 | 200, 401, 400, 403 |
| 可能的错误响应 | `400` — 文件夹名称无效或已存在；`403` — 路径安全检查失败或无写权限 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 是（`os.makedirs` 创建目录） |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 使用 `_files_check_path` 做路径检查（含 write=True 检查写权限） |
| 后续建议对应的 C# Endpoint 名称 | `FilesController.CreateDirectory` |

### 25. POST /api/files/upload — 文件上传到工作区

| 项目 | 值 |
|------|-----|
| 编号 | 25 |
| HTTP 方法 | POST |
| 完整 URL | `/api/files/upload` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `files_upload` |
| 路由前缀 | `/api` |
| 鉴权类型 | CurrentUser dependency |
| 是否需要账号登录 | 是（Depends(get_current_user) — 若未登录返回 401） |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | `user: CurrentUser = Depends(get_current_user)` |
| 请求 Content-Type | `multipart/form-data` |
| Path 参数 | 无 |
| Query 参数 | `path: str (目标目录, 必填)`, `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | `file: UploadFile (必填)` |
| 成功响应示例 | `{"status": "ok", "file_name": "...", "file_path": "...", "size": N, "size_str": "...", "ext": "...", "is_supported": true}` |
| 可能的 HTTP 状态码 | 200, 401, 403, 500 |
| 可能的错误响应 | `403` — 无写权限或路径安全检查失败；`500` — 上传失败 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 是（将上传文件写入工作区目录） |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 无文件类型校验；使用 UUID 前缀做文件名安全化；审计日志记录 `api_files_upload` |
| 后续建议对应的 C# Endpoint 名称 | `FilesController.UploadFile` |

### 26. GET /api/conversations — 对话列表

| 项目 | 值 |
|------|-----|
| 编号 | 26 |
| HTTP 方法 | GET |
| 完整 URL | `/api/conversations` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `list_conversations` |
| 路由前缀 | `/api` |
| 鉴权类型 | CurrentUser dependency |
| 是否需要账号登录 | 是（Depends(get_current_user) — 若未登录返回 401） |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | `user: CurrentUser = Depends(get_current_user)` |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"conversations": [{"id": "...", "title": "...", "created_at": "...", "updated_at": "...", "message_count": N, "owner_user_id": "..."}]}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.list_conversations()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 仅列出当前用户自己的对话（非管理员） |
| 后续建议对应的 C# Endpoint 名称 | `ConversationsController.List` |

### 27. GET /api/conversations/{conv_id} — 获取对话

| 项目 | 值 |
|------|-----|
| 编号 | 27 |
| HTTP 方法 | GET |
| 完整 URL | `/api/conversations/{conv_id}` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `get_conversation` |
| 路由前缀 | `/api` |
| 鉴权类型 | CurrentUser dependency |
| 是否需要账号登录 | 是（Depends(get_current_user) — 若未登录返回 401） |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | `user: CurrentUser = Depends(get_current_user)` |
| 请求 Content-Type | 无 |
| Path 参数 | `conv_id: str` |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"conversation": {...}, "messages": [...]}` 或 nanobot session 格式 |
| 可能的 HTTP 状态码 | 200, 401, 403, 404 |
| 可能的错误响应 | `403` — 无权访问该对话；`404` — 对话不存在 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.get_conversation()`, `db.get_messages()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 使用 `_assert_conv_access` 检查对话所有权（管理员可查看所有） |
| 后续建议对应的 C# Endpoint 名称 | `ConversationsController.Get` |

### 28. POST /api/chat/load — 加载对话

| 项目 | 值 |
|------|-----|
| 编号 | 28 |
| HTTP 方法 | POST |
| 完整 URL | `/api/chat/load` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `load_conversation` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `application/x-www-form-urlencoded` 或 Query |
| Path 参数 | 无 |
| Query 参数 | `conv_id: str (必填)`, `session_id: str (可选, 默认"")`, `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无（参数通过 Query 传入） |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "message": "会话切换完成"}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | **无实际操作** — 仅返回成功，不做任何加载或所有权验证 |
| 后续建议对应的 C# Endpoint 名称 | `ConversationsController.Load` |

### 29. DELETE /api/conversations/{conv_id} — 删除对话

| 项目 | 值 |
|------|-----|
| 编号 | 29 |
| HTTP 方法 | DELETE |
| 完整 URL | `/api/conversations/{conv_id}` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `delete_conversation` |
| 路由前缀 | `/api` |
| 鉴权类型 | CurrentUser dependency |
| 是否需要账号登录 | 是（Depends(get_current_user) — 若未登录返回 401） |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | `user: CurrentUser = Depends(get_current_user)` |
| 请求 Content-Type | 无 |
| Path 参数 | `conv_id: str` |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "message": "对话已删除"}` |
| 可能的 HTTP 状态码 | 200, 401, 403, 404 |
| 可能的错误响应 | `403` — 无权访问；`404` — 对话不存在 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.delete_conversation()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 使用 `_assert_conv_access` 检查对话所有权（管理员可删除所有） |
| 后续建议对应的 C# Endpoint 名称 | `ConversationsController.Delete` |

### 30. GET /api/settings — 读取设置

| 项目 | 值 |
|------|-----|
| 编号 | 30 |
| HTTP 方法 | GET |
| 完整 URL | `/api/settings` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `get_settings` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"config": {...}, "db_settings": {"model_type": "...", "ollama_url": "...", ...}}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.get_all_settings()`） |
| 是否访问文件系统 | 是（读取 `config.yaml`） |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 返回 `openai_api_key` 为空字符串但 `openai_api_key_configured` 指示是否已配置；公开模型配置、知识库参数等敏感信息 |
| 后续建议对应的 C# Endpoint 名称 | `SettingsController.Get` |

### 31. POST /api/settings — 保存设置

| 项目 | 值 |
|------|-----|
| 编号 | 31 |
| HTTP 方法 | POST |
| 完整 URL | `/api/settings` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `update_settings` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | `{"settings": {"model_type": "ollama", "ollama_url": "...", "openai_api_key": "...", ...}}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "message": "设置已保存；..."}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（保存用户偏好到 settings 表） |
| 是否访问文件系统 | 是（写入 `config.yaml`） |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | API Key 通过 `persist_provider_api_key` 存储到 `config.yaml`；任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可修改所有配置（含模型 API Key、知识库参数等）；无管理员限制 |
| 后续建议对应的 C# Endpoint 名称 | `SettingsController.Update` |

### 32. POST /api/mcp/reload — 重载 MCP

| 项目 | 值 |
|------|-----|
| 编号 | 32 |
| HTTP 方法 | POST |
| 完整 URL | `/api/mcp/reload` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `reload_mcp_servers` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "filesystem_dirs": [...]}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 是（重载磁盘上的 `config.yaml`） |
| 是否调用 Agent | 是（重载 adapter 配置和 MCP 文件系统目录） |
| 当前安全注意事项 | 任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可重载 MCP 配置，无管理员限制 |
| 后续建议对应的 C# Endpoint 名称 | `MCPController.Reload` |

### 33. GET /api/mcp/filesystem-dirs — MCP 文件系统目录

| 项目 | 值 |
|------|-----|
| 编号 | 33 |
| HTTP 方法 | GET |
| 完整 URL | `/api/mcp/filesystem-dirs` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `get_mcp_filesystem_dirs` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"directories": [...]}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 是（读取 `config.yaml` 中的 MCP 目录配置） |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 公开 MCP 文件系统允许目录列表 |
| 后续建议对应的 C# Endpoint 名称 | `MCPController.GetFilesystemDirs` |

### 34. POST /api/models/test — 测试模型连接

| 项目 | 值 |
|------|-----|
| 编号 | 34 |
| HTTP 方法 | POST |
| 完整 URL | `/api/models/test` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `test_model_connection` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | `{"model_type": "ollama", "base_url": "", "api_key": "", "model": ""}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "message": "Ollama 连接成功"}` 或 `{"status": "error", "message": "..."}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 直接发起 HTTP 请求到配置的模型服务器（可能暴露内网信息） |
| 后续建议对应的 C# Endpoint 名称 | `ModelsController.TestConnection` |

### 35. GET /api/ollama/check — 检查 Ollama

| 项目 | 值 |
|------|-----|
| 编号 | 35 |
| HTTP 方法 | GET |
| 完整 URL | `/api/ollama/check` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `ollama_check` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"ollama_running": true, "models": ["qwen2.5:7b"], "default_model_ready": true, "message": ""}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 无 |
| 后续建议对应的 C# Endpoint 名称 | `OllamaController.Check` |

### 36. POST /api/ollama/pull — 拉取模型

| 项目 | 值 |
|------|-----|
| 编号 | 36 |
| HTTP 方法 | POST |
| 完整 URL | `/api/ollama/pull` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `ollama_pull` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | `{"model": "qwen2.5:7b"}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "model": "...", "detail": "..."}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可触发模型下载（可能消耗大量带宽和磁盘空间） |
| 后续建议对应的 C# Endpoint 名称 | `OllamaController.Pull` |

### 37. GET /api/tools/display — 工具显示名

| 项目 | 值 |
|------|-----|
| 编号 | 37 |
| HTTP 方法 | GET |
| 完整 URL | `/api/tools/display` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `get_tools_display` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"tools": {"exec": "执行命令", "web_search": "网页搜索", ...}}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 公开可用工具清单和能力信息 |
| 后续建议对应的 C# Endpoint 名称 | `ToolsController.GetDisplay` |

### 38. POST /api/upload — 临时文件上传

| 项目 | 值 |
|------|-----|
| 编号 | 38 |
| HTTP 方法 | POST |
| 完整 URL | `/api/upload` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `upload_file` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `multipart/form-data` |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | `file: UploadFile (必填)` |
| 成功响应示例 | `{"status": "ok", "file_name": "...", "file_path": "...", "size": N, "size_str": "...", "ext": "...", "is_supported": true}` |
| 可能的 HTTP 状态码 | 200, 401, 500 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件；`500` — 文件上传失败 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 是（保存到 `data/uploads/` 临时目录） |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 无文件类型校验；文件名安全化使用 `uuid_original` 模式；审计日志记录 `api_upload` |
| 后续建议对应的 C# Endpoint 名称 | `UploadController.Upload` |

### 39. GET /api/upload/cleanup — 清理上传

| 项目 | 值 |
|------|-----|
| 编号 | 39 |
| HTTP 方法 | GET |
| 完整 URL | `/api/upload/cleanup` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `cleanup_uploads` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `hours: int (可选, 默认24)`, `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "deleted": N}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 是（删除临时上传目录中的过期文件） |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可触发清理临时文件 |
| 后续建议对应的 C# Endpoint 名称 | `UploadController.Cleanup` |

### 40. GET /api/status — 系统状态

| 项目 | 值 |
|------|-----|
| 编号 | 40 |
| HTTP 方法 | GET |
| 完整 URL | `/api/status` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `system_status` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"ollama": {...}, "model": {...}, "knowledge": {...}, "sessions": 0, "version": "1.0.0.1-Beta"}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（查询文档统计和设置） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 公开 Ollama 可用性、模型列表、知识库大小等信息 |
| 后续建议对应的 C# Endpoint 名称 | `SystemController.Status` |

### 41. GET /api/debug/logs — 调试日志

| 项目 | 值 |
|------|-----|
| 编号 | 41 |
| HTTP 方法 | GET |
| 完整 URL | `/api/debug/logs` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `debug_logs` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `since: float (可选, 默认0, Unix 时间戳)`, `limit: int (可选, 默认100)`, `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"logs": [...]}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 可能暴露系统内部状态和错误信息；任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可访问 |
| 后续建议对应的 C# Endpoint 名称 | `DebugController.Logs` |

### 42. GET /api/debug/agent-state — Agent 状态

| 项目 | 值 |
|------|-----|
| 编号 | 42 |
| HTTP 方法 | GET |
| 完整 URL | `/api/debug/agent-state` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `debug_agent_state` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `session_id: str (可选)`, `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"session_id": "...", "memory_size": N, "messages": [...]}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | **可能暴露任意会话的消息内容** — 传递任意 session_id 可读取该会话的消息历史（前500字符），无访问权限检查 |
| 后续建议对应的 C# Endpoint 名称 | `DebugController.AgentState` |

### 43. GET /api/mcp/servers — MCP 服务器列表

| 项目 | 值 |
|------|-----|
| 编号 | 43 |
| HTTP 方法 | GET |
| 完整 URL | `/api/mcp/servers` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `list_mcp_servers` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"servers": [...], "error": "..."}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 公开 MCP 服务器配置信息；使用同步事件循环（`new_event_loop` + `run_until_complete`） |
| 后续建议对应的 C# Endpoint 名称 | `MCPController.ListServers` |

### 44. GET /api/mcp/status — MCP 连接状态

| 项目 | 值 |
|------|-----|
| 编号 | 44 |
| HTTP 方法 | GET |
| 完整 URL | `/api/mcp/status` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `mcp_status` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"connected": [...], "error": "..."}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 公开 MCP 连接状态 |
| 后续建议对应的 C# Endpoint 名称 | `MCPController.Status` |

### 45. POST /api/mcp/servers — MCP 重载别名

| 项目 | 值 |
|------|-----|
| 编号 | 45 |
| HTTP 方法 | POST |
| 完整 URL | `/api/mcp/servers` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `reload_mcp_servers_alias` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | 与 `POST /api/mcp/reload` 相同 |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 是（委托给 `reload_mcp_servers`） |
| 是否调用 Agent | 是（委托给 `reload_mcp_servers`） |
| 当前安全注意事项 | 与 `POST /api/mcp/reload` 相同 |
| 后续建议对应的 C# Endpoint 名称 | `MCPController.ReloadAlias` |

### 46. GET /api/database/configs — 数据库配置列表

| 项目 | 值 |
|------|-----|
| 编号 | 46 |
| HTTP 方法 | GET |
| 完整 URL | `/api/database/configs` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `list_db_configs` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"configs": [...]}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.list_db_configs()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | **任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可查看所有数据库配置** — 含 `password_encrypted` 字段（未在列表接口中过滤），无管理员限制 |
| 后续建议对应的 C# Endpoint 名称 | `DatabaseConfigController.List` |

### 47. POST /api/database/configs — 创建数据库配置

| 项目 | 值 |
|------|-----|
| 编号 | 47 |
| HTTP 方法 | POST |
| 完整 URL | `/api/database/configs` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `create_db_config` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | `{"name": "...", "db_type": "mysql|postgresql", "host": "...", "port": 3306, "database_name": "...", "username": "...", "password": "..."}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "config": {...}}` |
| 可能的 HTTP 状态码 | 200, 400, 401 |
| 可能的错误响应 | `400` — 缺少必填字段 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.create_db_config()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | **任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可创建数据库配置** — 无管理员限制；密码使用基于机器名的 Fernet 加密存储 |
| 后续建议对应的 C# Endpoint 名称 | `DatabaseConfigController.Create` |

### 48. GET /api/database/configs/{config_id} — 获取数据库配置

| 项目 | 值 |
|------|-----|
| 编号 | 48 |
| HTTP 方法 | GET |
| 完整 URL | `/api/database/configs/{config_id}` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `get_db_config` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | `config_id: int` |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"config": {...}}`（不含 `password_encrypted` 字段） |
| 可能的 HTTP 状态码 | 200, 401, 404 |
| 可能的错误响应 | `404` — 配置不存在 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.get_db_config()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 返回配置信息但不含加密密码字段 |
| 后续建议对应的 C# Endpoint 名称 | `DatabaseConfigController.Get` |

### 49. PUT /api/database/configs/{config_id} — 更新数据库配置

| 项目 | 值 |
|------|-----|
| 编号 | 49 |
| HTTP 方法 | PUT |
| 完整 URL | `/api/database/configs/{config_id}` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `update_db_config` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `application/json` + Path |
| Path 参数 | `config_id: int` |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 可更新字段: `name`, `host`, `port`, `database_name`, `username`, `password` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok"}` |
| 可能的 HTTP 状态码 | 200, 401, 404 |
| 可能的错误响应 | `404` — 配置不存在 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.update_db_config()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可修改数据库连接参数和密码，无管理员限制 |
| 后续建议对应的 C# Endpoint 名称 | `DatabaseConfigController.Update` |

### 50. DELETE /api/database/configs/{config_id} — 删除数据库配置

| 项目 | 值 |
|------|-----|
| 编号 | 50 |
| HTTP 方法 | DELETE |
| 完整 URL | `/api/database/configs/{config_id}` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `delete_db_config` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | `config_id: int` |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok"}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.delete_db_config()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可删除数据库配置，无管理员限制 |
| 后续建议对应的 C# Endpoint 名称 | `DatabaseConfigController.Delete` |

### 51. POST /api/database/configs/{config_id}/test — 测试连接

| 项目 | 值 |
|------|-----|
| 编号 | 51 |
| HTTP 方法 | POST |
| 完整 URL | `/api/database/configs/{config_id}/test` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `test_db_config` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | `config_id: int` |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "message": "..."}` 或 `{"status": "error", "message": "..."}` |
| 可能的 HTTP 状态码 | 200, 401, 404 |
| 可能的错误响应 | `404` — 配置不存在 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（建立测试数据库连接） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可测试任意数据库配置的连接 |
| 后续建议对应的 C# Endpoint 名称 | `DatabaseConfigController.TestConnection` |

### 52. POST /api/database/configs/{config_id}/scan — 扫描表元数据

| 项目 | 值 |
|------|-----|
| 编号 | 52 |
| HTTP 方法 | POST |
| 完整 URL | `/api/database/configs/{config_id}/scan` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `scan_table_metadata` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | `config_id: int` |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "message": "已扫描 N 个表", "count": N}` |
| 可能的 HTTP 状态码 | 200, 401, 404, 500 |
| 可能的错误响应 | `404` — 配置不存在；`500` — 扫描失败 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（连接到外部数据库，扫描所有表的列、主键、外键和行数） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可触发扫描外部数据库，可能泄露数据库结构信息 |
| 后续建议对应的 C# Endpoint 名称 | `DatabaseConfigController.ScanMetadata` |

### 53. GET /api/database/configs/{config_id}/metadata — 获取表元数据

| 项目 | 值 |
|------|-----|
| 编号 | 53 |
| HTTP 方法 | GET |
| 完整 URL | `/api/database/configs/{config_id}/metadata` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `get_table_metadata` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | `config_id: int` |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"metadata": [...]}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（查询表元数据） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可查看数据库表结构元数据 |
| 后续建议对应的 C# Endpoint 名称 | `DatabaseConfigController.GetMetadata` |

### 54. PUT /api/database/metadata/{meta_id} — 更新表问答设置

| 项目 | 值 |
|------|-----|
| 编号 | 54 |
| HTTP 方法 | PUT |
| 完整 URL | `/api/database/metadata/{meta_id}` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `update_table_qa` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `application/json` + Path |
| Path 参数 | `meta_id: int` |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | `{"qa_enabled": 1, "business_context": "..."}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok"}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.update_table_qa()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可修改表问答配置 |
| 后续建议对应的 C# Endpoint 名称 | `DatabaseConfigController.UpdateTableQA` |

### 55. POST /api/smart-query — 智能问数

| 项目 | 值 |
|------|-----|
| 编号 | 55 |
| HTTP 方法 | POST |
| 完整 URL | `/api/smart-query` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `smart_query` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | `{"query": "string", "config_id": int}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"result": {...}}` |
| 可能的 HTTP 状态码 | 200, 400, 401 |
| 可能的错误响应 | `400` — 缺少参数；`401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（通过 `smart_query_service` 查询外部数据库） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（使用 LLM 进行 NL2SQL 转换和执行） |
| 当前安全注意事项 | 任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可使用任意数据库配置执行智能查询 |
| 后续建议对应的 C# Endpoint 名称 | `SmartQueryController.Query` |

### 56. POST /api/smart-query/with-steps — 智能问数（带步骤）

| 项目 | 值 |
|------|-----|
| 编号 | 56 |
| HTTP 方法 | POST |
| 完整 URL | `/api/smart-query/with-steps` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `smart_query_with_steps` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | `{"query": "string", "config_id": int}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"steps": [...], "result": {...}, "error": "..."}` |
| 可能的 HTTP 状态码 | 200, 400, 401 |
| 可能的错误响应 | `400` — 缺少参数；`401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（通过 `smart_query_service` 查询外部数据库） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（使用 LLM 进行 NL2SQL 转换和执行） |
| 当前安全注意事项 | 任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可使用任意数据库配置执行智能查询 |
| 后续建议对应的 C# Endpoint 名称 | `SmartQueryController.QueryWithSteps` |

### 57. POST /api/smart-query/stream — 流式智能问数

| 项目 | 值 |
|------|-----|
| 编号 | 57 |
| HTTP 方法 | POST |
| 完整 URL | `/api/smart-query/stream` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `smart_query_stream` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | `{"query": "string", "config_id": int}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | SSE 流，每个事件 `data: {json}\n\n`，事件格式: `{"type": "step|result|error", ...}` |
| 可能的 HTTP 状态码 | 200, 400, 401 |
| 可能的错误响应 | `400` — 缺少参数；`401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 是，`media_type="text/event-stream"` |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（通过 `smart_query_service` 查询外部数据库） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（使用 LLM 进行 NL2SQL 转换和执行） |
| 当前安全注意事项 | 任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可使用任意数据库配置执行流式智能查询 |
| 后续建议对应的 C# Endpoint 名称 | `SmartQueryController.QueryStream` |

### 58. GET /api/command/status — 系统状态概览

| 项目 | 值 |
|------|-----|
| 编号 | 58 |
| HTTP 方法 | GET |
| 完整 URL | `/api/command/status` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `cmd_status` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"text": "━━━ 科吉系统状态 ...\n模型: ...\n工具总数: N\n最大工具轮次: N"}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（读取 adapter 的工具列表和模型信息） |
| 当前安全注意事项 | 公开系统配置信息（模型、工具数量等） |
| 后续建议对应的 C# Endpoint 名称 | `CommandController.Status` |

### 59. GET /api/command/selfcheck — 系统自检

| 项目 | 值 |
|------|-----|
| 编号 | 59 |
| HTTP 方法 | GET |
| 完整 URL | `/api/command/selfcheck` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `cmd_selfcheck` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"text": "自检报告文本..."}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（运行 SelfCheckRunner） |
| 当前安全注意事项 | 可能暴露系统内部状态信息 |
| 后续建议对应的 C# Endpoint 名称 | `CommandController.SelfCheck` |

### 60. GET /api/command/cost — 会话统计

| 项目 | 值 |
|------|-----|
| 编号 | 60 |
| HTTP 方法 | GET |
| 完整 URL | `/api/command/cost` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `cmd_cost` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"text": "━━━ 会话统计 ...\n总会话数: N\nmax_tool_rounds: N\nProvider: ..."}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（读取 adapter 的 session manager） |
| 当前安全注意事项 | 公开会话数量和 Provider 信息 |
| 后续建议对应的 C# Endpoint 名称 | `CommandController.Cost` |

### 61. GET /api/command/tools — 工具列表（分类）

| 项目 | 值 |
|------|-----|
| 编号 | 61 |
| HTTP 方法 | GET |
| 完整 URL | `/api/command/tools` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `cmd_tools` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"text": "━━━ 可用工具 (N) ─────\n\n内置工具:\n  - exec\n..."}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（读取 adapter 工具列表） |
| 当前安全注意事项 | 公开所有可用工具清单（含 MCP 工具命名空间） |
| 后续建议对应的 C# Endpoint 名称 | `CommandController.Tools` |

### 62. GET /api/command/knowledge — 知识库统计（文字版）

| 项目 | 值 |
|------|-----|
| 编号 | 62 |
| HTTP 方法 | GET |
| 完整 URL | `/api/command/knowledge` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `cmd_knowledge` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"text": "━━━ 知识库统计 ...\n已索引文档: N\n向量块数: N\n最近文档:\n  - ..."}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（查询文档列表和向量库计数） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 公开知识库文档数量和最近文档名称 |
| 后续建议对应的 C# Endpoint 名称 | `CommandController.Knowledge` |

### 63. POST /api/compact — 会话压缩

| 项目 | 值 |
|------|-----|
| 编号 | 63 |
| HTTP 方法 | POST |
| 完整 URL | `/api/compact` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `cmd_compact` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | `{"session_id": "string (必填)"}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"text": "...", "new_session_id": "..."}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 是（读取 nanobot session 文件） |
| 是否调用 Agent | 是（调用 LLM 总结会话历史并创建新会话） |
| 当前安全注意事项 | 无 session 所有权检查——传递任意 session_id 即可压缩 |
| 后续建议对应的 C# Endpoint 名称 | `CompactController.Compact` |

### 64. GET /api/stats/tokens — Token 统计

| 项目 | 值 |
|------|-----|
| 编号 | 64 |
| HTTP 方法 | GET |
| 完整 URL | `/api/stats/tokens` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `get_token_stats` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"total": {"conversations": N, "prompt_tokens": N, "completion_tokens": N, "total_tokens": N, "cached_tokens": N, "cost": N}, "conversations": [...], "model": "...", "is_local": true}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（查询会话 LLM usage 数据） |
| 是否访问文件系统 | 是（读取 nanobot session 文件） |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 公开所有会话的 token 消耗和费用估算（含模型定价信息） |
| 后续建议对应的 C# Endpoint 名称 | `StatsController.TokenStats` |

### 65. GET /api/skills — 技能列表

| 项目 | 值 |
|------|-----|
| 编号 | 65 |
| HTTP 方法 | GET |
| 完整 URL | `/api/skills` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `list_skills` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"skills": [{"name": "...", "description": "...", "version": "...", "category": "...", "instructions": "...", "active": false}]}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 公开所有注册技能的指令内容 |
| 后续建议对应的 C# Endpoint 名称 | `SkillsController.List` |

### 66. GET /api/skills/{name} — 技能详情

| 项目 | 值 |
|------|-----|
| 编号 | 66 |
| HTTP 方法 | GET |
| 完整 URL | `/api/skills/{name}` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `get_skill` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | `name: str` |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"name": "...", "description": "...", "version": "...", "category": "...", "instructions": "..."}` |
| 可能的 HTTP 状态码 | 200, 401, 404 |
| 可能的错误响应 | `404` — 技能不存在；`401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 公开单个技能的完整指令 |
| 后续建议对应的 C# Endpoint 名称 | `SkillsController.Get` |

### 67. POST /api/skills/activate — 激活技能

| 项目 | 值 |
|------|-----|
| 编号 | 67 |
| HTTP 方法 | POST |
| 完整 URL | `/api/skills/activate` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `activate_skill` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | `{"session_id": "string (必填)", "skill_name": "string (必填)"}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "message": "已激活技能: skill_name", "active_skills": [...]}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（修改 adapter 的 `_active_skills`） |
| 当前安全注意事项 | 任何通过中间件的身份，包括 JWT 用户、API Key service/admin 和配置允许的 localhost/admin可为任意 session_id 激活技能（无 session 所有权检查） |
| 后续建议对应的 C# Endpoint 名称 | `SkillsController.Activate` |

### 68. POST /api/skills/active — 查询已激活技能

| 项目 | 值 |
|------|-----|
| 编号 | 68 |
| HTTP 方法 | POST |
| 完整 URL | `/api/skills/active` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `get_active_skills` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | `{"session_id": "string (必填)"}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"active_skills": [...]}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（调用 `_ensure_default_skills` 和读取 `_active_skills`） |
| 当前安全注意事项 | 无 session 所有权检查 |
| 后续建议对应的 C# Endpoint 名称 | `SkillsController.GetActive` |

### 69. POST /api/skills/deactivate — 卸载技能

| 项目 | 值 |
|------|-----|
| 编号 | 69 |
| HTTP 方法 | POST |
| 完整 URL | `/api/skills/deactivate` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `deactivate_skill` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | `{"session_id": "string (必填)", "skill_name": "string (可选, 不指定则卸载全部)"}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "message": "已卸载技能: skill_name"}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（修改 adapter 的 `_active_skills`） |
| 当前安全注意事项 | 无 session 所有权检查 |
| 后续建议对应的 C# Endpoint 名称 | `SkillsController.Deactivate` |

### 70. POST /api/skills/set — 批量设置技能

| 项目 | 值 |
|------|-----|
| 编号 | 70 |
| HTTP 方法 | POST |
| 完整 URL | `/api/skills/set` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `set_active_skills` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | `{"session_id": "string (必填)", "skills": ["string", ...]}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "active_skills": [...]}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（修改 adapter 的 `_active_skills` 和 `_skill_notified`） |
| 当前安全注意事项 | 无 session 所有权检查 |
| 后续建议对应的 C# Endpoint 名称 | `SkillsController.SetActive` |

### 71. GET /api/stats/tools — 工具调用统计

| 项目 | 值 |
|------|-----|
| 编号 | 71 |
| HTTP 方法 | GET |
| 完整 URL | `/api/stats/tools` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `tool_stats` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `days: int (可选, 默认7, 范围1-365)`, `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | 工具调用统计数据（按工具名汇总） |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.get_tool_stats()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 公开所有用户的工具调用统计 |
| 后续建议对应的 C# Endpoint 名称 | `StatsController.ToolStats` |

### 72. GET /api/stats/cost — 费用汇总

| 项目 | 值 |
|------|-----|
| 编号 | 72 |
| HTTP 方法 | GET |
| 完整 URL | `/api/stats/cost` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `cost_summary` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | 今日、本月、全部的成本汇总 |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.get_cost_summary()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 公开所有用户的费用统计 |
| 后续建议对应的 C# Endpoint 名称 | `StatsController.CostSummary` |

### 73. GET /api/stats/session/{session_id} — 会话费用

| 项目 | 值 |
|------|-----|
| 编号 | 73 |
| HTTP 方法 | GET |
| 完整 URL | `/api/stats/session/{session_id}` |
| Python 来源文件 | `core/routes.py` |
| Python 函数名 | `session_cost` |
| 路由前缀 | `/api` |
| 鉴权类型 | Protected by middleware |
| 是否需要账号登录 | 否，不强制账号登录；JWT、API Key 或配置允许的 localhost 身份均可访问 |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | 无（通过 APIKeyMiddleware 鉴权，request.state.user 由中间件设置，无显式 Depends） |
| 请求 Content-Type | 无 |
| Path 参数 | `session_id: str` |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | 单个会话的成本数据 |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 未提供有效 JWT、API Key，且不满足允许的 localhost 跳过条件 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.get_session_cost()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 无会话所有权检查——传递任意 session_id 可查看其他会话的费用 |
| 后续建议对应的 C# Endpoint 名称 | `StatsController.SessionCost` |

### 74. POST /api/auth/login — 登录

| 项目 | 值 |
|------|-----|
| 编号 | 74 |
| HTTP 方法 | POST |
| 完整 URL | `/api/auth/login` |
| Python 来源文件 | `core/routes_auth.py` |
| Python 函数名 | `login` |
| 路由前缀 | `/api/auth` |
| 鉴权类型 | Public |
| 是否需要账号登录 | 否（公开路径，列入 `_DEFAULT_PUBLIC_PREFIXES`） |
| 允许身份和角色 | anonymous |
| Depends 依赖 | 无 |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | 无 |
| Header 参数 | 无 |
| Body JSON | `{"username": "string (必填, 1-64字符)", "password": "string (必填, 1-128字符)"}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"token": "jwt_token", "expires_in": 259200, "user": {"id": "...", "username": "...", "role": "admin", "display_name": "..."}}` |
| 可能的 HTTP 状态码 | 200, 401, 422 |
| 可能的错误响应 | `401` — `{"detail": "用户名或密码错误"}` |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.get_user_by_username()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 返回的 token 默认有效期 72 小时；登录成功后设置 `request.state.user` |
| 后续建议对应的 C# Endpoint 名称 | `AuthController.Login` |

### 75. GET /api/auth/me — 当前用户

| 项目 | 值 |
|------|-----|
| 编号 | 75 |
| HTTP 方法 | GET |
| 完整 URL | `/api/auth/me` |
| Python 来源文件 | `core/routes_auth.py` |
| Python 函数名 | `auth_me` |
| 路由前缀 | `/api/auth` |
| 鉴权类型 | CurrentUser dependency |
| 是否需要账号登录 | 是（Depends(get_current_user) — 若未登录返回 401） |
| 允许身份和角色 | JWT admin/member/readonly, API Key service/admin, localhost/admin |
| Depends 依赖 | `user: CurrentUser = Depends(get_current_user)` |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"user": {"id": "...", "username": "...", "role": "admin", "display_name": "..."}}` |
| 可能的 HTTP 状态码 | 200, 401 |
| 可能的错误响应 | `401` — 账号已禁用或不存在 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.get_user_by_id()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 无 |
| 后续建议对应的 C# Endpoint 名称 | `AuthController.Me` |

### 76. GET /api/admin/users — 用户列表

| 项目 | 值 |
|------|-----|
| 编号 | 76 |
| HTTP 方法 | GET |
| 完整 URL | `/api/admin/users` |
| Python 来源文件 | `core/routes_admin.py` |
| Python 函数名 | `admin_list_users` |
| 路由前缀 | `/api/admin` |
| 鉴权类型 | Admin role dependency |
| 是否需要账号登录 | 是（Depends(require_admin) — 非管理员返回 403） |
| 允许身份和角色 | JWT admin, API Key service/admin, localhost/admin |
| Depends 依赖 | `user: CurrentUser = Depends(require_admin)` |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"users": [{"id": "...", "username": "...", "role": "member", ...}]}` |
| 可能的 HTTP 状态码 | 200, 401, 403 |
| 可能的错误响应 | `403` — 非管理员 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.list_users()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 仅管理员可访问，返回的用户信息不包含密码哈希（`user_to_public`） |
| 后续建议对应的 C# Endpoint 名称 | `AdminController.ListUsers` |

### 77. POST /api/admin/users — 创建用户

| 项目 | 值 |
|------|-----|
| 编号 | 77 |
| HTTP 方法 | POST |
| 完整 URL | `/api/admin/users` |
| Python 来源文件 | `core/routes_admin.py` |
| Python 函数名 | `admin_create_user` |
| 路由前缀 | `/api/admin` |
| 鉴权类型 | Admin role dependency |
| 是否需要账号登录 | 是（Depends(require_admin) — 非管理员返回 403） |
| 允许身份和角色 | JWT admin, API Key service/admin, localhost/admin |
| Depends 依赖 | `user: CurrentUser = Depends(require_admin)` |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | `{"username": "string (必填, 2-64字符)", "password": "string (必填, 6-128字符)", "role": "member (默认)", "display_name": ""}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "user": {...}}` |
| 可能的 HTTP 状态码 | 200, 400, 401, 403 |
| 可能的错误响应 | `400` — 无效角色或用户名已存在；`403` — 非管理员 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.create_user()`, `db.get_user_by_username()`） |
| 是否访问文件系统 | 是（`ensure_user_dir()` 创建用户工作目录） |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 无 |
| 后续建议对应的 C# Endpoint 名称 | `AdminController.CreateUser` |

### 78. DELETE /api/admin/users/{user_id} — 删除用户

| 项目 | 值 |
|------|-----|
| 编号 | 78 |
| HTTP 方法 | DELETE |
| 完整 URL | `/api/admin/users/{user_id}` |
| Python 来源文件 | `core/routes_admin.py` |
| Python 函数名 | `admin_delete_user` |
| 路由前缀 | `/api/admin` |
| 鉴权类型 | Admin role dependency |
| 是否需要账号登录 | 是（Depends(require_admin) — 非管理员返回 403） |
| 允许身份和角色 | JWT admin, API Key service/admin, localhost/admin |
| Depends 依赖 | `user: CurrentUser = Depends(require_admin)` |
| 请求 Content-Type | 无 |
| Path 参数 | `user_id: str` |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "message": "用户已删除"}` |
| 可能的 HTTP 状态码 | 200, 400, 401, 403, 404 |
| 可能的错误响应 | `400` — 不能删除当前登录管理员或唯一管理员；`404` — 用户不存在；`403` — 非管理员 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.delete_user()`, `db.list_users()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 不能删除当前登录管理员；不能删除唯一管理员账号 |
| 后续建议对应的 C# Endpoint 名称 | `AdminController.DeleteUser` |

### 79. PATCH /api/admin/users/{user_id} — 更新用户

| 项目 | 值 |
|------|-----|
| 编号 | 79 |
| HTTP 方法 | PATCH |
| 完整 URL | `/api/admin/users/{user_id}` |
| Python 来源文件 | `core/routes_admin.py` |
| Python 函数名 | `admin_update_user` |
| 路由前缀 | `/api/admin` |
| 鉴权类型 | Admin role dependency |
| 是否需要账号登录 | 是（Depends(require_admin) — 非管理员返回 403） |
| 允许身份和角色 | JWT admin, API Key service/admin, localhost/admin |
| Depends 依赖 | `user: CurrentUser = Depends(require_admin)` |
| 请求 Content-Type | `application/json` + Path |
| Path 参数 | `user_id: str` |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | `{"display_name": "...", "role": "...", "is_active": true, "password": "..."}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "user": {...}}` |
| 可能的 HTTP 状态码 | 200, 400, 401, 403, 404 |
| 可能的错误响应 | `400` — 不能禁用当前登录管理员或无效角色；`404` — 用户不存在；`403` — 非管理员 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.update_user()`, `db.get_user_by_id()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 不能禁用当前登录管理员自己 |
| 后续建议对应的 C# Endpoint 名称 | `AdminController.UpdateUser` |

### 80. GET /api/admin/conversations — 所有对话列表

| 项目 | 值 |
|------|-----|
| 编号 | 80 |
| HTTP 方法 | GET |
| 完整 URL | `/api/admin/conversations` |
| Python 来源文件 | `core/routes_admin.py` |
| Python 函数名 | `admin_list_all_conversations` |
| 路由前缀 | `/api/admin` |
| 鉴权类型 | Admin role dependency |
| 是否需要账号登录 | 是（Depends(require_admin) — 非管理员返回 403） |
| 允许身份和角色 | JWT admin, API Key service/admin, localhost/admin |
| Depends 依赖 | `user: CurrentUser = Depends(require_admin)` |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `limit: int (默认100, 最大500)`, `user_id: str (可选, 按用户筛选)`, `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"conversations": [...]}`（包含所有用户的对话，含 nanobot 会话合并） |
| 可能的 HTTP 状态码 | 200, 401, 403 |
| 可能的错误响应 | `403` — 非管理员 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.list_conversations()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 仅管理员可访问 |
| 后续建议对应的 C# Endpoint 名称 | `AdminController.ListConversations` |

### 81. GET /api/admin/conversations/{conv_id} — 查看任意对话

| 项目 | 值 |
|------|-----|
| 编号 | 81 |
| HTTP 方法 | GET |
| 完整 URL | `/api/admin/conversations/{conv_id}` |
| Python 来源文件 | `core/routes_admin.py` |
| Python 函数名 | `admin_get_conversation` |
| 路由前缀 | `/api/admin` |
| 鉴权类型 | Admin role dependency |
| 是否需要账号登录 | 是（Depends(require_admin) — 非管理员返回 403） |
| 允许身份和角色 | JWT admin, API Key service/admin, localhost/admin |
| Depends 依赖 | `user: CurrentUser = Depends(require_admin)` |
| 请求 Content-Type | 无 |
| Path 参数 | `conv_id: str` |
| Query 参数 | `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | 委托给 `get_conversation(conv_id, _admin)` — 返回对话详情和消息 |
| 可能的 HTTP 状态码 | 200, 401, 403, 404 |
| 可能的错误响应 | `403` — 非管理员；`404` — 对话不存在 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（委托给 `get_conversation`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 仅管理员可访问 |
| 后续建议对应的 C# Endpoint 名称 | `AdminController.GetConversation` |

### 82. GET /api/security/status — 安全状态

| 项目 | 值 |
|------|-----|
| 编号 | 82 |
| HTTP 方法 | GET |
| 完整 URL | `/api/security/status` |
| Python 来源文件 | `core/routes_security.py` |
| Python 函数名 | `security_status` |
| 路由前缀 | `/api/security` |
| 鉴权类型 | Public |
| 是否需要账号登录 | 否（公开路径，列入 `_DEFAULT_PUBLIC_PREFIXES`） |
| 允许身份和角色 | anonymous |
| Depends 依赖 | 无 |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | 无 |
| Header 参数 | 无 |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"auth_enabled": true, "auth_mode": "both", "allow_localhost_without_auth": false, "authenticated": true, "audit_enabled": true, "user_login": true}` |
| 可能的 HTTP 状态码 | 200 |
| 可能的错误响应 | 无 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（若已登录，查询用户信息） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | **公开暴露鉴权配置状态**，含当前用户信息（若已登录）；`security.enabled=false` 时返回 `authenticated: true` |
| 后续建议对应的 C# Endpoint 名称 | `SecurityController.Status` |

### 83. GET /api/security/audit/logs — 审计日志

| 项目 | 值 |
|------|-----|
| 编号 | 83 |
| HTTP 方法 | GET |
| 完整 URL | `/api/security/audit/logs` |
| Python 来源文件 | `core/routes_security.py` |
| Python 函数名 | `list_audit_logs` |
| 路由前缀 | `/api/security` |
| 鉴权类型 | Admin role dependency |
| 是否需要账号登录 | 是（Depends(require_admin) — 非管理员返回 403） |
| 允许身份和角色 | JWT admin, API Key service/admin, localhost/admin |
| Depends 依赖 | `user: CurrentUser = Depends(require_admin)` |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `event_type: str (可选, tool_call|file_access)`, `limit: int (默认100, 最大500)`, `offset: int (默认0)`, `api_key: string（可选，全局 API Key 认证参数，仅在 auth_mode 允许 API Key 时有效）` |
| Header 参数 | `Authorization: Bearer <JWT or API Key>`, `X-API-Key: <API Key>` |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"events": [...], "limit": 100, "offset": 0}` |
| 可能的 HTTP 状态码 | 200, 401, 403 |
| 可能的错误响应 | `403` — 非管理员 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 是（`db.list_audit_events()`） |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 仅管理员可访问；审计日志包含工具调用和文件访问记录 |
| 后续建议对应的 C# Endpoint 名称 | `SecurityController.AuditLogs` |

### 84. POST /api/work/configure — 企业微信配置

| 项目 | 值 |
|------|-----|
| 编号 | 84 |
| HTTP 方法 | POST |
| 完整 URL | `/api/work/configure` |
| Python 来源文件 | `core/wechat/work_bridge.py` |
| Python 函数名 | `configure_work` |
| 路由前缀 | `/api/work` |
| 鉴权类型 | Public |
| 是否需要账号登录 | 否（公开路径前缀 `/api/work`） |
| 允许身份和角色 | anonymous |
| Depends 依赖 | 无 |
| 请求 Content-Type | `application/json` |
| Path 参数 | 无 |
| Query 参数 | 无 |
| Header 参数 | 无 |
| Body JSON | `{"corp_id": "string", "agent_id": "string", "corp_secret": "string"}` |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"status": "ok", "message": "配置已保存"}` |
| 可能的 HTTP 状态码 | 200 |
| 可能的错误响应 | 无 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | **完全公开** — corp_secret 被保存在全局 WorkBridge / WorkClient 的进程内存中；没有管理员认证；任意匿名请求可以覆盖已有配置 |
| 后续建议对应的 C# Endpoint 名称 | `WeChatWorkController.Configure` |

### 85. GET /api/work/status — 企业微信状态

| 项目 | 值 |
|------|-----|
| 编号 | 85 |
| HTTP 方法 | GET |
| 完整 URL | `/api/work/status` |
| Python 来源文件 | `core/wechat/work_bridge.py` |
| Python 函数名 | `work_status` |
| 路由前缀 | `/api/work` |
| 鉴权类型 | Public |
| 是否需要账号登录 | 否（公开路径前缀 `/api/work`） |
| 允许身份和角色 | anonymous |
| Depends 依赖 | 无 |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | 无 |
| Header 参数 | 无 |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `{"configured": true, "connected": true, "message": "..."}` 或 `{"configured": false}` |
| 可能的 HTTP 状态码 | 200 |
| 可能的错误响应 | 无 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 公开暴露企业微信连接状态和配置情况 |
| 后续建议对应的 C# Endpoint 名称 | `WeChatWorkController.Status` |

### 86. POST /api/work/callback — 企业微信回调

| 项目 | 值 |
|------|-----|
| 编号 | 86 |
| HTTP 方法 | POST |
| 完整 URL | `/api/work/callback` |
| Python 来源文件 | `core/wechat/work_bridge.py` |
| Python 函数名 | `work_callback` |
| 路由前缀 | `/api/work` |
| 鉴权类型 | Public |
| 是否需要账号登录 | 否（公开路径前缀 `/api/work`） |
| 允许身份和角色 | anonymous |
| Depends 依赖 | 无 |
| 请求 Content-Type | `application/xml`（企业微信 XML 格式） |
| Path 参数 | 无 |
| Query 参数 | 无 |
| Header 参数 | 无 |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | `"ok"`（text/plain, 200） |
| 可能的 HTTP 状态码 | 200 |
| 可能的错误响应 | 无 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 是（解析 XML 后调用 `_bridge.handle_message()`，内部使用 `KejiAdapter.chat()`） |
| 当前安全注意事项 | 接口位于公开前缀 /api/work 下，匿名请求可以访问；未验证 msg_signature；未验证 timestamp；未验证 nonce；未实现企业微信消息解密；未实现重放防护；FromUserName、Content、MsgType 等字段均直接来自匿名 XML，不能视为企业微信已保证；合法格式的匿名 XML 可能触发 Agent；XML 解析失败仍返回 HTTP 200 和 ok；未配置桥接时返回 HTTP 200 和 not configured |
| 后续建议对应的 C# Endpoint 名称 | `WeChatWorkController.Callback` |

### 87. GET /api/work/callback — 企业微信验证

| 项目 | 值 |
|------|-----|
| 编号 | 87 |
| HTTP 方法 | GET |
| 完整 URL | `/api/work/callback` |
| Python 来源文件 | `core/wechat/work_bridge.py` |
| Python 函数名 | `work_callback_verify` |
| 路由前缀 | `/api/work` |
| 鉴权类型 | Public |
| 是否需要账号登录 | 否（公开路径前缀 `/api/work`） |
| 允许身份和角色 | anonymous |
| Depends 依赖 | 无 |
| 请求 Content-Type | 无 |
| Path 参数 | 无 |
| Query 参数 | `msg_signature: str`, `timestamp: str`, `nonce: str`, `echostr: str` |
| Header 参数 | 无 |
| Body JSON | 无 |
| Form 参数 | 无 |
| 上传文件参数 | 无 |
| 成功响应示例 | 直接返回 `echostr` 明文（text/plain） |
| 可能的 HTTP 状态码 | 200 |
| 可能的错误响应 | 无 |
| 是否使用 SSE | 否 |
| SSE 响应头 | 无 |
| 是否访问数据库 | 否 |
| 是否访问文件系统 | 否 |
| 是否调用 Agent | 否 |
| 当前安全注意事项 | 接口公开；虽然接收 msg_signature、timestamp、nonce 和 echostr，但未验证任何签名；直接原样返回调用者提交的 echostr；当前不是完整或安全的企业微信回调 URL 验证实现；任意匿名调用者都可以获得其提交的 echostr 响应 |
| 后续建议对应的 C# Endpoint 名称 | `WeChatWorkController.CallbackVerify` |

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
