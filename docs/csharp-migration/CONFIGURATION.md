# Configuration: Python vs C# Behavior

## Overview

This document describes behavioral differences between the Python and C# configuration implementations.

---

## Architecture

### Python
- `load_app_config()` in `core/security/secrets.py`
  1. Calls `load_dotenv_file()` (custom implementation, not `python-dotenv`)
  2. Reads `config.yaml` with `yaml.safe_load`
  3. Calls `resolve_secrets()` recursively to resolve `${ENV_VAR}` references
  4. Missing variables: logged via Loguru `logger.warning`, resolved to empty string
  5. Returns flat dict

### C# (Keji.Configuration)
Two-layer architecture:
1. **`ISafeYamlConfigurationLoader`** — Low-level YAML-only parser. Reads `config.yaml`, returns raw `ConfigNode` tree. No environment variable resolution.
2. **`IKejiConfigurationLoader`** — Full orchestration coordinator.
   - Creates/reads `.env` via `IDotEnvStore`
   - Creates `ProcessEnvironmentValueSource` and `DotEnvEnvironmentValueSource`
   - Combines as `CompositeEnvironmentValueSource` (process priority)
   - Loads YAML via `ISafeYamlConfigurationLoader`
   - Resolves `${ENV_VAR}` via `EnvironmentReferenceResolver`
   - Returns `KejiConfigurationLoadResult` with resolved `Document` and `Diagnostics`

Callers use:
```csharp
var result = loader.Load(options);
result.Document.GetRequiredString("path.to.key");
```

---

## Environment Variable References

### Syntax
Both only match exact `${ENV_VAR}` patterns:
- Regex: `^\$\{([A-Za-z_][A-Za-z0-9_]*)\}$`
- No substring interpolation: `prefix-${VAR}` and `${VAR}-suffix` are kept as-is
- No default value syntax: `${VAR|default}` is kept as-is

### Resolution Order
- **C#:** Process environment → `.env` file (process priority)
- **Python:** `os.environ` (after `load_dotenv_file` writes missing vars)

### Missing Variable Handling

| Scenario | Python | C# (strict) | C# (non-strict) |
|---|---|---|---|
| Missing var | Returns "" + `logger.warning()` | Throws `KejiConfigurationException` | Returns "" + `EnvironmentResolutionDiagnostic` |
| Exception contains | — | Variable name + config path | — |
| Exception does NOT contain | — | Secret values | — |

---

## YAML Loading Security

| Concern | Python | C# (SafeYamlConfigurationLoader) |
|---|---|---|
| Custom tags | Blocked via `yaml.safe_load` | Blocked — Tag check on Scalar/MappingStart/SequenceStart, exception `CUSTOM_TAG` |
| Anchors | Allowed | Rejected — Anchor check on all nodes, exception `ANCHOR_NOT_ALLOWED` |
| Aliases | Allowed | Rejected — `AnchorAlias` event raises `ALIAS_NOT_ALLOWED` |
| Object creation | Blocked via `safe_load` | Not possible — event-based, no deserializer |
| Max file size | Not enforced | Enforced via `MaxConfigFileBytes` (default 1 MB) |
| Max depth | Not enforced | Enforced via `MaxDepth` (default 32) |
| Max nodes | Not enforced | Enforced via `MaxNodeCount` (default 10,000) |
| Duplicate keys | Last wins | Throws `DUPLICATE_KEY` |
| Root must be mapping | No | Yes — `ROOT_NOT_MAPPING` if not |
| Malformed YAML | Native exception | Wrapped as `KejiConfigurationException` with sanitized `YAML_PARSE_ERROR` (line, col only, no raw content) |

### YAML Exception Sanitization
- `YamlException.Message` is NOT included in the public exception
- Public message format: `YAML_PARSE_ERROR: Invalid YAML syntax at line X, column Y.`
- Line and column numbers are included
- Original `YamlException` is NOT set as InnerException to prevent content leakage
- `KejiConfigurationException.ToString()` does not contain any secret values

---

## `.env` File Handling

### Python (`core/security/secrets.py`)
- `load_dotenv_file()`: custom implementation (not `python-dotenv`)
- Lines parsed by splitting on first `=` only; no special `export KEY=value` support (`export KEY` is parsed as variable name `export KEY`, not `export` syntax)
- If the same key appears multiple times in `.env`:
  - First occurrence that successfully writes to `os.environ` wins
  - Subsequent same-key lines are skipped because the variable is already set in `os.environ`
- Process-pre-existing environment variables take priority over all `.env` lines
- `upsert_dotenv_var()`: custom line-by-line update implementation
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

