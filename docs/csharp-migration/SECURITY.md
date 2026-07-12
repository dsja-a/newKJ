# C# Security Implementation

## Password Hashing

BCrypt via BCrypt.Net-Next 4.0.3. Work factor: 12.

### Python bcrypt compatibility

Hashes produced by `bcrypt` >= 4.0 (Python) are verified successfully in C#. The test `Jwt_PythonCompatibleToken_Validates` uses a pre-computed Python bcrypt fixture `$2a$12$OcqTVPvKF43yxT2Kcmb/nOaBRg0icXiWTCRL3WYr46C5vQNw6GtGS` (password `test_password_123`).

## JWT

### Algorithm

HS256 (HMAC-SHA256) via `System.IdentityModel.Tokens.Jwt` 8.6.0.

### Exact claims

| Claim | Type | Example |
|-------|------|---------|
| `sub` | string | user ID |
| `username` | string | login name |
| `role` | string | `admin`, `member`, or `readonly` |
| `iat` | number | Unix timestamp (seconds) |
| `exp` | number | Unix timestamp (seconds) |
| `jti` | string | random UUID |

No `unique_name` claim is emitted. No URI-format claim types (`ClaimTypes.Role` etc.) are used.

### Expiry and clock skew

`jwt_expire_hours` (default 72, range 1–720).
`jwt_clock_skew_seconds` (default 0, range 0–300).

## AuthMode

Three modes, configured via `security.auth_mode` in config.yaml:

| Mode | Login | API Key auth | Requires JWT Secret | Requires API Key |
|------|-------|--------------|---------------------|------------------|
| `Both` | yes | yes | yes | yes |
| `UserOnly` | yes | no | yes | no |
| `ApiKeyOnly` | no (503) | yes | no | yes |

- `UserOnly` mode does NOT configure an API Key for the server. API Key authentication is rejected.
- `ApiKeyOnly` mode does NOT configure a JWT Secret. Login returns 503.

## Fail-closed startup

The application MUST fail to start when required secrets are missing:

### Missing JWT Secret

If `Enabled=true` and `AuthMode` is `Both` or `UserOnly`, `JwtSecret` must be set in config or via `KEJI_JWT_SECRET` environment variable. Otherwise `KejiSecurityConfigurationException` is thrown during startup.

### Missing API Key

If `Enabled=true` and `AuthMode` is `Both` or `ApiKeyOnly`, `ApiKey` must be set in config or via `KEJI_API_KEY` environment variable. Otherwise `KejiSecurityConfigurationException` is thrown during startup.

### Bootstrap Admin behavior

Bootstrap admin password is configured via `security.bootstrap_admin.password` in config.yaml or `KEJI_ADMIN_PASSWORD` environment variable.

- **Zero users + missing password**: `KejiSecurityConfigurationException` is thrown during `KejiStartupInitializer.StartAsync`. The host fails to start.
- **Existing users + missing password**: Bootstrap skips (`SkippedExistingUsers`). The host starts normally.
- **Password length < 12**: `KejiSecurityConfigurationException` during initialization.
- **Duplicate username during concurrent initialization**: If the conflicting user has `role = "admin"`, `AlreadyCreated` (IsCreated=false) is returned. If the conflicting user has a non-admin role, `KejiSecurityConfigurationException` is thrown.

## Default public paths

These paths are public by default (no authentication required):

```
/
/health
/favicon.ico
/static
/api/security/status
/api/auth/login
/api/work
```

Additional paths can be configured via `security.public_paths` in config.

## `/api/auth/login-evil` boundary

`/api/auth/login-evil` is NOT in the default public paths. Access requires authentication.

## Fixed-time API Key comparison

API Key comparison uses `fixedTimeEquals` (constant-time) to prevent timing attacks.

## Query API Key

Query-string API Key (`?api_key=...`) is disabled by default (`allow_api_key_in_query: false`). When enabled, the key is only accepted over HTTPS.

## Localhost authentication and proxy risk

When `allow_localhost_without_auth: true`, requests from `127.0.0.1` or `::1` bypass authentication entirely. This is intended for development only. Running behind a reverse proxy that forwards the original client IP requires careful configuration; otherwise the proxy's internal IP (`127.0.0.1`) may be seen by the application, granting unintended access.

## Database user state revalidation on every JWT request

Every authenticated request re-reads the user record from the SQLite database. If the user has been deactivated (`is_active = 0`), deleted, or had their role changed, the JWT is rejected with 401 even if the token itself is still valid.

## Database failure returns 500, not 401

If a database error (`KejiPersistenceException`) occurs during authentication, the middleware returns 500 `{"detail":"服务器内部错误"}`. A database failure is never disguised as a wrong-password or invalid-token response (which would leak information).

## ApiKeyOnly login behavior

Login (`/api/auth/login`) returns 503 `{"detail":"当前认证模式不支持用户登录"}` in ApiKeyOnly mode.

## Endpoints

### POST `/api/auth/login`

Public (no auth required). Accepts `LoginRequest` JSON (`username`, `password`). Returns JWT token in `LoginResponse` (`access_token`, `token_type`).

| Response | Condition |
|----------|-----------|
| 200 | Success |
| 401 | Invalid credentials |
| 422 | Malformed request body |
| 500 | Server/database error |
| 503 | ApiKeyOnly mode |

### GET `/api/auth/me`

Requires authentication. Returns current user info (`UserResponse`: `id`, `username`, `display_name`, `role`, `is_active`).

| Response | Condition |
|----------|-----------|
| 200 | Authenticated |
| 401 | Not authenticated |
| 500 | Server/database error |

### 401, 422, 500 response semantics

- **401**: `{"detail":"未授权：请登录（/api/auth/login）或使用有效 API Key"}`
- **422**: `{"detail":"请求格式错误"}`
- **500**: `{"detail":"服务器内部错误"}` (generic, no SQL or secret details leaked)

## No Refresh Token

Token refresh is not implemented. Clients must obtain a new token by logging in again.

## No Logout

Server-side logout is not implemented. Token revocation is not supported.

## No AdminController

User management (list, create, update, delete) is not implemented in the C# API. This is left for TASK-006.

## TASK-006 scope

TASK-006 will implement: `AdminController` (`GET/POST /api/admin/users`, `PUT/DELETE /api/admin/users/{user_id}`), user CRUD operations, and a `GET /api/admin/users/me` endpoint for password changes.
