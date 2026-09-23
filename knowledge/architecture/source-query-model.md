---
type: Architecture
title: Source Query Model
description: >-
  SourceQuery/QueryResult — the database-neutral query model introduced in Arbeitsauftrag 4, its
  schema validation, the PostgreSQL compiler, and what still runs as provider-native SQL.
tags: [architecture, data-source, query, postgres, security]
timestamp: 2026-09-23T00:00:00Z
---

# Source Query Model

## 1. Why this exists

After [Arbeitsauftrag 2](/architecture/data-source-abstraction.md) the connector no longer depended on
Npgsql connection types, but every query was still Postgres SQL text built by hand in
`DynamicExportService` and handed to `IDataSourceProvider.ExecuteAsync` as a string. A second
provider could only ever receive SQL it can't run. Arbeitsauftrag 4 adds a database-neutral query
model so query *intent* can be described once, with the SQL dialect confined to the provider:

```text
ExportDefinition → ExportNode → SourceQuery → IDataSourceProvider → QueryResult
```

Binding rules the model is built around:

- No free SQL fragments from the UI or stored configuration — the model has no field that holds one.
- Filter values must be bindable as parameters — every operand is a value, never an expression.
- Table and column names must come from a known schema — `SourceQueryValidator` enforces it.
- No SQL dialect logic in `Connector.Core`.
- Existing PostgreSQL behavior is preserved.

## 2. The model (`Connector.Core.DataSources`)

```csharp
public sealed record SourceQuery
{
    public required string RootTable { get; init; }
    public IReadOnlyList<QueryColumn> Columns { get; init; } = [];      // empty = every root column
    public IReadOnlyList<QueryJoin> Joins { get; init; } = [];
    public IReadOnlyList<QueryCondition> Conditions { get; init; } = [];  // AND-combined
    public int? Limit { get; init; }
}

public sealed record QueryColumn    { string? Table; required string Column; string? Alias; }
public sealed record QueryJoin      { required string Table; required string Column;
                                      string? ParentTable; required string ParentColumn; QueryJoinType Type; }
public sealed record QueryCondition { string? Table; required string Column; required QueryOperator Operator;
                                      object? Value; IReadOnlyList<object> Values; }

public enum QueryOperator { Equal, NotEqual, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual,
                            Contains, StartsWith, EndsWith, IsNull, IsNotNull, In }

public sealed record QueryResult
{
    public required IReadOnlyList<QueryResultColumn> Columns { get; init; }
    public required IReadOnlyList<QueryResultRow> Rows { get; init; }   // Row.Values aligned with Columns
}
```

- A `Table` left `null` means the root table. Joins are equi-joins only
  (`Table.Column = ParentTable.ParentColumn`, inner or left); a join's parent must be the root or a
  table joined earlier in the list, and each table may appear once.
- `Contains`/`StartsWith`/`EndsWith` take a string and match it **literally and case-sensitively** —
  there is no wildcard syntax in the model.
- `In` takes its candidates in `Values`; `IsNull`/`IsNotNull` take no operand; every other operator
  takes a non-null scalar `Value` (string, number, bool, `Guid`, date/time types).
- `QueryResult` values are already stringified by the provider (same contract as before — see
  [Data Source Abstraction §3](/architecture/data-source-abstraction.md)); `ToDictionaries()` gives
  the name-keyed row shape `QueryResult` had before this change.

## 3. Validation — `SourceQueryValidator`

Dialect-free and shared by every provider. `Validate(query, schema)` throws
`InvalidSourceQueryException` for: an unknown table or column (exact, case-sensitive match against
`SourceSchema`), a column/condition/join referencing a table that isn't the root or joined before
it, a table joined twice, duplicate or empty output names, a negative `Limit`, and an operand that
doesn't fit its operator. A compiler may therefore assume every identifier it quotes is one the
schema itself reported.

## 4. PostgreSQL compilation — `PostgreSqlQueryCompiler` (`Connector.Infrastructure`)

`Compile(query, schema)` validates, then emits:

```sql
SELECT t0."id" AS "id", t1."name" AS "customer_name"
FROM "orders" AS t0
LEFT JOIN "customers" AS t1 ON t1."id" = t0."customer_id"
WHERE t0."status" IN (@p0, @p1) AND t0."shipped_at" IS NULL AND t1."name"::text LIKE @p2
LIMIT 25
```

- Identifiers are double-quoted like `DynamicExportService.QI`; table aliases are synthetic (`t0`, `t1`, …).
- Every value is a bound `NpgsqlParameter` (`@p0`, `@p1`, …); `In` binds one parameter per value.
  `Limit` is an `int` formatted invariantly.
- String values are bound with PostgreSQL's `unknown` type, so Postgres infers the type from the
  compared column exactly as for an untyped literal — a `uuid`/`date`/`numeric` column can be
  filtered with a string without a dialect-specific cast leaking into the model.
- LIKE operators escape the value's own `%`, `_` and `\` and cast the column `::text`.

`PostgreSqlDataSourceProvider.ExecuteAsync(config, SourceQuery, ct)` reads the live schema, compiles
against it and executes — so a column dropped since a definition was saved fails as
`InvalidSourceQueryException` before any SQL runs.

## 5. What still runs as provider-native SQL

`IDataSourceProvider` now has two execution entry points:

| Method | Input | Used by |
|---|---|---|
| `ExecuteAsync` | `SourceQuery` (neutral, validated, parameterized) | New code; nothing in the export pipeline yet |
| `ExecuteNativeAsync` | `NativeSqlQuery` (renamed from the old raw-SQL `SourceQuery`) | `DynamicExportService`'s flat, nested-JSON and `ExportNode` builders |

The export builders stay on native SQL for now because the model can't yet express what they emit:
correlated `json_build_object`/`json_agg` subquery trees, `string_agg`/`array_agg` flattening, and
`ExportNode.Filter`, which is still a free SQL fragment stored in the definition (screened at save
time by `ExportDefinitionEndpoints.ValidateRequestAsync`, but still spliced into the WHERE clause).
Moving `ExportNode → SourceQuery` needs (a) nested/aggregated projections in the model and (b)
replacing `Filter` with structured `QueryCondition`s, including a migration for stored definitions
and a UI change. Both are follow-up work. Nothing in `Connector.Core` builds a `NativeSqlQuery`.

## 6. Tests

- `SourceQueryValidatorTests` (`Connector.Core.Tests`): unknown table/column, join scoping,
  duplicate table/output name, negative limit, operand rules per operator.
- `PostgreSqlQueryCompilerTests` (`Connector.Integration.Tests`, no DB): simple SELECT, field
  projection, multiple conditions, null checks, `IN`, limit, unknown field, unknown table, plus
  joins, LIKE escaping, untyped string parameters and alias quoting.
- `PostgreSqlDataSourceProviderTests` (real Postgres): `ExecuteAsync` with projection, a string value
  against a `uuid` column, left join, `IN`, `IsNotNull`, `StartsWith`, `DateOnly` comparison and
  limit; unknown column rejected before execution; the existing raw-SQL tests now target
  `ExecuteNativeAsync`.
