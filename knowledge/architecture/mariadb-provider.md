---
type: Architecture
title: MariaDB Provider
description: >-
  MariaDbDataSourceProvider — the second relational data source (Arbeitsauftrag 7): its components,
  how each MariaDB-specific concern is mapped, the known differences to PostgreSQL, and its tests.
tags: [architecture, data-source, mariadb, sql]
timestamp: 2026-09-23T00:00:00Z
---

# MariaDB Provider

`DataSourceType.MariaDb` is a real provider, registered next to PostgreSQL in `Program.cs`. It implements
the same interfaces as the PostgreSQL provider (`IDataSourceProvider` + `ISqlDataSourceProvider`), so
every caller that resolves a provider — the export pipeline, Export Definitions, the connection and
schema endpoints — works against MariaDB without a single MariaDB-specific branch. The driver is
[MySqlConnector](https://mysqlconnector.net/).

## 1. Components (`Connector.Infrastructure/DataSources/MariaDb`)

| Type | Job |
|---|---|
| `MariaDbDataSourceProvider` | `TestConnectionAsync`, `ReadSchemaAsync`, `ExecuteAsync` (neutral `SourceQuery`), `ExecuteNativeAsync` (SQL rendered with the dialect). Row materialization and error mapping. |
| `MariaDbConnectionFactory` | `MySqlConnectionStringBuilder` from a `DataSourceConfig` (typed properties, no string interpolation — the SR-02 rule), default port 3306, connect timeout 5 s, `SslMode` mapping, opening a connection. |
| `MariaDbSchemaReader` | Schema introspection of `DATABASE()` via `information_schema.TABLES`/`COLUMNS`/`KEY_COLUMN_USAGE`/`REFERENTIAL_CONSTRAINTS`: tables, columns, `DATA_TYPE`, nullability, primary keys (`CONSTRAINT_NAME = 'PRIMARY'`), foreign keys, `auto_increment` (identity) and generated columns. |
| `MariaDbQueryCompiler` | `SourceQuery` → MariaDB SQL. Same guarantees as the PostgreSQL compiler: validated against the live schema first, synthetic aliases, every value a bound `@pN` parameter. |
| `MariaDbDialect` | `ISqlDialect`: backtick quoting, `@pN`, `LIMIT n`, `CAST(… AS CHAR)`, `<=>`, `GROUP_CONCAT(… SEPARATOR …)`, `IN (@p0, …)` for the tree engine's key batches, and native text → JSON. |

## 2. How MariaDB-specific concerns are mapped

- **Key batches.** MariaDB has no array parameters. `ISqlDialect.BuildMatchesAny` therefore binds the
  batch itself: PostgreSQL adds one `text[]` parameter (`= ANY(@p0)`, unchanged SQL), MariaDB one
  parameter per key (`IN (@p0, @p1, …)`). MySqlConnector substitutes parameters client-side, so a full
  10 000-key batch is fine.
- **Native text.** For `NativeSqlQuery.ReturnNativeText`, the provider renders each value the way
  MariaDB's text protocol would (`1`/`0` for `BOOL`, `yyyy-MM-dd HH:mm:ss[.ffffff]` for `DATETIME`,
  invariant numbers) and reports MySqlConnector's type name (`INT`, `DECIMAL`, `BOOL`, `DATETIME`, …).
  `MariaDbDialect.ConvertNativeTextToJson` maps that to the JSON PostgreSQL's `to_json` gives the
  equivalent column, so legacy nested groups are the same document on both backends.
- **Default stringification** (flat export) matches PostgreSQL's: dates and timestamps as `yyyy-MM-dd`,
  everything else the CLR value's `ToString()`, `NULL` as null. Zero dates (`0000-00-00`) read as
  `DateTime.MinValue` instead of failing the row.
