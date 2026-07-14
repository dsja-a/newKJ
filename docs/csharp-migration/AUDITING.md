# TASK-008 Structured Security Audit Foundation

## Audit events

`AuditEvent` is the sole audit event type. Every event carries:

- a `Guid EventId` and `DateTimeOffset Timestamp`;
- an `AuditCategory Category` (Authentication, Authorization, ResourceAccess, Configuration, System);
- an `AuditSeverity Severity` (Information, Warning, Failure);
- an `AuditOutcome Outcome` (Success, Denied, Failure);
- a nullable `ActorId` (16 hex chars, not the raw user string);
- a nullable `TargetId` (same format);
- a required, non-empty, trimmed `Message`;
- a nullable `Exception` reference (for Failure outcomes).

## Safety rules

`AuditEventSafety` enforces immutability-checks after event creation and serialization-bounds checks before serialization:

- Message length limit (4096 characters, code unit limit).
- Actor and Target ID length limit (16 hex chars).
- Category, Severity, and Outcome must be defined enum values (0 is invalid/undefined).

Serialization uses `JsonSerializer` with `WriteIndented = false` and throws `AuditSerializationException` on length violations.

## Audit service

`IAuditService` / `AuditService` implements:

- `WriteAsync(AuditEvent, CancellationToken)` — guards, serializes, and routes to the configured sink.
- `WriteIfAsync(Func<AuditEvent?>, AuditCategory, AuditSeverity, AuditOutcome, ...)` — deferred evaluation factory.
- `IBufferedAuditService` — adds `FlushAsync` for buffer-friendly sinks.

`AddKejiAuditing` registers the service as scoped (request-bound lifetime).

## File-system audit sink

`AuditFileSinkOptions` configures the directory path. The sink writes one JSON line per event to `yyyy-MM-ddTHH-mm-ssZ_fff_{eventId:n}.audit.json`. All writes are flushed through `FlushAsync` which calls `await Task.CompletedTask`.

## Abstraction layer

`IAuditSink` / `AuditSinkBase` allow replacing the sink without touching service logic. The service resolves `IEnumerable<IAuditSink>` and writes to all registered sinks.

## Integration

- `Keji.Api` registers `AddKejiAuditing` in `AuditingModule`.
- `AuditEvent` fields use `System.Text.Json` attributes (`JsonPropertyName`, `JsonIgnore`).
- No changes to existing production code in Security, Persistence, FileSystem, Agent, or Tools.
- No changes to Python or the web frontend.

## Verification

- `Keji.Auditing.Tests`: 60/60
- Full solution: 1121/1121
- Failed: 0
- Skipped: 0
- Build warnings: 0
- Build errors: 0
- Known NuGet vulnerabilities: 0
- `git diff --check`: empty stdout and stderr

TASK-008 is ready for acceptance.
