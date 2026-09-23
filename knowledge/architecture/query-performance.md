---
type: Architecture
title: Query Performance
description: >-
  How the export pipeline reads large data sets (Arbeitsauftrag 13): the query plan per export path,
  the QueryCount/RecordsRead/DurationMs metrics, load-test results for 1k/10k/100k records, and the
  known performance limits.
tags: [architecture, performance, export, postgres, mariadb, servicenow]
timestamp: 2026-09-23T00:00:00Z
---

# Query Performance

## 1. Query plan per export path

No export path sends a query per root record. The pattern everywhere is to read the roots, read the
children of all of them with one `IN` query per level, and group them in C#.

| Path | Queries per run | Notes |
|---|---|---|
| `ExportNode` trees (Export Definitions, all formats) and legacy nested JSON groups | 1 root query + 1 per child node (+1 per extra 10 000-key batch) | [Export Tree Assembly](/architecture/export-tree-assembly.md). Children of a whole level are fetched with `key IN (…)` / `= ANY(@keys)` and grouped by key. |
| Legacy flat export (CSV/XLSX/flat JSON of `/api/pipeline/*`) | 1 | Relations are flattened by correlated `string_agg`/`GROUP_CONCAT` subqueries inside the one statement. The database evaluates them per row, but there is no application round trip per row. |
| `SourceQuery` on PostgreSQL/MariaDB | 1 | A SQL join |
| `SourceQuery` on ServiceNow | 1 root read + 1 per join and 100-key batch, each paginated | Joins run in C# ([ServiceNow provider §3](/architecture/servicenow-provider.md)). `sysparm_limit` pages of 1 000. |

The rest of the work order's checklist:

- **Only the needed fields.** The tree engine selects only the enabled scalars and join keys. The
  `SourceQuery` compilers enumerate the columns, even when "all columns" is asked for. ServiceNow
  sends `sysparm_fields`. No builder emits `SELECT *`.
- **Server-side filters.** `ExportNode.Filter` and `SourceQuery` conditions run in the database or in
  ServiceNow's encoded query, never in C#.
- **Cancellation.** Every provider call takes the run's `CancellationToken`. A 100 000-record export
  cancelled after 50 ms stops right away (`ExportLoadTests.Cancellation_StopsALargeExport`).

## 2. Metrics

`MeteredDataSourceProvider` wraps the provider for the duration of one build. `BuildExportNodeAsync`
and `BuildExportAsync` return what it measured as `ExportBuildResult.Metrics` (`ExportQueryMetrics`):

| Metric | Meaning |
|---|---|
| `QueryCount` | Provider calls (`ExecuteAsync`/`ExecuteNativeAsync`). A paginated ServiceNow read counts once. |
| `RecordsRead` | Rows returned by the source, over all queries |
| `DurationMs` | Wall time of the whole build: queries, tree assembly and writing the output |

They're logged with every scheduled run (`ExportWorker`, `ExportDefinitionWorker`) and written to the
audit detail of manual and test runs of an Export Definition (`queries=… rows_read=… duration_ms=…`).
Peak memory isn't measured in production. The load tests report allocated bytes instead.

## 3. Load tests

`ExportLoadTests` export an order → lines tree (two lines per order, JSON) from the `perf_order`/
`perf_line` tables (100 000 / 200 000 rows, created on first use) against real PostgreSQL 16 and
MariaDB 10.11. Measured locally:

| Records | Backend | QueryCount | RecordsRead | DurationMs | Allocated | Output |
|---:|---|---:|---:|---:|---:|---:|
| 1 000 | PostgreSQL | 2 | 3 000 | 122 | ~5 MB | 175 KB |
| 1 000 | MariaDB | 2 | 3 000 | 78 | ~5 MB | 175 KB |
| 10 000 | PostgreSQL | 2 | 30 000 | 231 | ~57 MB | 1.8 MB |
| 10 000 | MariaDB | 2 | 30 000 | 252 | ~56 MB | 1.8 MB |
| 100 000 | PostgreSQL | 11 | 300 000 | 5 528 | ~617 MB | 18 MB |
| 100 000 | MariaDB | 11 | 300 000 | 4 705 | ~585 MB | 18 MB |

The tests assert that the query count is `1 + ⌈records / 10 000⌉`, independent of the row count; that
exactly the needed rows are read (`3 × records`); and that the output is plausible. The duration
assertion is only a generous safety net (< 120 s), since CI machines vary.

## 4. Known limits

- **Everything is built in memory.** Records are assembled as `JsonObject` trees and the file is
  written into a `byte[]`. For this tree that is about 6 KB allocated per root record (~600 MB for
  100 000), with a live set well below that. `MaxExportRowsPerRun` (500 000) caps a run. Streaming
  the output would be the next step if exports grow further.
- **Join keys are compared as text.** Child queries match `CAST(key AS text/CHAR)` against the batch,
  so a plain index on the join column isn't used. Each child level costs one scan of the child table
  per 10 000-key batch.
- **Batch size.** 10 000 keys per child query (`TreeChildKeyBatchSize`). MariaDB binds one parameter
  per key; MySqlConnector substitutes them client-side, so a statement is about 10 000 × key length
  bytes.
- **Legacy flat export.** The correlated aggregate subqueries run once per root row and relation
  field inside the database. That is fine for the table sizes it's used with, but it doesn't scale
  like the tree engine.
- **ServiceNow.**
  - Offset pagination gets slower on very deep offsets.
  - Join reads use 100-key batches to keep URLs short, so a join over 100 000 root rows costs
    1 000+ requests.
  - A full schema read (connection test, schema page) reads all of `sys_db_object`/`sys_dictionary`.
  - Exports can't run on ServiceNow yet (`NativeSql` capability).
