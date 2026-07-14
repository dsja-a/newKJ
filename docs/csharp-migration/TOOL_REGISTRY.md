# Tool Contracts and Frozen Registry

## Design

Tools are defined by immutable C# contracts at compile time. No executable code, delegates, MethodInfo, or reflection is involved. The registry is two-phase: a mutable builder is used during startup, then frozen into a read-only singleton for the application lifetime.

## Two-phase registry

```
Startup: KejiToolRegistryBuilder.Register(...)  ← BuiltInToolCatalog
         ↓
Runtime: IKejiToolRegistry / KejiFrozenToolRegistry  (immutable, concurrent-safe)
```

- `KejiToolRegistryBuilder`: call `Register()` once per tool, then `Build()` exactly once. Duplicate names and double-build throw `KejiToolContractException`.
- `KejiFrozenToolRegistry`: internal class implementing `IKejiToolRegistry`. Uses `FrozenDictionary` for reads and `ReaderWriterLockSlim` for thread-safe `GetAll()` ordering. `StringComparer.Ordinal` for name lookups.
- `IKejiToolRegistry`: read-only interface with `Resolve(KejiToolName)`, `Contains(KejiToolName)`, `GetAll()`.

## Tool name (`KejiToolName`)

- Pattern: `^[a-z][a-z0-9_]{0,64}$` (1–64 chars, lowercase ASCII start, ASCII letters/digits/underscores only).
- No trim, no auto-lowercase, no fuzzy match. `StringComparer.Ordinal` throughout.
- `TryCreate(string, out KejiToolName?)` with `[NotNullWhen(true)]`.

## Definitions

### `KejiToolDefinition`

| Property | Validation |
|---|---|
| `Name` | `KejiToolName` (validated at construction) |
| `Category` | `Enum.IsDefined` |
| `RiskLevel` | `Enum.IsDefined` |
| `ExecutionTarget` | `Enum.IsDefined` |
| `Availability` | `Enum.IsDefined` |
| `ContractVersion` | ≥1 |
| `Description` | 1–2000 chars, non-empty |
| `Tags` | ≤16 items, each ≤32 chars |
| `RequiredPermission` | Must pass `KejiPermissionCatalog.IsDefined()` |
| `InputSchema` | `KejiToolInputSchema` (nullable → empty) |
| `OutputSchema` | `KejiToolInputSchema` (nullable → empty) |

### `KejiToolParameterDefinition`

| Property | Validation |
|---|---|
| `Name` | Non-empty, valid identifier pattern |
| `Type` | `Enum.IsDefined` `KejiToolParameterType` |
| `Required` | Boolean |
| `DefaultValue` | Must be convertible to declared type |
| `MinValue` / `MaxValue` | Applied per type |
| `AllowedValues` | List of allowed values |
| `Sensitive` | If true, validator returns generic error messages |
| `MaxItems` | Required when Type=Array |

### `KejiToolInputSchema`

- Null constructor arg → empty parameter list.
- Duplicate parameter names → `KejiToolContractException`.
- Iteration preserves insertion order.

## Validation (`KejiToolInputValidator`)

Static `Validate(IKejiToolRegistry, KejiToolName, IReadOnlyDictionary<string, object?>)`:

1. Validate name format.
2. Resolve from registry.
3. Check required params.
4. Match CLR types (int, bool, double, string, list).
5. Check constraints (min/max, allowed values).
6. Sensitive-parameter-safe error messages (don't leak value).

Returns `KejiToolValidationResult` via `Valid()` / `Invalid(error, message)` factory methods. `KejiToolValidationError` enum with 9 codes. First-error-only (early-exit).

## Built-in tool catalog

43 tools defined in `BuiltInToolCatalog`. All `ContractOnly` or `ExecutionPending`. Derived from Python baseline at commit `aad0afab7181e529a53691a4f2801f295025a2c7`.

### Excluded high-risk tools (RejectedLegacyCapability)

| Tool | Reason |
|---|---|
| `exec` | Arbitrary code execution |
| `run_code` | Arbitrary code execution |
| `db_execute_query` | Arbitrary SQL execution |
| `cron` | Background process scheduling |
| `spawn` | Process spawning |
| `notebook_edit` | Interactive code execution |
| `my` | User impersonation |

## DI registration

```csharp
services.AddKejiToolRegistry();  // registers IKejiToolRegistry as singleton
```

## Security properties

- No tool has executable code (`ContractOnly` or `ExecutionPending`).
- Registry is immutable after startup — no hot-reload or dynamic registration.
- Tool name validation at registration and validation time rejects non-ASCII, uppercase, empty names.
- Only compile-time trusted C# code can register tools (`BuiltInToolCatalog`).
- Python baseline tools excluded from catalog do not appear in any C# list.
