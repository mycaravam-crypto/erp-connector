---
type: Architecture
title: Data Source Abstraction
description: >-
  IDataSourceProvider/IDataSourceProviderResolver — the generic data-source seam introduced in
  Arbeitsauftrag 2, what it covers, and what it deliberately doesn't (yet).
tags: [architecture, data-source, postgres, dip]
timestamp: 2026-09-22T00:00:00Z
---

# Data Source Abstraction

## 1. Why this exists

Before this change, "the ERP database" meant "PostgreSQL" everywhere in the backend:
`DynamicExportService` built and opened a concrete `NpgsqlConnection` itself, `ConnectionEndpoints`
ran its own `information_schema` query against one, and every caller in between repeated the same
`new NpgsqlConnection(BuildConnectionString(cfg))` pattern. [export-definitions-2.0.md](/pipeline/export-definitions-2.0.md)
§8 flagged this as a DIP violation early on but deliberately left it unfixed under YAGNI — introduce
a connection-provider abstraction only if per-export connection sourcing becomes a real need.

Arbeitsauftrag 2 is that need becoming real: a generic `IDataSourceProvider` seam so the application
no longer depends directly on PostgreSQL connection types, **without changing PostgreSQL behavior**.
This page documents the result.

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

Both live in `Connector.Core.DataSources` — `Connector.Core` still has no `Npgsql` package reference,
and none of these types (or `DataSourceConfig`/`SourceSchema`/`SourceQuery`/`NativeSqlQuery`/`QueryResult`) mention
`Npgsql` anywhere in their own signatures. `DataSourceProviderResolver` (`Connector.Infrastructure`)
resolves by each DI-registered provider's own `Type` (`IEnumerable<IDataSourceProvider>` injection),
so adding a second provider later is a DI registration, never a change to the resolver itself.

**Supporting types**, also in `Connector.Core.DataSources`:

| Type | Purpose |
|---|---|
| `DataSourceType` | `PostgreSql` (implemented), `MariaDb`/`ServiceNowTableApi`/`ServiceNowSqlApi` (deliberately not — see §5) |
| `DataSourceConfig` | Generic connection parameters (`Type` + relational Host/Port/Database or HTTP-API InstanceUrl + Username/Password/SslMode). Renamed from `ErpConnectionConfig` in Arbeitsauftrag 2, generalized from a positional record to an init-only one in Arbeitsauftrag 3 — see [Data Source Configuration](/architecture/data-source-configuration.md) for the current shape and its back-compat contract. Same `AppSettings` storage key throughout. |
| `SourceSchema`/`SourceTable`/`SourceColumn` | The provider's schema-read result — moved down from `Connector.Api/Dtos.cs`'s `SourceSchemaDto`/`SourceTableDto`/`SourceColumnDto` (same shape, no parallel model, no API change) since it's the interface's own return type, not an API-only shape. |
| `TestConnectionResult` | `Success`/`Schema`/`Error` — a connection-test failure is reported here, sanitized, never thrown as a raw exception a caller might leak (credentials) by accident. |
| `SourceQuery` | Arbeitsauftrag 4: the database-neutral query model (root table, columns, joins, conditions, limit) — see [Source Query Model](/architecture/source-query-model.md). |
| `NativeSqlQuery` | Provider-native SQL text + optional named parameters + optional command timeout (named `SourceQuery` before Arbeitsauftrag 4). The provider does not parse or understand this text — see §3. |
| `QueryResult` | Generic rows: `Columns` plus `Rows` whose `Values` align with them (Arbeitsauftrag 4; previously name-keyed dictionaries, still available via `ToDictionaries()`). Every value is already stringified by the provider (see §3); `null` means the source column was `NULL`. |
| `DataSourceQueryException` | Wraps a provider-specific query failure a caller needs to *inspect*, not just log — e.g. a SQL error code — without the caller referencing a provider SDK type. `ErrorCode` carries that code verbatim (Postgres's `SQLSTATE`, for `PostgreSqlDataSourceProvider`). |
| `UnsupportedDataSourceException` | Thrown by `Resolve` for a `DataSourceType` with no registered provider. |

## 3. What moved, and what deliberately didn't

**Moved into `PostgreSqlDataSourceProvider` (`Connector.Infrastructure`):**

- `BuildConnectionString`/`ParseSslMode` — building an `NpgsqlConnectionStringBuilder` from a `DataSourceConfig`.
- The `information_schema` schema-introspection query (`ReadSchemaAsync`) — previously
  `ConnectionEndpoints.IntrospectSchemaAsync`.
- Generic SQL execution (`ExecuteNativeAsync`, `ExecuteAsync` before Arbeitsauftrag 4) — opens a connection, runs `NativeSqlQuery.Sql` with its
  parameters, and materializes rows as string-keyed dictionaries. This is where the
  Postgres-specific date/timestamp → ISO-8601 stringification rule now lives (unchanged behavior,
  just relocated from the old inline reader loops in `DynamicExportService`/`ConnectionEndpoints`).

**Deliberately *not* abstracted — SQL dialect generation stays in `DynamicExportService`:**

The `json_build_object`/`json_agg`/`string_agg`/`array_agg`/`::text`-cast/double-quote-identifier SQL
text `DynamicExportService` builds is still Postgres-specific, and still lives in
`DynamicExportService.LegacyMapping.cs`/`.ExportNode.cs` exactly as before. `IDataSourceProvider`
does not understand or generate SQL — it only *executes* a `NativeSqlQuery.Sql` string handed to it and
returns rows generically. This is a deliberate scope boundary from Arbeitsauftrag 2 itself: "Keine
neue Export-Pipeline erstellen" and "Bestehendes Exportverhalten muss unverändert bleiben." Abstracting
the SQL dialect itself (so a second provider could generate its own native JSON-aggregation syntax)
is real future work, not something this change attempts — a second provider today would need
`ExecuteNativeAsync` to accept Postgres-flavored SQL it can't actually run, which is exactly why
`MariaDb`/`ServiceNowTableApi`/`ServiceNowSqlApi` stay unimplemented rather than half-implemented.

## 4. What calls the abstraction today

| Caller | What it does |
|---|---|
| `DynamicExportService.BuildExportAsync`/`ExecuteQueryAsync`/`ExecuteNestedJsonQueryAsync`/`ExecuteExportNodeQueryAsync`/`BuildExportNodeAsync` | Build Postgres SQL text (unchanged), execute it via `provider.ExecuteNativeAsync`, convert `QueryResult` back into the same `List<Dictionary<string,string>>`/`List<JsonObject>` shapes callers already expected. |
| `ExportWorker`, `ExportDefinitionRunner` (+ `ExportDefinitionWorker`), `PipelineEndpoints`' three handlers, `ExportDefinitionEndpoints`' preview handler | Resolve a provider via `IDataSourceProviderResolver.Resolve(config.Type)` instead of building their own `NpgsqlConnection`. |
| `ConnectionEndpoints`' `POST /api/connection`, `GET /api/source-schema` | Call `provider.TestConnectionAsync`/`ReadSchemaAsync` directly. |
| `ImportDefinitionEndpoints`' save-time `AllowedWritableColumns` validator | Calls `provider.ReadSchemaAsync` directly (previously called `ConnectionEndpoints.IntrospectSchemaAsync` with its own `NpgsqlConnection`). |

## 5. What's still direct `NpgsqlConnection` — and why

`ImportNodeWalker` and `ImportRunReleaser` (`Connector.Infrastructure`) still build and use
`NpgsqlConnection` directly. This is a deliberate, documented scope boundary, not an oversight:

- **`ImportNodeWalker`** only ever issues read-only `SELECT`s per its own doc comment, so it *could*
  plausibly route through `IDataSourceProvider.ExecuteNativeAsync` (or, better, `ExecuteAsync` with a neutral `SourceQuery`) — but doing so wasn't required by
  Arbeitsauftrag 2's Definition of Done (which names `DynamicExportService` specifically), and
  touching the import read path adds risk to Phase 17's Slice 2 walk logic for no behavior change.
- **`ImportRunReleaser`** is the harder case: `ReleaseAsync` commits an approved import's field-level
  diff as one atomic multi-statement transaction (a conditional `UPDATE` per row, guarded by every
  row's expected old values, all-or-nothing per the four-eyes commit contract). The given
  `IDataSourceProvider.ExecuteAsync`/`ExecuteNativeAsync(config, query, ct)` shape is a single query in, single
  `QueryResult` out — it has no concept of a caller-managed transaction spanning multiple
  statements. Modeling that without inventing a second, parallel execution entry point (which
  Arbeitsauftrag 2 explicitly rules out — "Keine neue Export-Pipeline erstellen" extends in spirit to
  not silently growing the interface either) is real design work, and `ImportRunReleaser` is the
  single highest-risk write path in the whole system: it's the only code that writes to the
  customer's ERP database at all, under the four-eyes approval guarantee. Changing its connection
  handling without a corresponding transaction-aware interface extension was judged not worth the
  risk for this work package.

Both keep working exactly as before — `PostgreSqlDataSourceProvider.BuildConnectionString` (public
static) is the one place they still get an Npgsql connection string from, so there's no duplicated
connection-string-building logic even though the connections themselves aren't provider-abstracted.

## 6. Extending with a second provider

To add a real (not placeholder) `MariaDb` or ServiceNow provider:

1. Implement `IDataSourceProvider` for it. `TestConnectionAsync`/`ReadSchemaAsync` are
   straightforward — connect, introspect, return `SourceSchema`. `ExecuteAsync` needs a compiler from the
   neutral `SourceQuery` to its own dialect (see [Source Query Model](/architecture/source-query-model.md));
   `ExecuteNativeAsync` is the hard one: it must run whatever `NativeSqlQuery.Sql` it's handed, which today is always Postgres-dialect
   SQL from `DynamicExportService`. A second provider is only genuinely usable once
   `DynamicExportService`'s SQL-building (§3) also becomes dialect-aware — otherwise it can only ever
   receive SQL it can't run.
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
  `ReadSchemaAsync` (tables/PK/FK/generated-column shape), `ExecuteNativeAsync` (flat select, null
  handling, date coercion, `json_build_object` aggregation, SQLSTATE 21000 cardinality-violation
  mapping to `DataSourceQueryException`), plus the relocated `BuildConnectionString` unit tests
  (connection-string-injection safety, SslMode handling). Real-Postgres, same
  "no-op if `testdb` isn't running" convention as every other Postgres-backed test in this project.
- The full pre-existing test suite (`DynamicExportServiceFlatQueryPostgresTests`,
  `DynamicExportServiceNestedJsonPostgresTests`, `ExportNodeQueryPostgresTests`, and the rest) still
  passes unchanged against a real PostgreSQL instance — the load-bearing evidence that wiring
  `DynamicExportService` through the abstraction didn't change export/import behavior.
