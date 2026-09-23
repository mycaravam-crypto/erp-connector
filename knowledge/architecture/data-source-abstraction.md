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
`Npgsql` anywhere in their own signatures. `DataSourceProviderResolver` (`Connector.Infrastructure.DataSources`)
resolves by each DI-registered provider's own `Type` (`IEnumerable<IDataSourceProvider>`
injection), so adding a second provider is a DI registration, never a change to the resolver
itself.

**Supporting types**, also in `Connector.Core.DataSources`:

| Type | Purpose |
|---|---|
| `DataSourceType` | `PostgreSql`, `MariaDb` ([MariaDB Provider](/architecture/mariadb-provider.md)), `ServiceNowTableApi` ([ServiceNow Table API Provider](/architecture/servicenow-provider.md)) — all implemented; `ServiceNowSqlApi` (modeled only) |
| `DataSourceConfig` | Generic connection parameters (`Type` + relational Host/Port/Database or HTTP-API InstanceUrl + Username/Password/SslMode). See [Data Source Configuration](/architecture/data-source-configuration.md) for the full shape. Persisted under the same `AppSettings` storage key as always. |
| `SourceSchema`/`SourceTable`/`SourceColumn` | The provider's schema-read result — the interface's own return type, mirroring `Connector.Api/Dtos.cs`'s `SourceSchemaDto`/`SourceTableDto`/`SourceColumnDto` shape (no parallel model, no API change). |
| `TestConnectionResult` | `Success`/`Schema`/`Error` — a connection-test failure is reported here, sanitized, never thrown as a raw exception a caller might leak (credentials) by accident. |
| `SourceQuery` | The database-neutral query model (root table, columns, joins, conditions, limit) — see [Source Query Model](/architecture/source-query-model.md). |
| `NativeSqlQuery` | Provider-native SQL text + optional named parameters + optional command timeout. The provider does not parse or understand this text — see §3. |
| `QueryResult` | Generic rows: `Columns` plus `Rows` whose `Values` align with them (`ToDictionaries()` gives name-keyed rows). Every value is already stringified by the provider (see §3); `null` means the source column was `NULL`. |
| `DataSourceQueryException` | Wraps a provider-specific query failure a caller needs to *inspect*, not just log — e.g. a SQL error code — without the caller referencing a provider SDK type. `ErrorCode` carries that code verbatim (Postgres's `SQLSTATE`, for `PostgreSqlDataSourceProvider`). |
| `UnsupportedDataSourceException` | Thrown by `Resolve` for a `DataSourceType` with no registered provider. |

## 3. What's abstracted, and what deliberately isn't

**In `PostgreSqlDataSourceProvider` (`Connector.Infrastructure.DataSources.PostgreSql`)** — `MariaDbDataSourceProvider` mirrors each point for MariaDB, see [MariaDB Provider](/architecture/mariadb-provider.md):

- `BuildConnectionString`/`ParseSslMode` — building an `NpgsqlConnectionStringBuilder` from a `DataSourceConfig`.
- The `information_schema` schema-introspection query (`ReadSchemaAsync`).
- Neutral query execution (`ExecuteAsync`) — validates a `SourceQuery` against the live schema,
  compiles it with `PostgreSqlQueryCompiler`, and runs it (see [Source Query Model](/architecture/source-query-model.md)).
- Native SQL execution (`ExecuteNativeAsync`) — opens a connection, runs `NativeSqlQuery.Sql` with its
  parameters, and materializes rows generically. This is where the
  Postgres-specific date/timestamp → ISO-8601 stringification rule lives.

**SQL dialect — in `PostgreSqlDialect`, used by `DynamicExportService`:**

`DynamicExportService` still assembles its export queries itself: plain per-node SELECTs for the
export trees (nested records are built in C# — see [Export Tree Assembly](/architecture/export-tree-assembly.md))
and string-aggregated relations for the legacy flat export. But every PostgreSQL-specific fragment
in them (identifier quoting, `= ANY(…)`, `string_agg`, `::text`, `LIMIT`) comes from the provider's
`ISqlDialect`. See
[SQL Dialect](/architecture/sql-dialect.md). `IDataSourceProvider.ExecuteNativeAsync` only *executes*
the resulting `NativeSqlQuery.Sql` string and returns rows generically. So a second SQL provider needs
its own `ISqlDialect` (plus `ISqlDataSourceProvider`), not changes to the export builders — which is exactly
how `MariaDbDataSourceProvider` was added.

## 4. What calls the abstraction

| Caller | What it does |
|---|---|
| `DynamicExportService.BuildExportAsync`/`ExecuteQueryAsync`/`ExecuteNestedJsonQueryAsync`/`ExecuteExportNodeQueryAsync`/`BuildExportNodeAsync` | Build Postgres SQL text, execute it via `provider.ExecuteNativeAsync`, convert `QueryResult` back into the same `List<Dictionary<string,string>>`/`List<JsonObject>` shapes callers expect. |
| `ExportWorker`, `ExportDefinitionRunner` (+ `ExportDefinitionWorker`), `PipelineEndpoints`' three handlers, `ExportDefinitionEndpoints`' preview handler | Resolve a provider via `IDataSourceProviderResolver.Resolve(config.Type)` instead of building their own `NpgsqlConnection`. |
| `ConnectionEndpoints`' `POST /api/connection`, `GET /api/source-schema` | Call `provider.TestConnectionAsync`/`ReadSchemaAsync` directly. |
| `ImportDefinitionEndpoints`' save-time `AllowedWritableColumns` validator | Calls `provider.ReadSchemaAsync` directly. |

## 5. Imports: a connection, not a query

The import path needs more than one query per call. `ImportNodeWalker` issues a SELECT per matched
record and child. `ImportRunReleaser` commits an approved diff as **one** transaction of conditional
`UPDATE`s, all or nothing, under the four-eyes contract. `ExecuteAsync`/`ExecuteNativeAsync` are one
query in and one result out, so the import path doesn't use them.

Instead (Arbeitsauftrag 14), a SQL provider hands out an ADO.NET connection:
`ISqlDataSourceProvider.OpenConnectionAsync(config, ct)` returns a `DbConnection`. `ImportConnection.OpenAsync(resolver, config, ct)`
pairs it with the provider's `ISqlDialect` and is the only way the import code gets one. It is used by
`ImportWorker`, the preview and stage endpoints, and `ImportRunReleaser`. It refuses any provider
without the `Imports` capability. The walker and the releaser work on `DbConnection`/`DbCommand`/
`DbDataReader`. Their SQL comes from the dialect, and column values are stringified by
`ISqlDialect.FormatValue`, the same rule the provider's own rows use. None of them names a driver.

`Imports` is enabled for PostgreSQL only. MariaDB's dialect already covers the statements
(`CAST(… AS CHAR)`, `<=>`), but the release path hasn't been verified against it, so it stays off
until it is. ServiceNow has no SQL.

## 6. Adding another provider

`MariaDb` and `ServiceNowTableApi` were added exactly this way ([MariaDB Provider](/architecture/mariadb-provider.md), [ServiceNow Table API Provider](/architecture/servicenow-provider.md)). To add another one:

1. Implement `IDataSourceProvider` for it. `TestConnectionAsync`/`ReadSchemaAsync` are
   straightforward — connect, introspect, return `SourceSchema`. `ExecuteAsync` needs a compiler from
   the neutral `SourceQuery` to its own dialect (see [Source Query Model](/architecture/source-query-model.md));
   for `ExecuteNativeAsync` to receive SQL it can run, a SQL provider also implements
   `ISqlDataSourceProvider` and returns its own `ISqlDialect` — `DynamicExportService` renders its
   export trees through that ([SQL Dialect](/architecture/sql-dialect.md)), and `OpenConnectionAsync`
   for imports (§5). It also implements `ValidateConfig`/`TargetHost`/`IsAlwaysEncrypted` (its
   config rules; a relational one reuses `RelationalConnectionRules`) and declares its
   `Capabilities` (§7). `ExportNode.Filter` stays a stored, dialect-specific WHERE fragment
   ([SQL Dialect §4](/architecture/sql-dialect.md)).
2. Register it in `Program.cs` (`builder.Services.AddSingleton<IDataSourceProvider, YourProvider>()`)
   — `DataSourceProviderResolver` picks it up automatically via `IEnumerable<IDataSourceProvider>`.
3. No resolver code changes, no `DynamicExportService` signature changes — those are already
   provider-agnostic.

## 7. Capabilities and the provider contract

Providers differ in a few ways. `IDataSourceProvider.Capabilities` (`DataSourceCapabilities`, in
`Connector.Core`) names each difference, so neither callers nor tests branch on `provider.Type`. Only
the differences that actually exist are modeled:

| Capability | PostgreSQL | MariaDB | ServiceNow Table API | Effect |
|---|---|---|---|---|
| `NativeSql` | ✅ | ✅ | ❌ | `ExecuteNativeAsync` runs SQL, so the SQL-rendering export builders work. `DynamicExportService` refuses a provider without it (`UnsupportedDataSourceException`). |
| `CaseSensitiveTextMatch` | ✅ | ✅ | ❌ | `Contains`/`StartsWith`/`EndsWith` are case-sensitive as the model asks; ServiceNow ignores case |
| `DistinguishesEmptyFromNull` | ✅ | ✅ | ❌ | ServiceNow has no NULL; an empty field is reported as `null` |
| `ConditionsOnLeftJoinedTables` | ✅ | ✅ | ❌ | ServiceNow rejects a condition on a left-joined table (`InvalidSourceQueryException`) |

Everything else is required of every provider and pinned by `DataSourceProviderContractTests`: an
abstract xUnit class that runs the same assertions against each provider, on the same
`export_order`/`export_customer` rows.

- `TestConnection` succeeds, and the schema can be read.
- Relation metadata exists.
- A simple table and selected fields can be queried.
- Filters (`=`, `>`, `IN`), null handling and limit work, and a join over the reported relation works.
- An unknown table or column fails with `InvalidSourceQueryException`.
- Cancellation works.
- No secret appears in an error.

A capability-dependent test asserts the declared behavior **both ways**: for example, native SQL runs
when `NativeSql` is set and throws `UnsupportedDataSourceException` when it isn't. The concrete
classes are `PostgreSqlProviderContractTests`, `MariaDbProviderContractTests` (real databases; no-op
when the fixture isn't running) and `ServiceNowTableApiProviderContractTests` (in-memory
`FakeServiceNow.WithExportFixture()`). A new provider gets a subclass, which is three members.

## 8. Tests

- `DataSourceProviderResolverTests` — provider resolution (`PostgreSql`/`MariaDb` resolve to their registered
  providers) and the unsupported-provider error scenario (an unregistered type, an out-of-range numeric
  type, no providers at all throw `UnsupportedDataSourceException` carrying the requested type).
- The MariaDB provider's own suites — see [MariaDB Provider §5](/architecture/mariadb-provider.md).
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
