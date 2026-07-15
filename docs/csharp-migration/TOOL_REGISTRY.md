# Tool Contracts and Frozen Registry

## Design

Tools are defined by immutable C# contracts at compile time. No executable code, delegates, MethodInfo, or reflection is involved. The registry is two-phase: a mutable builder is used during startup, then frozen into a read-only singleton for the application lifetime.

## Two-phase registry

```
Startup: KejiToolRegistryBuilder.Register(...)  ← BuiltInToolCatalog
         ↓
Runtime: IKejiToolRegistry / KejiFrozenToolRegistry  (immutable, concurrent-safe)
```

- `KejiToolRegistryBuilder`: call `Register()` once per tool, then `Build()` exactly once. Duplicate names throw `KejiToolContractException`, double-build throws `InvalidOperationException`.
- `KejiFrozenToolRegistry`: internal class implementing `IKejiToolRegistry`. Uses `FrozenDictionary` for name lookups with `StringComparer.Ordinal`. `GetAll()` returns a sorted copy.
- `IKejiToolRegistry`: read-only interface with `Resolve(KejiToolName)`, `Contains(KejiToolName)`, `GetAll()`. No `Execute`, `Invoke`, `Run`, or `Start` methods. Registered does not imply authorized; authorized does not imply executable.

## Tool name (`KejiToolName`)

- Pattern: `^[a-z][a-z0-9_]{0,63}$` (1–64 chars, lowercase ASCII start, ASCII letters/digits/underscores only).
- No trim, no auto-lowercase, no fuzzy match. `StringComparer.Ordinal` throughout.
- `TryCreate(string, out KejiToolName?)` with `[NotNullWhen(true)]`.

## Definitions

### `KejiToolDefinition`

| Property | Validation |
|---|---|
| `Name` | `KejiToolName` (must not be null) |
| `Category` | `Enum.IsDefined` |
| `RiskLevel` | `Enum.IsDefined` |
| `ExecutionTarget` | `Enum.IsDefined` |
| `Availability` | `Enum.IsDefined` |
| `ContractVersion` | ≥1 |
| `Description` | 1–2000 chars, non-empty |
| `Tags` | ≤16 items, each non-null, non-empty, 1–32 chars, no control chars. Stored as `FrozenSet<string>`. |
| `RequiredPermission` | Must pass `KejiPermissionCatalog.IsDefined()` |
| `InputSchema` | `KejiToolInputSchema` (nullable → empty). Null parameters in schema rejected. |
| `IsDeterministic` | Boolean |
| `SupportsCancellation` | Boolean |

### `KejiToolParameterDefinition`

| Property | Validation |
|---|---|
| `Name` | Non-empty, ≤64 chars, lowercase+digits+underscores |
| `Type` | `Enum.IsDefined` (rejects 0, negative, out-of-range) |
| `Required` | Boolean |
| `Description` | 1–2000 chars |
| `DefaultValue` | Deep immutable snapshot for array types (`string[]`, `int[]`) |
| `Minimum` / `Maximum` | Only for Integer, Number. Mutually checked (Min ≤ Max). |
| `MinLength` / `MaxLength` | Only for String. MaxLength required (1–100000). MinLength ≥ 0. |
| `AllowedValues` | Only for String. Deduplicated via `StringComparer.Ordinal`. Non-empty. |
| `MaxItems` | Required for StringArray, IntegerArray. > 0. |
| `Sensitive` | If true, validator returns generic error messages (no value leak). |

**Constraint type matching:**
- `String` — only MinLength, MaxLength (required), AllowedValues. No Minimum/Maximum/MaxItems.
- `Integer` — only Minimum, Maximum. No string/array constraints or AllowedValues.
- `Number` — only Minimum, Maximum. No string/array constraints or AllowedValues.
- `Boolean` — no constraints at all. No AllowedValues.
- `StringArray` — only MaxItems (required). No scalar constraints or AllowedValues.
- `IntegerArray` — only MaxItems (required). No scalar constraints or AllowedValues.