- **LIKE** (`Contains`/`StartsWith`/`EndsWith`) is compiled as
  `CAST(col AS CHAR) COLLATE utf8mb4_bin LIKE @p`: literal (escaped `%`, `_`, `\`) and case-sensitive, as
  the model requires, although MariaDB's default collations are case-insensitive.
- **TLS.** `DataSourceConfig.SslMode` keeps one vocabulary for both relational sources (the Npgsql names
  `ConnectionEndpoints` validates). MariaDB maps `Disable` → `None`, `Require` → `Required`, `VerifyCA`,
  `VerifyFull`, and everything else (including `Prefer`/`Allow`/unset) → `Preferred`.
- **Errors.** A server error (it carries a SQLSTATE, e.g. `42S02` unknown table) becomes a
  `DataSourceQueryException` with that SQLSTATE as `ErrorCode`. Client-side failures (command timeout,
  lost connection) propagate as-is, as with Npgsql. Connection-test failures are sanitized by
  `ErrorSanitizer` like PostgreSQL's.
- **Timeouts and cancellation.** `NativeSqlQuery.CommandTimeoutSeconds` (default 30 s) becomes
  `MySqlCommand.CommandTimeout`; every call passes the `CancellationToken` through, and MySqlConnector
  cancels the running statement server-side.

## 3. Known differences to PostgreSQL

| Case | PostgreSQL | MariaDB | Why |
|---|---|---|---|
| `ExportNode` scalar of a boolean column | `"true"`/`"false"` | `"1"`/`"0"` | MariaDB's `BOOLEAN` is `TINYINT(1)`; scalars are the column's text cast, and the cast can't know the column was meant as a boolean. Typed (legacy nested-group) values are `true`/`false` on both. |
| Legacy flat relation aggregate of a boolean | `true` | `1` | Same reason (`GROUP_CONCAT(CAST(… AS CHAR))`). |
| `Equal`/`In` on text columns | case-sensitive | per column collation (usually case-insensitive) | Kept as plain comparisons so indexes are used; only the LIKE operators force a binary collation. |
| `ExportNode.Filter` | PostgreSQL syntax | MariaDB syntax | The stored fragment is written in the backend's dialect; `ExportDefinitionEndpoints` screens it the same way (no function calls, no statement keywords, no comments). |

## 4. What stays PostgreSQL-only

The import paths (`ImportNodeWalker`, `ImportRunReleaser`, `ImportWorker`, the import preview/stage
endpoints) still run on Npgsql connections (see [Data Source Abstraction §5](/architecture/data-source-abstraction.md)).
They all get their connection string from `PostgreSqlDataSourceProvider.BuildConnectionString`, which
now refuses any config whose `Type` isn't `PostgreSql` with an `UnsupportedDataSourceException` — an
import against a MariaDB connection fails with a clear message instead of Npgsql talking to a MariaDB
server.

## 5. Tests

- `MariaDbDialectTests`, `MariaDbQueryCompilerTests` (no DB): exact SQL fragments, parameter binding,
  native text → JSON, unknown table/column rejected.
- `MariaDbDataSourceProviderTests` (real MariaDB): connection-string injection safety and `SslMode`
  mapping, connection test (success, invalid credentials without password echo), schema (columns, PK,
  FK), neutral queries (join, where, `IN` with an injection-shaped value, `DateOnly`, case-sensitive
  literal `StartsWith`, limit), missing table (validation and SQLSTATE `42S02`), date formatting,
  command timeout, cancellation.
- `MariaDbExportParityTests` (real PostgreSQL **and** MariaDB, the same `export_*` rows from
  `testdb/init.sql` and `testdb/mariadb-init.sql`): the acceptance criterion — flat export, filter, join,
  nested export, CSV, JSON, XLSX and legacy typed nested groups produce the same output on both;
  missing table, invalid credentials and timeout fail on both; plus the documented boolean difference.
- MariaDB-backed tests no-op when the fixture isn't running (`docker-compose --profile test up -d
  testdb-mariadb`). CI starts a `mariadb:11.4` service and loads `testdb/mariadb-init.sql`, so they run
  for real there.
