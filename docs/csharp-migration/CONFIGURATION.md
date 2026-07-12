# Configuration: Python vs C# Behavior

## Overview

This document describes behavioral differences between the Python and C# configuration implementations.

---

## Load Order

### Python
1. Parse `config.yaml` with PyYAML `SafeLoader`
2. Call `load_dotenv()` which writes into `os.environ`
3. Resolve `${ENV_VAR}` references via `os.environ`
4. Return flat settings dict

### C# (Keji.Configuration)
1. Probe project root for `config.yaml`
2. Parse `.env` file into internal `DotEnvStore` (never written to process environment)
3. Create two `IEnvironmentValueSource` instances:
   - `ProcessEnvironmentValueSource` (reads `Environment.GetEnvironmentVariable`)
   - `DotEnvEnvironmentValueSource` (reads `.env` store)
4. `CompositeEnvironmentValueSource` queries process first, then `.env` (process priority)
5. `SafeYamlConfigurationLoader` parses `config.yaml` into `ConfigNode` tree
6. `EnvironmentReferenceResolver` walks tree and replaces `${ENV_VAR}` references
7. Return `KejiConfigurationDocument`

**Key difference:** Python's `load_dotenv()` mutates the process environment. C# keeps `.env` as an isolated internal store to avoid side effects.

---

## Environment Variable References

### Syntax
- **Python:** Only matches exact `${ENV_VAR}` pattern. No substring interpolation. No `${VAR|default}` syntax. Missing variables return empty string with a warning.
- **C#:** Only matches exact `${ENV_VAR}` pattern. Regex: `^\$\{([A-Za-z_][A-Za-z0-9_]*)\}$`. No substring interpolation. No `${VAR|default}` syntax.

### Behavior
- `prefix-${VAR}` — kept as-is by both (not resolved)
- `${VAR}-suffix` — kept as-is by both (not resolved)
- `${VAR|default}` — kept as-is by both (not resolved)
- `${1BAD}` — kept as-is by both (invalid name)
- `${VALID_NAME}` — resolved by both

### Resolution Order
- **C#:** Process environment → `.env` file (process priority)
- **Python:** `os.environ` (which is mutated by `load_dotenv`)

### Missing Variable Handling

| Scenario | Python | C# (strict) | C# (non-strict) |
|---|---|---|---|
| Missing var | Returns "" and `warnings.warn()` | Throws `KejiConfigurationException` | Returns "" + `EnvironmentResolutionDiagnostic` |
| Exception contains | — | Variable name + config path | — |
| Exception does NOT contain | — | Secret values | — |

---

## YAML Loading Security

| Concern | Python (PyYAML SafeLoader) | C# (SafeYamlConfigurationLoader) |
|---|---|---|
| Custom tags | Blocked | Blocked — Tag check on Scalar/MappingStart/SequenceStart |
| Anchors | Allowed | Rejected — Anchor check on all nodes |
| Aliases | Allowed | Rejected — AnchorAlias event raises exception |
| Arbitrary object creation | Blocked via `SafeLoader` | Not possible — event-based parser, no deserializer |
| Max file size | Not enforced | Enforced via `MaxConfigFileBytes` (default 1 MB) |
| Max depth | Not enforced | Enforced via `MaxDepth` (default 32) |
| Max nodes | Not enforced | Enforced via `MaxNodeCount` (default 10,000) |
| Duplicate keys | Last wins | Throws `KejiConfigurationException` |
| Root must be mapping | No | Yes |
| YAML parse exceptions | Native | Wrapped as `KejiConfigurationException` with file/line/col |

---

## `.env` File Handling

### Python (`python-dotenv`)
- `load_dotenv()` reads `.env` and calls `os.environ[key] = value`
- `set_key()` uses line-by-line replacement
- Supports `export KEY=value`
- Allows duplicate keys (last wins)
- No file size or line length limits

### C# (`DotEnvStore`)
- Reads `.env` into internal structures; **never calls `Environment.SetEnvironmentVariable`**
- Variable name must match `^[A-Za-z_][A-Za-z0-9_]*$`
- NUL bytes in file or value → rejected
- Default `MaxDotEnvFileBytes`: 1 MB
- Default `MaxDotEnvLineLength`: 16384
- Duplicate variable names → throws `KejiConfigurationException`
- `export KEY=value` → throws `KejiConfigurationException`
- Non-empty, non-comment lines must be `KEY=value`
- Simple single and double quote stripping supported
- `GetSnapshot()` returns a read-only copy, never the internal dictionary

