---
type: Architecture
title: Data Source Abstraction
description: >-
  IDataSourceProvider/IDataSourceProviderResolver — the generic data-source seam the backend
  queries through, what it covers, and what it deliberately doesn't (yet).
tags: [architecture, data-source, postgres, dip]
timestamp: 2026-09-22T00:00:00Z
---

# Data Source Abstraction

## 1. Why this exists

The application depends on `IDataSourceProvider`/`IDataSourceProviderResolver`, not directly on
PostgreSQL connection types: `DynamicExportService` never builds or opens an `NpgsqlConnection`
itself, and `ConnectionEndpoints` never runs an `information_schema` query against one directly.
Every caller instead resolves a provider and executes through it. This is a plain DIP seam — the
application depends on an interface, not a concrete database client — introduced so a second data
source could be added later without changing every call site, while leaving current PostgreSQL
behavior unchanged.

## 2. The interface

```csharp
namespace Connector.Core.DataSources;

public interface IDataSourceProvider
{
    DataSourceType Type { get; }

    Task<TestConnectionResult> TestConnectionAsync(DataSourceConfig config, CancellationToken cancellationToken);
    Task<SourceSchema> ReadSchemaAsync(DataSourceConfig config, CancellationToken cancellationToken);
    Task<QueryResult> ExecuteAsync(DataSourceConfig config, SourceQuery query, CancellationToken cancellationToken);
    Task<QueryResult> ExecuteNativeAsync(DataSourceConfig config, NativeSqlQuery query, CancellationToken cancellationToken);
}

public interface IDataSourceProviderResolver
{
    IDataSourceProvider Resolve(DataSourceType type);
}
```

Both live in `Connector.Core.DataSources` — `Connector.Core` has no `Npgsql` package reference, and
none of these types (or `DataSourceConfig`/`SourceSchema`/`SourceQuery`/`NativeSqlQuery`/`QueryResult`) mention
`Npgsql` anywhere in their own signatures. `DataSourceProviderResolver` (`Connector.Infrastructure`)
resolves by each DI-registered provider's own `Type` (`IEnumerable<IDataSourceProvider>`
injection), so adding a second provider is a DI registration, never a change to the resolver
itself.

**Supporting types**, also in `Connector.Core.DataSources`:

