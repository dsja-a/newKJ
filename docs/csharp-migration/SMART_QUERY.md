# Safe read-only SmartQuery

TASK-014 R2 accepted functional baseline: `6806886a0baa333ac3b833b6c995c1fef932b2f8`.

## Security flow

`IKejiSmartQuery.RunStreamAsync` is the only business chain. It validates the frozen four-field request and current user, requires both `SmartQueryExecute` and `DatabaseRead`, resolves a persisted user-accessible data source, and emits deterministic typed terminal events. `RunAsync` only consumes this stream and returns its terminal result. Provider, model, and summary policy come exclusively from server options.

The versioned Keji.Persistence catalog uses normalized data-source, allowed-schema, table, column, and foreign-key tables with constraints and indexes. It persists MySQL/PostgreSQL dialect, host, port, database, username, TLS policy, Secret Reference, visibility, and enabled metadata. It never stores a password or open connection. Strict `env:NAME` secrets are resolved asynchronously for each execution without caching or fallback.

The existing `IModelProvider` receives only bounded query-enabled, non-sensitive schema expressed as untrusted JSON data and must return a strongly typed `KejiSmartQueryPlan`, never SQL. Parsing and compilation reject markdown, raw SQL, unknown or duplicate properties, excessive depth/size, unknown enums, sensitive or disabled metadata, arbitrary joins, join cycles, disconnected tables, invalid aggregate/grouping combinations, and every configured limit violation. One bounded repair call is allowed after an invalid first plan.

## Deterministic SQL and execution

The independent MySQL and PostgreSQL dialects are the only SQL producers. They deterministically generate aliases, quote metadata-approved identifiers, allow only enabled foreign-key joins, map closed projection/aggregate/filter/group/sort contracts, bind every value and limit, and emit one `SELECT` without a raw SQL entry point. AND/OR filters are bounded to depth 4, 64 nodes, 32 leaves, 100 IN values, and 256 parameters; NOT, expressions, functions, fragments, and subqueries are absent from the contract.

The independent executors enforce TLS policy, disabled pooling, connection and command timeouts, a repeatable-read read-only transaction, sequential single-result reading, rollback, and asynchronous resource disposal. Columns, rows, cell bytes, and total result bytes are bounded.

Results use typed immutable values and rows. Optional summarization sends only a bounded result sample with a fixed instruction that treats cell contents as untrusted data. Audit metadata contains safe status, source ID, user ID, row count, and truncation only; it excludes questions, plans, SQL, parameters, rows, summaries, provider errors, exceptions, stacks, and connection references.

## Local acceptance evidence

- Keji.SmartQuery.Tests: 230/230
- Keji.Persistence.Tests: 181/181
- Keji.Integration.Tests: 216/216
- Keji.Agent.Tests: 197/197
- Keji.Providers.Tests: 202/202
- Keji.Streaming.Tests: 156/156
- Full solution: 2462/2462
- Failed/skipped/build warnings/build errors/known NuGet vulnerabilities: 0/0/0/0/0

This is local Gate evidence. No remote GitHub CI status was available.
