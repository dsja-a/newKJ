# TASK-008 Structured Security Audit Foundation

## Audit events

`KejiAuditEvent` is the sole audit event type. Every event carries:

- a `Guid EventId` (server-generated, no caller override);
- a `DateTime OccurredAtUtc` (from `TimeProvider`, no caller override);
- a `KejiAuditCategory Category` (Authentication, Authorization, WorkspaceSecurity, Configuration);
- a `KejiAuditSeverity Severity` (Information, Warning, Error, Critical);
- a `KejiAuditOutcome Outcome` (Success, Failure, Denied, Error);
- a `string ActorId` (from `ICurrentUserAccessor`, no caller override);
- a `string ActorRole` (from `ICurrentUserAccessor`, validated against known roles);
- a `string AuthenticationType` (from `ICurrentUserAccessor`, validated against known types);
- a nullable `string CorrelationId` (from `IKejiAuditCorrelationAccessor`, via `HttpContext.TraceIdentifier`);
- a required, non-empty, bounded `string Action`;
- a required, non-empty, bounded `string TargetType`;
- a nullable `string TargetId`;
- an immutable `IReadOnlyDictionary<string, string> Metadata`.

## Safety rules

- The constructor is `internal`; callers cannot supply EventId, OccurredAtUtc, ActorId, ActorRole, or AuthenticationType.
- `EventId` is always `Guid.NewGuid()` in the service.
- `OccurredAtUtc` is always from `TimeProvider.GetUtcNow()`.
- `ActorId`/`ActorRole`/`AuthenticationType` always come from `ICurrentUserAccessor`.
- `CorrelationId` comes from `IKejiAuditCorrelationAccessor` (HTTP: `HttpContext.TraceIdentifier`), control chars stripped, max 128 chars.
- Input validation: category/outcome/severity must be defined enum values; action/targetType non-empty, max length, no control chars; returns `ValidationError` distinct from `SinkError`.

## Multi-sink architecture

`KejiAuditService` takes `IEnumerable<IKejiAuditSink>` and writes to all registered sinks. Each sink receives the same immutable event snapshot. A failing sink does not block other sinks. The aggregate result is one of:

- `Written`: all sinks succeeded
- `PartialFailure`: at least one sink succeeded, at least one failed
- `SinkError`: all sinks failed, or no sinks registered
- `ValidationError`: input validation failed (returned before any sink is called)

## Correlation accessor

`IKejiAuditCorrelationAccessor` abstracts correlation ID sourcing. The ASP.NET Core implementation (`KejiCorrelationAccessor`) reads `HttpContext.TraceIdentifier`, strips control characters, and limits to 128 characters. Non-HTTP environments use `NullCorrelationAccessor` which returns null.

## Input validation

The service validates:
- Enum values must be defined (0/default is rejected).
- `action` and `targetType`: non-empty, max 256/128 chars, no control characters.
- `targetId`: max 128 chars, no control characters (if non-null).
- `actorRole` must be a known role; unknown roles fall back to "unknown".
- `authenticationType` must be a known type; unknown types fall back to "none".

Invalid inputs return `KejiAuditResult.ValidationError` without writing to any sink.

## Audit failure policy

- Audit failures never change the original authentication or authorization outcome.
- Sink errors log fixed error codes via `ILogger` (no raw exceptions, paths, or event content).
- `OperationCanceledException` always propagates.
- No empty `catch { }` blocks exist.

## Database sink

`KejiDatabaseAuditSink` writes to the `audit_events` table using parameterized SQL. The `event_id` column has a unique constraint; duplicate EventId writes return `Error` and do not overwrite existing rows. `created_at` uses the event's `OccurredAtUtc` as a Unix timestamp.

## DI registration

`AddKejiAuditingFoundation` registers:
- `IKejiAuditSink` → `KejiDatabaseAuditSink` (singleton)
- `IKejiAuditService` → `KejiAuditService` (scoped)
- `IKejiAuditCorrelationAccessor` → null accessor (singleton, overridden in API)

The API overrides the correlation accessor with `KejiCorrelationAccessor` and also registers `IKejiAuditBridge` → `KejiAuditBridgeImpl`.

## Integration

- `IKejiAuditBridge` returns `Task` (no return type to avoid circular dependency with Keji.Auditing).
- `KejiAuditBridgeImpl` calls `IKejiAuditService.WriteAsync`, logs sink errors via `ILogger`, and propagates `OperationCanceledException`.
- Middleware audit calls use the bridge; sink failures log warnings and do not change the auth decision.
- No changes to Python or the web frontend.

## Metadata sanitizer

- Sensitive keys (password, token, secret, etc.) are case-insensitively removed.
- Control characters (including CR, LF, Tab) are removed to prevent log injection.
- Unicode surrogate pairs are preserved; lone surrogates are discarded.
- Key count limit: 32. Key length limit: 64 chars. Value length limit: 512 chars.
- Total metadata character limit: 4096.
- Caller modifications to the source dictionary after `WriteAsync` do not affect the event.

## Verification

- `Keji.Auditing.Tests`: 102/102
- `Keji.Security.Tests`: 541/541
- `Keji.Persistence.Tests`: 138/138
- `Keji.Integration.Tests`: 91/91
- `Keji.FileSystem.Tests`: 289/289
- `Keji.Agent.Tests`: 1/1
- `Keji.Tools.Tests`: 1/1
- Full solution: 1163/1163
- Failed: 0
- Skipped: 0
- Build warnings: 0
- Build errors: 0
- Known NuGet vulnerabilities: 0 across 20 projects
- `git diff --check`: no whitespace errors

TASK-008 is accepted. The initial commit is `546b3a62729cb916ff0eca0af08e64ff89019df9`. The repair commit is `aaa126e15a2f4ea130a540b13643be13ed879ad4`. The final fix commit is `c027ab34a7a30655f11ff035af191b31cae57bd1`. The next task is TASK-009, which has not started.