### UpsertAsync (Safe Write)
1. Validates key format
2. Rejects value containing CR, LF, or NUL
3. Value is never logged, never returned in result
4. Preserves all original comments, blank lines, and variable order
5. Updates existing variable in place (only replaces its line)
6. New keys appended to end of file
7. UTF-8 without BOM
8. Creates temp file with random name in same directory
9. Uses `FileStream` with async writes
10. `FlushAsync` followed by `Flush(flushToDisk: true)`
11. Atomic `File.Move` replacement
12. On failure, original file unchanged
13. `finally` cleans up temp file
14. Supports `CancellationToken`
15. Instance-level `SemaphoreSlim` for concurrency
16. Returns `DotEnvUpsertResult` (key, created, updated) — value never returned

---

## Provider Secret Names

Method: `ProviderSecretName.GetEnvironmentVariableName(string provider)`

| Provider | Result |
|---|---|
| `deepseek` | `DEEPSEEK_API_KEY` |
| `openai` | `OPENAI_API_KEY` |
| `my-provider` | `MY_PROVIDER_API_KEY` |
| `azure_openai` | `AZURE_OPENAI_API_KEY` |

Rules:
- Known providers (`deepseek`, `openai`) return fixed env var names (case-insensitive)
- Generic providers: uppercase, hyphens → underscores, append `_API_KEY`
- Rejected characters: space, newline, `=`, `/`, `\`, `.`, `:`
- Rejected inputs: empty string, null
- Invalid provider → throws `ArgumentException`

---

## Secret Masking

### Behavior
Both mask the values of sensitive keys (case-insensitive). Mask value is `***`.

### Sensitive Key Names (15 patterns, C#)
```
api_key, apikey, app_secret, client_secret, secret,
password, token, access_token, refresh_token,
verification_token, encrypt_key, work_secret, jwt_secret,
private_key, connection_string
```

### Python
- `mask_secrets()` uses `***`
- Only works on `ConfigNode` tree

### C# Differences
- Masking is recursive through `ConfigMap`, `ConfigSequence`, `IReadOnlyDictionary<string, object?>`, and `IEnumerable<object?>` children
- The masker returns a **new** tree; it never mutates the original
- `ToString()` does not leak secrets
- Exceptions do not leak secrets
- `MaskApiKeyForSettings(string?)`: null/empty → `{IsConfigured=false, DisplayValue=""}`, any non-empty → `{IsConfigured=true, DisplayValue="***"}`

---

## Configuration Document Access

### Python
Flat dictionary access: `config["key"]["nested"]`

### C#
`KejiConfigurationDocument` provides typed accessors:
- `GetOptionalString(path)` — returns `null` if missing
- `GetRequiredString(path)` — throws `KejiConfigurationException` if missing
- `GetBoolean(path, default)` — parses `bool`
- `GetInt32(path, default)` — parses `int`
- `GetStringList(path)` — reads `ConfigSequence` as `IReadOnlyList<string>`
- `TryGetNode(path, out node)` — raw `ConfigNode` access

---

## Error Handling

### Python
Uses built-in exceptions (`KeyError`, `TypeError`).

### C#
Uses `KejiConfigurationException` with:
- Human-readable message
- `ConfigPath` property (dotted config key that caused the error)
- `FilePath`, `LineNumber`, `ColumnNumber` for YAML parse errors
- Inner exception support

---

## DI Registration

### C# Only
Method: `AddKejiConfigurationFoundation()`

Registers (all singleton):
- `KejiConfigurationLoadOptions`
- `IDotEnvStore`
- `IEnvironmentValueSource` (composite: process → .env)
- `ISecretMasker`
- `IKejiConfigurationLoader`

**Does NOT register or auto-load `KejiConfigurationDocument`** — DI registration phase does not perform file I/O. Callers must explicitly call `IKejiConfigurationLoader.Load(options)` when needed.

Python has no DI container integration.