| Type | Purpose |
|---|---|
| `DataSourceType` | `PostgreSql` (implemented), `MariaDb`/`ServiceNowTableApi`/`ServiceNowSqlApi` (deliberately not — see §5) |
| `DataSourceConfig` | Generic connection parameters (`Type` + relational Host/Port/Database or HTTP-API InstanceUrl + Username/Password/SslMode). See [Data Source Configuration](/architecture/data-source-configuration.md) for the full shape. Persisted under the same `AppSettings` storage key as always. |
| `SourceSchema`/`SourceTable`/`SourceColumn` | The provider's schema-read result — the interface's own return type, mirroring `Connector.Api/Dtos.cs`'s `SourceSchemaDto`/`SourceTableDto`/`SourceColumnDto` shape (no parallel model, no API change). |
| `TestConnectionResult` | `Success`/`Schema`/`Error` — a connection-test failure is reported here, sanitized, never thrown as a raw exception a caller might leak (credentials) by accident. |
| `SourceQuery` | The database-neutral query model (root table, columns, joins, conditions, limit) — see [Source Query Model](/architecture/source-query-model.md). |
| `NativeSqlQuery` | Provider-native SQL text + optional named parameters + optional command timeout. The provider does not parse or understand this text — see §3. |
| `QueryResult` | Generic rows: `Columns` plus `Rows` whose `Values` align with them (`ToDictionaries()` gives name-keyed rows). Every value is already stringified by the provider (see §3); `null` means the source column was `NULL`. |
| `DataSourceQueryException` | Wraps a provider-specific query failure a caller needs to *inspect*, not just log — e.g. a SQL error code — without the caller referencing a provider SDK type. `ErrorCode` carries that code verbatim (Postgres's `SQLSTATE`, for `PostgreSqlDataSourceProvider`). |
| `UnsupportedDataSourceException` | Thrown by `Resolve` for a `DataSourceType` with no registered provider. |

## 3. What's abstracted, and what deliberately isn't

**In `PostgreSqlDataSourceProvider` (`Connector.Infrastructure`), the one registered implementation:**

- `BuildConnectionString`/`ParseSslMode` — building an `NpgsqlConnectionStringBuilder` from a `DataSourceConfig`.
- The `information_schema` schema-introspection query (`ReadSchemaAsync`).
- Neutral query execution (`ExecuteAsync`) — validates a `SourceQuery` against the live schema,
  compiles it with `PostgreSqlQueryCompiler`, and runs it (see [Source Query Model](/architecture/source-query-model.md)).
- Native SQL execution (`ExecuteNativeAsync`) — opens a connection, runs `NativeSqlQuery.Sql` with its
  parameters, and materializes rows generically. This is where the
  Postgres-specific date/timestamp → ISO-8601 stringification rule lives.

**Deliberately *not* abstracted — SQL dialect generation stays in `DynamicExportService`:**

The `json_build_object`/`json_agg`/`string_agg`/`array_agg`/`::text`-cast/double-quote-identifier
SQL text `DynamicExportService` builds is Postgres-specific, and lives in
`DynamicExportService.LegacyMapping.cs`/`.ExportNode.cs`. `IDataSourceProvider` does not understand
or generate SQL — it only *executes* a `NativeSqlQuery.Sql` string handed to it and returns rows
generically. This is a deliberate scope boundary, not an oversight: abstracting the SQL dialect
itself (so a second provider could generate its own native JSON-aggregation syntax) is real future
work — a second provider today would need `ExecuteNativeAsync` to accept Postgres-flavored SQL it can't
actually run, which is exactly why `MariaDb`/`ServiceNowTableApi`/`ServiceNowSqlApi` stay
unimplemented rather than half-implemented.

## 4. What calls the abstraction

| Caller | What it does |
|---|---|
| `DynamicExportService.BuildExportAsync`/`ExecuteQueryAsync`/`ExecuteNestedJsonQueryAsync`/`ExecuteExportNodeQueryAsync`/`BuildExportNodeAsync` | Build Postgres SQL text, execute it via `provider.ExecuteNativeAsync`, convert `QueryResult` back into the same `List<Dictionary<string,string>>`/`List<JsonObject>` shapes callers expect. |
| `ExportWorker`, `ExportDefinitionRunner` (+ `ExportDefinitionWorker`), `PipelineEndpoints`' three handlers, `ExportDefinitionEndpoints`' preview handler | Resolve a provider via `IDataSourceProviderResolver.Resolve(config.Type)` instead of building their own `NpgsqlConnection`. |
| `ConnectionEndpoints`' `POST /api/connection`, `GET /api/source-schema` | Call `provider.TestConnectionAsync`/`ReadSchemaAsync` directly. |
| `ImportDefinitionEndpoints`' save-time `AllowedWritableColumns` validator | Calls `provider.ReadSchemaAsync` directly. |

## 5. What's still direct `NpgsqlConnection` — and why

`ImportNodeWalker` and `ImportRunReleaser` (`Connector.Infrastructure`) still build and use
`NpgsqlConnection` directly. This is a deliberate, documented scope boundary, not an oversight:

- **`ImportNodeWalker`** only ever issues read-only `SELECT`s per its own doc comment, so it *could*
  plausibly route through `IDataSourceProvider.ExecuteAsync` with a neutral `SourceQuery` — but touching the import read path
  adds risk to the write-back walk logic for no behavior change, and abstracting it wasn't required
  for the current provider seam.
- **`ImportRunReleaser`** is the harder case: `ReleaseAsync` commits an approved import's field-level
  diff as one atomic multi-statement transaction (a conditional `UPDATE` per row, guarded by every
  row's expected old values, all-or-nothing per the four-eyes commit contract). The given
  `IDataSourceProvider.ExecuteAsync`/`ExecuteNativeAsync(config, query, ct)` shape is a single query in, single
  `QueryResult` out — it has no concept of a caller-managed transaction spanning multiple
  statements. Modeling that without inventing a second, parallel execution entry point is real
  design work, and `ImportRunReleaser` is the single highest-risk write path in the whole system:
  it's the only code that writes to the customer's ERP database at all, under the four-eyes
  approval guarantee. Changing its connection handling without a corresponding transaction-aware
  interface extension isn't worth the risk this abstraction is meant to reduce.

Both keep working exactly as any direct caller would — `PostgreSqlDataSourceProvider.BuildConnectionString`
(public static) is the one place they get an Npgsql connection string from, so there's no
duplicated connection-string-building logic even though the connections themselves aren't
provider-abstracted.

## 6. Adding a second provider

To add a real (not placeholder) `MariaDb` or ServiceNow provider:

1. Implement `IDataSourceProvider` for it. `TestConnectionAsync`/`ReadSchemaAsync` are
   straightforward — connect, introspect, return `SourceSchema`. `ExecuteAsync` needs a compiler from
   the neutral `SourceQuery` to its own dialect (see [Source Query Model](/architecture/source-query-model.md));
   `ExecuteNativeAsync` is the hard one: it must run whatever `NativeSqlQuery.Sql` it's handed, which today is always Postgres-dialect
   SQL from `DynamicExportService`. A second provider is only genuinely usable once
   `DynamicExportService`'s SQL-building (§3) also becomes dialect-aware — otherwise it can only
   ever receive SQL it can't run.
2. Register it in `Program.cs` (`builder.Services.AddSingleton<IDataSourceProvider, YourProvider>()`)
   — `DataSourceProviderResolver` picks it up automatically via `IEnumerable<IDataSourceProvider>`.
3. No resolver code changes, no `DynamicExportService` signature changes — those are already
   provider-agnostic.

## 7. Tests

- `DataSourceProviderResolverTests` — provider resolution (`PostgreSql` resolves to the registered
  provider) and the unsupported-provider error scenario (`MariaDb`/`ServiceNowTableApi`/`ServiceNowSqlApi`/an
  out-of-range numeric type/no providers at all throw `UnsupportedDataSourceException` carrying the
  requested type).
- `PostgreSqlDataSourceProviderTests` — `TestConnectionAsync` (success + sanitized failure),
  `ReadSchemaAsync` (tables/PK/FK/generated-column shape), `ExecuteAsync` (neutral `SourceQuery`
  with projection, join, `IN`, null check, LIKE, limit; unknown column rejected), `ExecuteNativeAsync` (flat select, null
  handling, date coercion, `json_build_object` aggregation, SQLSTATE 21000 cardinality-violation
  mapping to `DataSourceQueryException`), plus `BuildConnectionString` unit tests
  (connection-string-injection safety, SslMode handling). Real-Postgres, same "no-op if `testdb`
  isn't running" convention as every other Postgres-backed test in this project.
- The full pre-existing test suite (`DynamicExportServiceFlatQueryPostgresTests`,
  `DynamicExportServiceNestedJsonPostgresTests`, `ExportNodeQueryPostgresTests`, and the rest) still
  passes unchanged against a real PostgreSQL instance — confirming export/import behavior is
  unaffected by querying through the abstraction.