### `KejiToolInputSchema`

- Null constructor arg → `Array.Empty<KejiToolParameterDefinition>()` (truly immutable).
- Non-null → `parameters.ToArray()` (preserves insertion order, immutable snapshot).
- Null parameter in list throws `KejiToolContractException`.
- Duplicate parameter names (Ordinal) throw `KejiToolContractException`.

## Validation (`KejiToolInputValidator`)

Static `Validate(IKejiToolRegistry, string?, IReadOnlyDictionary<string, object?>?)`:

1. Validate name format via `KejiToolName.TryCreate`.
2. Resolve from registry.
3. Check required params.
4. Match CLR types:
   - String → `string`
   - Integer → `int` or `long` (both checked for Min/Max)
   - Number → `int`, `long`, `float`, `double` (NaN/Infinity rejected)
   - Boolean → `bool`
   - StringArray → `IReadOnlyList<string>` (all non-null)
   - IntegerArray → `IReadOnlyList<int>` or `IReadOnlyList<long>` (MaxItems checked)
5. Check constraints (min/max, item count, allowed values).
6. Sensitive-parameter-safe error messages (don't leak parameter name or value).

Returns `KejiToolValidationResult` via `Valid()` / `Invalid(error, message)` factory methods. `KejiToolValidationError` enum with 9 codes. First-error-only (early-exit).

## Built-in tool catalog

47 tools defined in `BuiltInToolCatalog`. All `KejiToolAvailability.ContractOnly`. No executable code. TASK-011 (ToolWorker) has not started.

### Python baseline mapping

| # | C# Tool Name | Python Source File | Category | RiskLevel | Permission | ExecutionTarget | Security Tightening |
|---|---|---|---|---|---|---|---|
| 1 | `read_file` | `core/tools.py`, `nanobot/agent/tools/filesystem.py` | FileRead | ReadOnly | FileRead | Host | path MaxLength=1024, offset/limit bounds |
| 2 | `write_file` | `nanobot/agent/tools/filesystem.py` | FileWrite | Mutating | FileWrite | Host | path MaxLength=1024, content MaxLength=100000 |
| 3 | `edit_file` | `nanobot/agent/tools/filesystem.py` | FileWrite | Mutating | FileWrite | Host | all text MaxLength=100000 |
| 4 | `list_dir` | `nanobot/agent/tools/filesystem.py` | FileRead | ReadOnly | FileRead | Host | path MaxLength=1024 |
| 5 | `glob` | `nanobot/agent/tools/search.py` | FileRead | ReadOnly | FileRead | Host | pattern MaxLength=256, path MaxLength=1024 |
| 6 | `grep` | `nanobot/agent/tools/search.py` | FileRead | ReadOnly | FileRead | Host | pattern MaxLength=2048, path MaxLength=1024, glob MaxLength=256 |
| 7 | `browse_files` | `core/new_tools.py` | FileRead | ReadOnly | FileRead | Host | path MaxLength=1024 |
| 8 | `search_files` | `core/new_tools.py` | FileRead | ReadOnly | FileRead | Host | pattern MaxLength=256, folder MaxLength=1024 |
| 9 | `list_allowed_directories` | `core/new_tools.py` | FileRead | ReadOnly | FileRead | Host | No params |
| 10 | `verify_output` | `core/new_tools.py` | FileRead | ReadOnly | FileRead | Host | path MaxLength=1024 |
| 11 | `read_document` | `core/new_tools.py` | Document | ReadOnly | FileRead | PythonWorker | path MaxLength=1024 |
| 12 | `create_folder` | `core/new_tools.py` | FileWrite | Mutating | FileWrite | Host | path MaxLength=1024 |
| 13 | `delete_file` | `core/new_tools.py` | FileWrite | Mutating | FileWrite | Host | path MaxLength=1024, confirm required |
| 14 | `rename_files` | `core/filetools_organize.py` | FileWrite | Mutating | FileWrite | PythonWorker | directory MaxLength=1024, pattern 256, value 256 |
| 15 | `organize_files` | `core/filetools_organize.py` | FileWrite | Mutating | FileWrite | PythonWorker | source_dir MaxLength=1024, mode MaxLength=128 |
| 16 | `deduplicate_files` | `core/filetools_organize.py` | FileWrite | Mutating | FileWrite | PythonWorker | directory MaxLength=1024 |
| 17 | `analyze_data` | `core/new_tools.py` | DataRead | ReadOnly | FileRead | PythonWorker | data_source MaxLength=1024 |
| 18 | `knowledge_stats` | `core/new_tools.py` | DataRead | ReadOnly | KnowledgeRead | Host | No params |
| 19 | `format_data` | `core/new_tools.py` | DataWrite | Mutating | FileWrite | PythonWorker | data MaxLength=100000, operation MaxLength=256 |
| 20 | `clean_data` | `core/filetools_organize.py` | DataWrite | Mutating | FileWrite | PythonWorker | source MaxLength=1024, operations MaxLength=2048 |
| 21 | `convert_data` | `core/filetools_organize.py` | DataWrite | Mutating | FileWrite | PythonWorker | source MaxLength=1024, target_format MaxLength=128 |
| 22 | `etl_pipeline` | `core/filetools_organize.py` | DataWrite | Mutating | FileWrite | PythonWorker | source MaxLength=1024, steps MaxLength=100000 |
| 23 | `query_knowledge` | `core/new_tools.py` | Knowledge | ReadOnly | KnowledgeRead | Host | query MaxLength=4000, n_results bounded 1-50 |
| 24 | `index_knowledge` | `core/new_tools.py` | Knowledge | Mutating | KnowledgeWrite | Host | path MaxLength=1024 |
| 25 | `remove_from_knowledge` | `core/new_tools.py` | Knowledge | Mutating | KnowledgeWrite | Host | name MaxLength=256 |
| 26 | `create_document` | `core/new_tools.py` | Office | ExternalSideEffect | FileWrite | PythonWorker | title MaxLength=256, save_path MaxLength=1024 |
| 27 | `create_table` | `core/new_tools.py` | Office | ExternalSideEffect | FileWrite | PythonWorker | headers MaxLength=2048, save_path MaxLength=1024 |
| 28 | `create_presentation` | `core/new_tools.py` | Office | ExternalSideEffect | FileWrite | PythonWorker | title MaxLength=256, save_path MaxLength=1024 |
| 29 | `browse_archive` | `core/archive_tools.py` | Archive | ReadOnly | FileRead | PythonWorker | path MaxLength=1024 |
| 30 | `extract_archive` | `core/archive_tools.py` | Archive | Mutating | FileWrite | PythonWorker | path MaxLength=1024, output_dir MaxLength=1024 |
| 31 | `create_archive` | `core/archive_tools.py` | Archive | Mutating | FileWrite | PythonWorker | sources MaxLength=100000, output_path MaxLength=1024 |
| 32 | `parse_email` | `core/email_tools.py` | Email | ReadOnly | FileRead | PythonWorker | path MaxLength=1024 |
| 33 | `batch_parse_emails` | `core/email_tools.py` | Email | ReadOnly | FileRead | PythonWorker | directory MaxLength=1024 |
| 34 | `extract_email_attachments` | `core/email_tools.py` | Email | Mutating | FileWrite | PythonWorker | path MaxLength=1024, output_dir MaxLength=1024 |
| 35 | `ocr_image` | `core/ocr_tools.py` | Utility | ReadOnly | FileRead | PythonWorker | path MaxLength=1024, lang MaxLength=32 |
| 36 | `ocr_pdf` | `core/ocr_tools.py` | Utility | ReadOnly | FileRead | PythonWorker | path MaxLength=1024, lang MaxLength=32 |
| 37 | `ocr_batch` | `core/ocr_tools.py` | Utility | ReadOnly | FileRead | PythonWorker | directory MaxLength=1024 |
| 38 | `db_connect` | `core/db_tools.py` | Database | ExternalSideEffect | DatabaseManage | PythonWorker | All string params bounded (64-512), password Sensitive |
| 39 | `db_list_tables` | `core/db_tools.py` | Database | ReadOnly | DatabaseRead | PythonWorker | connection_id MaxLength=128 |
| 40 | `db_describe_table` | `core/db_tools.py` | Database | ReadOnly | DatabaseRead | PythonWorker | connection_id MaxLength=128, table_name MaxLength=256 |
| 41 | `db_test_connection` | `core/db_tools.py` | Database | ReadOnly | DatabaseRead | PythonWorker | All string params bounded, password Sensitive |
| 42 | `db_disconnect` | `core/db_tools.py` | Database | ReadOnly | DatabaseRead | PythonWorker | connection_id MaxLength=128 |
| 43 | `get_time` | `core/tools.py` | Utility | ReadOnly | ToolCatalogRead | Host | No params |
| 44 | `calculator` | `core/tools.py` | Utility | ReadOnly | ToolCatalogRead | Host | expr MaxLength=2000 |
| 45 | `web_search` | `core/tools.py`, `nanobot/agent/tools/web.py` | Network | ExternalSideEffect | ToolExecuteRead | Host | query MaxLength=4000, max_results bounded 1-20 |
| 46 | `web_fetch` | `nanobot/agent/tools/web.py` | Network | ExternalSideEffect | ToolExecuteRead | Host | url MaxLength=4096 |
| 47 | `selfcheck_run` | `nanobot/agent/tools/selfcheck.py` | System | ReadOnly | SystemRead | Host | scope AllowedValues (full, tools, mcp, database), MaxLength=128 |

### C# migration infrastructure contracts (no Python equivalent as a named tool)

These are modeled after Python patterns but are C#-specific infrastructure contracts:
- `execute_query` — intentionally excluded (RejectedLegacyCapability).
- `exec`, `run_code`, `cron`, `spawn`, `notebook_edit`, `my` — intentionally excluded (RejectedLegacyCapability).
- `message`, `ask_user`, `mcp_*` — will be introduced in TASK-011/TASK-015.

### Excluded high-risk tools (RejectedLegacyCapability)

| Tool | Python Source | Reason |
|---|---|---|
| `exec` | `nanobot/agent/tools/shell.py` | Arbitrary shell command execution |
| `run_code` | `core/new_tools.py` | Arbitrary Python code execution |
| `db_execute_query` | `core/db_tools.py` | Arbitrary SQL execution |
| `cron` | `nanobot/agent/tools/cron.py` | Background process scheduling |
| `spawn` | `nanobot/agent/tools/spawn.py` | Subagent spawning |
| `notebook_edit` | `nanobot/agent/tools/notebook.py` | Interactive code execution |
| `my` | `nanobot/agent/tools/self.py` | Runtime state inspection/mutation |

## DI registration

```csharp
services.AddKejiToolRegistry();  // registers IKejiToolRegistry as singleton
```

## Security properties

- All 47 tools have `Availability = ContractOnly` (no executable code). TASK-011 (ToolWorker) has not started.
- Registry is immutable after startup — no hot-reload or dynamic registration path.
- Only compile-time trusted C# code can register tools (`BuiltInToolCatalog`).
- Python baseline high-risk tools are explicitly excluded from the catalog.
- Registered does not imply authorized; authorized does not imply executable.
- All string parameters have explicit MaxLength. All array parameters have explicit MaxItems.
- `ParameterType` enum validated at construction (rejects 0, -1, 999).
- Constraint type matching enforced: strings only string constraints, integers only numeric constraints, etc.
- Deep immutability: `DefaultValue` for arrays is a snapshot (`ToArray()`), `Tags` is `FrozenSet<string>`, schema parameters are `ToArray()`.
- Empty schema uses `Array.Empty<KejiToolParameterDefinition>()`.
- Input schema preserves insertion order.
- NuGet vulnerabilities: 0.
