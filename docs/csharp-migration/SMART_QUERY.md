# Safe read-only SmartQuery

TASK-014 accepted functional baseline: `fc2b270902bbfc855f49d463872dac1a7988ee0e`.

## Security flow

`IKejiSmartQuery.ExecuteAsync` validates the bounded request and current user, requires both `SmartQueryExecute` and `DatabaseRead`, resolves a user-accessible data source through `IKejiSmartQueryDataSourceCatalog`, and accepts only administrator-enabled table metadata. Connection details remain behind an opaque connection reference and `IKejiSmartQueryConnectionFactory`.

The existing `IModelProvider` receives a bounded schema expressed as untrusted JSON data and must return a strongly typed `KejiSmartQueryPlan`, never SQL. Parsing rejects markdown, unknown properties, duplicate JSON properties, invalid UTF-16, excessive depth/size, unknown enum values, duplicate projections, unknown tables/columns, nested filter values, and every configured limit violation.

## Deterministic SQL and execution

`KejiSmartQueryCompiler` is the only SQL producer. It quotes metadata-approved identifiers, maps a closed filter/sort enum, binds every value and limit as a parameter, and has no raw SQL input. Execution opens a connection through the read-only factory, enables SQLite `PRAGMA query_only`, applies a command timeout, uses sequential single-result reading, and bounds columns, rows, cell bytes, and aggregate result bytes.

Results use typed immutable values and rows. Optional summarization sends only a bounded result sample with a fixed instruction that treats cell contents as untrusted data. Audit metadata contains safe status, source ID, user ID, row count, and truncation only; it excludes questions, plans, SQL, parameters, rows, summaries, provider errors, exceptions, stacks, and connection references.

## Local acceptance evidence

- Keji.SmartQuery.Tests: 31/31
- Keji.Integration.Tests: 189/189, including 5 real SmartQuery SQLite paths
- Keji.Agent.Tests: 197/197
- Keji.Providers.Tests: 202/202
- Keji.Streaming.Tests: 156/156
- Full solution: 2226/2226
- Failed/skipped/build warnings/build errors/known NuGet vulnerabilities: 0/0/0/0/0

This is local Gate evidence. No remote GitHub CI status was available.
