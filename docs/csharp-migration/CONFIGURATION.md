# Configuration: Python vs C# Behavior

## Overview

This document describes behavioral differences between the Python and C# configuration implementations.

---

## Load Order

### Python
1. Parse `config.yaml` with PyYAML
2. Call `load_dotenv()` which writes into `os.environ`
3. Resolve `${ENV_VAR}` references via `os.environ`
4. Return flat settings dict

### C# (Keji.Configuration)
1. Probe project root for `config.yaml`
2. Parse `.env` file into internal `DotEnvStore` (not written to process environment)
3. Create two `IEnvironmentValueSource` instances:
   - `ProcessEnvironmentValueSource` (reads `Environment.GetEnvironmentVariable`)
   - `DotEnvEnvironmentValueSource` (reads `.env` store)
4. `CompositeEnvironmentValueSource` queries process first, then `.env`
5. `SafeYamlConfigurationLoader` parses `config.yaml` into `ConfigNode` tree with strict security
6. `EnvironmentReferenceResolver` walks tree and replaces `${VAR|default}` references
7. Return `KejiConfigurationDocument`

**Key difference:** Python's `load_dotenv()` mutates the process environment. C# keeps `.env` as an isolated internal store to avoid side effects.

---

## YAML Loading Security

| Concern | Python (PyYAML) | C# (SafeYamlConfigurationLoader) |
|---|---|---|
| Custom tags | Blocked via `SafeLoader` | Blocked — only basic scalars/maps/sequences |
| Anchors/aliases | Resolved safely by `SafeLoader` | Transparently resolved by YamlDotNet parser (safe) |
| Arbitrary object creation | Blocked via `SafeLoader` | Not possible — event-based parser, no deserializer |
| Max file size | Not enforced | Enforced via `MaxConfigFileBytes` (default 1 MB) |
| Max depth | Not enforced | Enforced via `MaxDepth` (default 32) |
| Max nodes | Not enforced | Enforced via `MaxNodeCount` (default 10,000) |
| Duplicate keys | Last wins | Last wins (case-insensitive key dedup) |

---

## Environment Variable References

### Syntax
Both support `${VAR_NAME}` and `${VAR_NAME|default_value}`.

### Behavior
- **Python:** Raises `KeyError` if variable is missing with no default.
- **C#:** Throws `KejiConfigurationException` when `FailOnMissingEnvironmentVariable` is true (default). When false, the raw `${VAR}` text is preserved.

### Resolution Order
**C#:** Process environment → `.env` file (opposite of Python if `python-dotenv` overwrites `os.environ`). C# composite gives process environment priority over `.env` for security.

---

## `.env` File Handling

### Python (`python-dotenv`)
- Calls `load_dotenv()` which reads `.env` and calls `os.environ[key] = value`
- Writes via `set_key()` using line-by-line replacement

### C# (`DotEnvStore`)
- Reads `.env` into internal `Dictionary<string, string>` (case-insensitive keys)
- `SetValue`/`RemoveValue` writes atomically via temp file + `File.Move`
- Reload method re-reads from disk
- Does NOT write to `Environment.SetEnvironmentVariable`

---

## Provider Secret Names

Both map provider names to environment variable names:

| Provider | Env Variable |
|---|---|
| `deepseek` | `DEEPSEEK_API_KEY` |
| `openai` | `OPENAI_API_KEY` |

C# uses a `FrozenDictionary` with `OrdinalIgnoreCase` comparer. Python uses a plain `dict` with lowercased keys.

---

## Secret Masking

### Behavior
Both mask the values of sensitive keys (case-insensitive) with `---`.

### Sensitive Key Names (17 patterns)
```
password, passwd, pwd, secret, api_key, apikey, api-key,
token, auth_token, authtoken, access_token, accesstoken,
private_key, privatekey, connection_string, connectionstring,
master_key, masterkey
```

### C# Differences
- Masking is recursive through `ConfigMap` and `ConfigSequence` children
- The masker returns a **new** `ConfigNode` tree; it never mutates the original
- Can be composed independently from the loader

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
- Optional `ConfigPath` property indicating the configuration key that caused the error
- Inner exception support for wrapping I/O errors

---

## DI Registration

### C# Only
`Microsoft.Extensions.DependencyInjection` integration via `AddKejiConfiguration()` extension:
- Registers `KejiConfigurationLoadOptions` (singleton)
- Registers `IDotEnvStore` (singleton)
- Registers `IEnvironmentValueSource` as `CompositeEnvironmentValueSource`
- Registers `IKejiConfigurationLoader` (singleton)
- Registers `ISecretMasker` (singleton)
- Registers `KejiConfigurationDocument` (singleton, auto-loaded at registration time)

Python has no DI container integration.
