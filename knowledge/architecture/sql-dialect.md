---
type: Architecture
title: SQL Dialect
description: >-
  ISqlDialect/PostgreSqlDialect — the one place PostgreSQL-specific SQL syntax lives, which members
  exist and why, and every PostgreSQL-specific reference that remains outside it.
tags: [architecture, data-source, postgres, sql]
timestamp: 2026-09-23T00:00:00Z
---

# SQL Dialect

## 1. What it is

The generic query builders build statements from plain ANSI structure (`SELECT … FROM … WHERE … AND
…`, `UPDATE … SET … WHERE …`, correlated subqueries) and take every backend-specific fragment from an
`ISqlDialect`. Implementations: `PostgreSqlDialect` and `MariaDbDialect` ([MariaDB Provider](/architecture/mariadb-provider.md)). It is not a SQL framework: there is no
AST, no query builder object and no ORM. Each member returns a string fragment, and it exists only
because some builder in this codebase emits that fragment.

```text
src/Connector.Infrastructure/DataSources/
├── ISqlDialect.cs                   ISqlDialect, ISqlDataSourceProvider
├── DataSourceProviderResolver.cs
├── PostgreSql/
│   ├── PostgreSqlDialect.cs         every PostgreSQL-specific SQL fragment
│   ├── PostgreSqlDataSourceProvider.cs
│   └── PostgreSqlQueryCompiler.cs   SourceQuery → PostgreSQL (see Source Query Model)
└── MariaDb/                         see MariaDB Provider
    ├── MariaDbDialect.cs            every MariaDB-specific SQL fragment
    ├── MariaDbDataSourceProvider.cs
    ├── MariaDbConnectionFactory.cs
    ├── MariaDbSchemaReader.cs
    └── MariaDbQueryCompiler.cs      SourceQuery → MariaDB
```

`ISqlDialect` lives in `Connector.Infrastructure`, not `Connector.Core`: SQL is an infrastructure
concern, and `Connector.Core` stays dialect-free.

## 2. Members

| Member | PostgreSQL output | Needed by |
|---|---|---|
| `QuoteIdentifier` | `"name"` (embedded `"` doubled) | every builder |
| `BuildParameterName(i)` | `@p{i}` (also the Npgsql parameter name) | query compiler, import walker, import releaser |
| `BuildLimit(n)` | `LIMIT n` | legacy flat/nested export, `ExportNode` export, import walker, query compiler |
| `QuoteStringLiteral` | `'…'` (embedded `'` doubled) | string-aggregate delimiter |
| `CastToText` | `expr::text` | export tree scalars and join keys, import walker/releaser key matching, LIKE in the compiler |
| `BuildNullSafeEquals` | `a IS NOT DISTINCT FROM b` | import releaser's expected-old-value guard |
| `BuildMatchesAny` | `expr = ANY(@p0)` — binds the key batch itself (one `text[]` parameter; MariaDB binds one parameter per key: `expr IN (@p0, …)`) | export tree engine's one-query-per-level child fetch |
| `ConvertNativeTextToJson` | `to_json`'s rules, applied in C# (`PostgreSqlJsonValues`) | export tree engine's typed values for legacy nested groups |
| `BuildStringAggregate` | `string_agg(x::text, 'delim')` | legacy flat export's relation flattening |

The first three are the ones the work order asked for. The other six are extensions, each backed by a
builder listed in the right-hand column. `BuildJsonObject`/`BuildJsonArrayAggregate` existed until
the export trees moved to C# ([Export Tree Assembly](/architecture/export-tree-assembly.md)) and were
removed with them. The work order allowed extensions only for a concrete need
in existing code.

**How builders get a dialect:**
- `DynamicExportService` asks the provider. A provider that speaks SQL implements
  `ISqlDataSourceProvider` (`IDataSourceProvider` + `Dialect`). Any other provider makes an export
  query throw `UnsupportedDataSourceException`.