### UpsertAsync (Safe Write, Transactional)
1. Acquires `SemaphoreSlim`
2. Creates **candidate copies** of `_lines` and `_keyIndex`
3. Modifies candidate copy only
4. Validates candidate: checks `MaxDotEnvLineLength` and `MaxDotEnvFileBytes`
5. Writes candidate to temp file (UTF-8 without BOM — `new UTF8Encoding(false)`)
6. `FlushAsync` + `Flush(flushToDisk: true)`
7. Atomic replace:
   - Target exists: `File.Replace(tempFile, targetFile, null)` (no backup)
   - Target absent: `File.Move(tempFile, targetFile)`
8. On success: swaps `_lines` and `_keyIndex` with candidates
9. On failure: cleans temp file; **both memory state and disk file remain unchanged**
10. Supports `CancellationToken`

---

## Loader Semantics

### IKejiConfigurationLoader
- `IKejiConfigurationLoader` does **not** hold a fixed `IDotEnvStore` instance
- Each `Load(KejiConfigurationLoadOptions options)` call creates a new `DotEnvStore` using `options.ProjectRoot`, `options.DotEnvFileName`, `options.MaxDotEnvFileBytes`, and `options.MaxDotEnvLineLength`
- A single `IKejiConfigurationLoader` instance can safely load configuration for different `ProjectRoot` values across separate `Load()` calls
- Each `Load()` call re-reads `.env` from disk; external modifications between calls are picked up
- DI registration of `IKejiConfigurationLoader` (via `AddKejiConfigurationFoundation()`) performs **no file I/O** — only the `ISafeYamlConfigurationLoader` is injected; `.env` and `config.yaml` are read only within `Load()`
- The separately registered `IDotEnvStore` service (lazy factory) exists for standalone `.env` operations (e.g., `UpsertAsync`) but is **not** used by the full `IKejiConfigurationLoader`

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
- Known providers (`deepseek`, `openai`) mapped to fixed env var names (case-insensitive)
- Generic: uppercase, hyphens → underscores, append `_API_KEY`
- Rejected: empty, space, newline, `=`, `/`, `\`, `.`, `:`
- Invalid → `ArgumentException`

---

## Secret Masking

Both mask sensitive key values with `***`.

### Sensitive Key Names (15 patterns)
```
api_key, apikey, app_secret, client_secret, secret,
password, token, access_token, refresh_token,
verification_token, encrypt_key, work_secret, jwt_secret,
private_key, connection_string
```

### Python (`mask_secrets` in `core/security/secrets.py`)
- Recursively processes `dict` and `list`
- Uses `***`
- Also runs `mask_api_key_for_settings` separately

### C# (`SecretMasker`)
- `Mask(ConfigNode)` — for `ConfigNode` tree
- `Mask(IReadOnlyDictionary<string, object?>)` — for generic dictionaries
- `Mask(IEnumerable<object?>)` — for sequences
- Returns new objects, never mutates originals
- `ToString()` does not leak secrets
- Exceptions do not leak secrets
- `MaskApiKeyForSettings(string?)`: null/empty → `{IsConfigured=false, DisplayValue=""}`, any value → `{IsConfigured=true, DisplayValue="***"}`

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
- Error code prefix (`YAML_PARSE_ERROR`, `CUSTOM_TAG`, `ANCHOR_NOT_ALLOWED`, etc.)
- `ConfigPath` property (dotted config key)
- `FilePath`, `LineNumber`, `ColumnNumber` for YAML parse errors
- YamlException raw message is NOT included to prevent secret leakage

---

## DI Registration

Method: `AddKejiConfigurationFoundation()`

Registers (all singleton, lazy factories):
- `KejiConfigurationLoadOptions` (eager, no file I/O)
- `IDotEnvStore` (lazy factory — file I/O on first resolution; **not** used by `IKejiConfigurationLoader`)
- `ISafeYamlConfigurationLoader` (eager, no file I/O)
- `IKejiConfigurationLoader` (lazy; only injects `ISafeYamlConfigurationLoader`, no file I/O on resolution)
- `ISecretMasker` (eager, no file I/O)

**Registration phase performs no file I/O.**
- `IKejiConfigurationLoader` service resolution performs no file I/O — `.env` only read inside `Load()`, `config.yaml` only read inside `Load()`
- `IDotEnvStore` (if resolved independently) reads `.env` on first resolution via lazy factory

**No auto-registration of `KejiConfigurationDocument` singleton.** Callers must explicitly call `loader.Load(options)`.
