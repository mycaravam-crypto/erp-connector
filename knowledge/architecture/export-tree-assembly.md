---
type: Architecture
title: Export Tree Assembly
description: >-
  How nested export records (ExportNode trees and legacy nested JSON groups) are built in C# from
  plain relational rows: one query per tree node, grouping by join key, and the rules that keep the
  output identical to the former SQL-built JSON.
tags: [architecture, export, json, postgres, performance]
timestamp: 2026-09-23T00:00:00Z
---

# Export Tree Assembly

## 1. What changed

The database used to build the nested records itself: a root query with correlated
`json_build_object`/`json_agg` subqueries (`COALESCE(…, '[]'::json)` for empty arrays), returning
one JSON document per row. Now the database only returns plain relational rows, and
`DynamicExportService`'s tree query engine (`DynamicExportService.TreeQuery.cs`) builds the records
in C#:

```text
Database ─ one SELECT per tree node ─→ rows ─→ QueryResult ─→ grouped by join key (C#)
    ─→ ExportNode tree / nested groups ─→ JsonObject records ─→ JSON / CSV / XLSX writers
```

Both nested paths use the engine: the `ExportNode` tree engine (`ExecuteExportNodeQueryAsync`,
behind every Export Definition, all formats) and the legacy nested-JSON groups
(`ExecuteNestedJsonQueryAsync`, `/api/pipeline/*`). Their public signatures and their callers are
unchanged. The writers, GDPR stripping and `FieldMapping` application all operate on the same
`JsonObject` records as before.

## 2. How a tree is fetched

1. **Plan.** The config is compiled into a `TreePlan`: per node, its table, its SQL alias (`s` for the
   root, `en0`/`ng0`… below it, in the same pre-order numbering the correlated subqueries used, so
   a stored `Filter` that names an alias keeps working), its `Filter`, and its members in order
   (scalars and child nodes). Depth checks and GDPR exclusion happen here, before any query runs.
2. **Root query.**
   ```sql
   SELECT s."id"::text AS "c0", s."customer_id"::text AS "c1", s."id"::text AS "c2"
   FROM "export_order" s WHERE (<root filter>) LIMIT n
   ```
   Column *i* is member *i*: a scalar's value, or, for a child member, the parent-side join key.
3. **Child levels.** For each child member, the distinct non-null join-key values of **all** parent
   rows go into one query for the whole level:
   ```sql
   SELECT en0."name"::text AS "c0", en0."vip"::text AS "c1", en0."id"::text AS "c2"
   FROM "export_customer" en0 WHERE en0."id"::text = ANY(@p0) AND (<node filter>)
   ```
   The last column is the child's own join key. Levels recurse the same way. The query count equals
   the number of tree nodes, independent of row counts, so there is no N+1. A level with more than
   `TreeChildKeyBatchSize` (10 000) distinct parent keys is fetched in several chunks of that size.
4. **Assembly.** Child rows are grouped by join key in an `ILookup` and attached to each parent in
   member order.

Keys are compared as **text on both sides** (`::text` in the `ANY` match, and the text value for
grouping). This works for the key types in use (uuid, integers, varchar). It also means the child
query can't use a plain index on the join column. Each child level typically costs one scan of the
child table, instead of one index lookup per parent row.

## 3. Semantics kept identical

| Case | Result (unchanged) |
|---|---|
| Array node, no matching rows | `[]`, never `null` |
| Object node, no match (incl. NULL foreign key) | `null`, key present |
| Object node, more than one match | `InvalidOperationException` with the same message as before (`ObjectNodeCardinalityErrorMessage` / `ObjectGroupCardinalityErrorMessage`) |
| NULL scalar | JSON `null`, key present |
| Same child row under several parents (shared lookup) | an equal, independent copy under each parent |
| Two members with the same `TargetKey` | fails, as parsing the duplicate-key JSON did |
| Key order | member order, as `json_build_object` wrote it |
| Array element order / root row order | query order; never guaranteed, before or after (no `ORDER BY`) |

**Value encoding.**
- **`ExportNode` scalars** are selected with `::text`, so each one is a JSON string of exactly the
  text it had before.
- **Legacy nested-group fields** keep their column's JSON type. The engine runs its queries with
  `NativeSqlQuery.ReturnNativeText`: the provider returns PostgreSQL's own text rendering of every
  value plus the column type (`QueryResultColumn.DataType`), and
  `ISqlDialect.ConvertNativeTextToJson` (`PostgreSqlJsonValues`) applies `to_json`'s rules.
  - Numbers stay JSON numbers with the same digits, and `NaN` stays a string.
  - Booleans become `true`/`false`.
  - `json`/`jsonb` values are embedded as JSON.
  - Timestamps get ISO `T` and `±HH:MM` offsets.
  - Arrays become JSON arrays.
  - Everything else becomes a string.

**Verification.** Before the change, a differential harness captured the old SQL-built output for 28
trees in a scratch database. The trees covered every column type above plus arrays, NaN, time zones,
shared parents, NULL foreign keys, filters, GDPR, disabled nodes, limits, both cardinality errors
and the duplicate-key error. The new engine's output was byte-for-byte identical, with arrays
compared order-insensitively. The committed `ExportTreeRegressionTests` were also run against the old
implementation, and every structural expectation passed there too.

## 4. Known limits

- A child `Filter` used to run inside a correlated subquery, so it *could* reference the parent's
  columns through the outer alias. It now runs in the child's own query and can only reference the
  child's table. No test or fixture uses a parent reference. A stored definition that does would
  now fail with a SQL error rather than export wrong data.
- `ReturnNativeText` value conversion assumes the server's default `DateStyle` (ISO). Composite
  (row) types, and types with a custom cast to `json`, come out as strings rather than objects. No
  exported column uses either.
- The legacy **flat** export (`ExecuteQueryAsync`, CSV/Excel) still flattens relations with
  `string_agg` in SQL. It produces one delimited string per cell, not a JSON structure, and is
  unchanged.

## 5. Tests

- `ExportTreeRegressionTests` (real Postgres, dedicated `export_*` tables in `testdb/init.sql`):
  simple root table, 1:1, 1:n, two nesting levels, empty child collection, NULL fields, several
  root records (including a shared parent), the query count (one per node) with no
  `json_build_object`/`json_agg`/`array_agg`/`::json` in any SQL, and legacy typed values.
- `PostgreSqlDialectTests`: the native-text → JSON conversion rules, one case per type.
- All pre-existing export suites (`ExportNodeQueryPostgresTests`, `DynamicExportServiceNestedJsonPostgresTests`,
  `ExportNodeEngineTests`, `DynamicExportServiceFlatQueryPostgresTests`, …) pass unchanged.