- `ImportNodeWalker` and `ImportRunReleaser` use `PostgreSqlDialect.Instance` directly, because they
  already run on an Npgsql connection (§4).

**One intentional SQL change:** the legacy flat export's "array" relation strategy used
`array_to_string(array_agg(x::text), ',')` and is now `string_agg(x::text, ',')`. The two differ only
when every related value is NULL (`''` versus `NULL`). `ExecuteQueryAsync` turns NULL into `""`, so
export output is identical. All other emitted SQL is byte-for-byte unchanged, except that the
compiler's and import paths' parameter names now come from `BuildParameterName`.

## 3. Tests

- `PostgreSqlDialectTests`: pins the exact text every member renders.
- The existing Postgres-backed suites run the fragments for real: `DynamicExportServiceFlatQueryPostgresTests`,
  `DynamicExportServiceNestedJsonPostgresTests`, `ExportNodeQueryPostgresTests`,
  `ImportNodeWalkerPostgresTests`, `ImportRunReleaserPostgresTests`, `ImportWorkerPostgresTests`,
  `PostgreSqlDataSourceProviderTests`, and `PostgreSqlQueryCompilerTests`. All of them pass unchanged,
  apart from one compiler assertion on parameter names (`p0` → `@p0`).

## 4. Remaining PostgreSQL-specific references outside `DataSources/PostgreSql`

A repo-wide search after the extraction looked for casts, JSON/array functions, `LIMIT`,
`IS NOT DISTINCT FROM`, catalog names, `pg_*`, SQLSTATEs and `Npgsql`. The generic services no longer
emit any PostgreSQL SQL syntax. What remains:

| Where | What | Why it stays |
|---|---|---|
| `ExportNode.Filter` → `DynamicExportService.ExecuteExportNodeQueryAsync` (per-node queries) | A stored, admin-authored WHERE fragment spliced in as `WHERE (…)` / `AND (…)` | The fragment is written in the backend's dialect by whoever saves the definition. The builder adds only ANSI parentheses. Replacing it with structured `QueryCondition`s needs a stored-data migration and a UI change, see [Source Query Model §5](/architecture/source-query-model.md). |
| `ExportDefinitionEndpoints` (`DangerousFilterKeywordRegex`) | Denylist of PostgreSQL functions/catalogs (`pg_sleep`, `pg_catalog`, `dblink`, …) | Save-time security screening of the free-SQL `Filter` above. It has to know the target's dangerous functions, and it goes away together with `Filter`. |
| `ImportNodeWalker`, `ImportRunReleaser` | Take/open an `NpgsqlConnection` and `NpgsqlCommand` | A driver dependency, not SQL syntax (their SQL now goes through the dialect). The releaser needs one multi-statement transaction, which `IDataSourceProvider` can't express. See [Data Source Abstraction §5](/architecture/data-source-abstraction.md). |
| `ImportWorker`, `ImportDefinitionEndpoints` (preview + stage) | Open the `NpgsqlConnection` handed to `ImportNodeWalker` | Same boundary as the walker. |
| `ConnectionEndpoints.IsValidSslMode` | Validates `SslMode` against Npgsql's `SslMode` enum | Connection configuration for the relational (PostgreSQL) source type, not query syntax. |
| Doc comments in `Connector.Core` (`ExportNode.cs`, `ImportPlan.cs`, `SourceSchema.cs`, `DataSourceQueryException.cs`, `DataSourceConfig.cs`) | Mention `::text`, `IS NOT DISTINCT FROM`, `information_schema`, SQLSTATE, Npgsql | Descriptive text only; no code in `Connector.Core` builds or runs SQL. |
| `DynamicExportService.LegacyMapping.cs` comment | Mentions the old `array_to_string(array_agg(…))` form | Explains the one SQL change above. |

Out of scope for this search: `src/Connector.Infrastructure/Migrations/` (EF Core's generated schema)
and the `sqlite_master` checks in `Program.cs`. Both target the connector's own SQLite log database,
not the ERP source.
