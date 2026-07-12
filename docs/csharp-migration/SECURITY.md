# C# Security Implementation

## Password Hashing

BCrypt via BCrypt.Net-Next 4.0.3. Work factor: 12.

### Python bcrypt compatibility

Hashes produced by `bcrypt` >= 4.0 (Python) are verified successfully in C#. The test `Bcrypt_PythonFixture_Verifies` uses a pre-computed Python bcrypt fixture with `$2b$12$` prefix (password `test_password_123`).

Generated via:
```
python -c "import bcrypt; print(bcrypt.hashpw(b'test_password_123', bcrypt.gensalt(rounds=12)).decode())"
```

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

### Token validation uses TimeProvider

Both `CreateToken` and `ValidateToken` use the injected `TimeProvider`. Lifetime validation does not depend on `DateTime.UtcNow`. Clock skew is applied relative to the same `TimeProvider`.

## AuthMode

Three modes, configured via `security.auth_mode` in config.yaml:

| Mode | Login | API Key auth | Requires JWT Secret | Requires API Key |
|------|-------|--------------|---------------------|------------------|
| `Both` | yes | yes | yes | yes |
| `UserOnly` | yes | no | yes | no |
| `ApiKeyOnly` | no (503) | yes | no | yes |

- `UserOnly` mode does NOT configure an API Key for the server. API Key authentication is rejected.
- `ApiKeyOnly` mode does NOT configure a JWT Secret. Login returns 503.

## Secret configuration

Secrets are resolved by the Configuration Foundation layer (`KejiConfigurationLoader` → `EnvironmentReferenceResolver`). The YAML config file uses `${KEJI_JWT_SECRET}`, `${KEJI_API_KEY}`, and `${KEJI_ADMIN_PASSWORD}` references that are resolved before the document reaches `KejiSecurityOptions.FromConfiguration`.

`KejiSecurityOptions.FromConfiguration` accepts only the pre-resolved `KejiConfigurationDocument`. It does NOT call `Environment.GetEnvironmentVariable`. There is exactly one secret resolution path through the Configuration Foundation.

## Fail-closed startup

The application MUST fail to start when required secrets are missing:

### Missing JWT Secret

If `Enabled=true` and `AuthMode` is `Both` or `UserOnly`, and the config document does not contain `security.jwt_secret`, `KejiSecurityConfigurationException` is thrown during startup.

### Missing API Key

If `Enabled=true` and `AuthMode` is `Both` or `ApiKeyOnly`, and the config document does not contain `security.api_key`, `KejiSecurityConfigurationException` is thrown during startup.

### Bootstrap Admin behavior

Bootstrap admin password is configured via `security.bootstrap_admin.password` in config.yaml (resolved from `${KEJI_ADMIN_PASSWORD}`).

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

Query-string API Key (`?api_key=...`) is disabled by default (`allow_api_key_in_query: false`). Deployments MUST use HTTPS; the application does not enforce HTTPS scheme at the application level.

## Localhost authentication and proxy risk

When `allow_localhost_without_auth: true`, requests from `127.0.0.1` or `::1` bypass authentication entirely. This is intended for development only. Running behind a reverse proxy that forwards the original client IP requires careful configuration; otherwise the proxy's internal IP (`127.0.0.1`) may be seen by the application, granting unintended access.

## Database user state revalidation

Only **JWT-authenticated** requests re-read the user record from the SQLite database on every request. API Key and Localhost are service-level identities and are not subject to per-request user revalidation.

If a JWT user has been deactivated (`is_active = 0`) or deleted in the database, the request is rejected with 401.

If a JWT user's role has changed in the database, the token is still valid for authentication, but the current request uses the **latest role from the database**, not the role in the JWT.

## Database failure returns 500, not 401

If a database error (`KejiPersistenceException`) occurs during authentication, the global `KejiApiExceptionMiddleware` returns 500 `{"detail":"服务器内部错误"}`. A database failure is never disguised as a wrong-password or invalid-token response (which would leak information).

## Global exception middleware

`KejiApiExceptionMiddleware` is registered before `KejiAuthenticationMiddleware` and `MapControllers`:

```
app.UseMiddleware<KejiApiExceptionMiddleware>();
app.UseMiddleware<KejiAuthenticationMiddleware>();
app.MapControllers();
```

Rules:
- `OperationCanceledException` propagates unmodified.
- `KejiPersistenceException` → 500, safe body.
- `KejiSecurityException` → 500, safe body.
- Unknown exceptions → 500, safe body.
- No `Exception.Message`, `StackTrace`, `InnerException`, SQL, token, API Key, password, or secret is leaked.
- Both public and protected paths are covered.
- Does not rewrite responses after `Response.HasStarted`.

## ApiKeyOnly login behavior

Login (`/api/auth/login`) returns 503 `{"detail":"当前认证模式不支持用户登录"}` in ApiKeyOnly mode.

## Endpoints

### POST `/api/auth/login`

Public (no auth required). Accepts `LoginRequest` JSON (`username`, `password`). Returns:

```json
{
  "token": "...",
  "expires_in": 259200,
  "user": { "id": "...", "username": "...", "display_name": "...", "role": "...", "is_active": true }
}
```

| Response | Condition |
|----------|-----------|
| 200 | Success |
| 401 | Invalid credentials (`{"detail":"用户名或密码错误"}`) |
| 422 | Malformed request (nullable/missing body, empty or too-long fields, wrong types) (`{"detail":"请求格式错误"}`) |
| 500 | Server/database error (`{"detail":"服务器内部错误"}`) |
| 503 | ApiKeyOnly mode (`{"detail":"当前认证模式不支持用户登录"}`) |

### GET `/api/auth/me`

Requires authentication. Returns current user info.

| Response | Condition |
|----------|-----------|
| 200 | Authenticated |
| 401 | Not authenticated (`{"detail":"未登录，请先登录"}`) |
| 500 | Server/database error (`{"detail":"服务器内部错误"}`) |

### Response semantics

- **401 Login**: `{"detail":"用户名或密码错误"}`
- **401 Me**: `{"detail":"未登录，请先登录"}` or `{"detail":"账号已禁用或不存在"}`
- **422**: `{"detail":"请求格式错误"}`
- **500**: `{"detail":"服务器内部错误"}` (generic, no SQL or secret details leaked)
- **503**: `{"detail":"当前认证模式不支持用户登录"}`

## No Refresh Token

Token refresh is not implemented. Clients must obtain a new token by logging in again.

## No Logout

Server-side logout is not implemented. Token revocation is not supported.

## `/api/admin/*` endpoints

All `/api/admin/*` endpoints remain unimplemented.

## TASK-006 scope

TASK-006 will implement:
- Role permission matrix
- Default deny authorization
- Permission checking infrastructure
- Readonly user write restriction enforcement
- Middleware-level permission checks
