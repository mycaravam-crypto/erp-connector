---
type: Pipeline Design
title: Export Definitions — generic, tree-based multi-export
description: Any number of independently named, scheduled export definitions, each rooted at any table, with unlimited nesting depth, in every output format — generalizing the single-mapping legacy pipeline into first-class, saved entities.
resource: src/Connector.Core/DynamicExport/ExportNode.cs
tags: [pipeline, dynamic-mapping]
timestamp: 2026-09-03T00:00:00Z
---

## 1. Vision

The connector's core job is to extract CIs from the ERP, minimize for GDPR, and release to the
vendor via a four-eyes, air-gapped staging folder. Four-eyes release, GDPR-denylist enforcement,
audit log, and sequence-integrity checks are correctness-critical and apply identically regardless
of which mapping mechanism produced the export — see [Four-Eyes
Release](/operations/four-eyes-release.md).

Alongside the legacy single-mapping flow (one mapping, one source table, plus named presets — see
[DynamicExportService](/pipeline/dynamic-export-service.md)), the connector also supports **any
number of independently named, saved, scheduled export definitions**, each rooted at any table,
with unlimited nesting depth, in every output format, each with its own field-level
transformation, schedule, and run history. The legacy flow was deliberately kept, not superseded
— see [§8 Legacy coexistence](#8-legacy-coexistence).

## 2. Data Model

One recursive `ExportNode` tree replaces what would otherwise be three parallel, overlapping
mapping shapes (flat fields, flat 1:N relations, nested JSON groups) — one mental model instead of
three, and adding a fourth level of nesting needs zero new types, only a smarter writer.

```
ExportDefinition                          (EF Core entity)
├── Id, Name, Description
├── RootTable            : string
├── RootNode              : ExportNode      (the tree — see below)
├── OutputFormat          : csv | xlsx | json
├── IsEnabled             : bool
├── Schedule              : string?         (cron expression; null = manual only)
├── ConfigVersion         : int             (incremented on every save; carried onto each run)
├── IntegrationKey        : string?         (see §7 — Import Mapping Presets)
├── ContractVersion       : int?
├── CorrelationKeySourceField : string?
├── CreatedBy / CreatedAt / UpdatedBy / UpdatedAt

ExportNode                                  (recursive — the arbitrary-nesting mechanism)
├── TargetKey             : string          (export-visible name: column header / JSON key)
├── Kind                  : root | scalar-field | object | array
├── SourceField            : string?         (set when Kind = scalar-field)
├── RelatedTable / JoinKey / SourceJoinKey   (set when Kind = object | array — an N:1 or 1:N join)
├── Filter                : string?          (optional WHERE-clause fragment scoped to this node's table)
├── Mapping               : FieldMapping?    (set when Kind = scalar-field — see below)
├── Children              : ExportNode[]     (fields and/or nested relations — arbitrary depth)
└── Enabled               : bool

FieldMapping                                 (attached to every scalar-field node)
├── DefaultValue           : string?          (used when the source value is null)
├── Transform              : none | uppercase | lowercase | trim | dateFormat | constant
├── TransformArg            : string?          (e.g. the date format string, or the constant value)
└── DataType                : string | number | boolean | date  (coercion target)

ExportDefinitionRunEntity                    (execution history, one row per run)
├── Id, ExportDefinitionId, ConfigVersion    (which saved version ran — traceability)
├── StartedAt / FinishedAt (UTC)
├── Status                : Success | Failed | Running
├── RecordCount
├── ErrorMessage           : string?          (populated on Failed — never a silent empty result)
└── TriggeredBy             : string           (username, or "scheduler")
```

A `root` node's own fields are unused — it exists only to hold `Children`, one per top-level
export key. A flat CSV/Excel row is just the case where every node is `scalar-field` at depth 1;
no separate flat shape is needed.

**A scalar column is always read from Postgres as `::text`**, never cast per `FieldMapping.DataType`
in SQL — a bad value in one row would otherwise fail the entire query. `DataType` coercion happens
in C# after the row is read, so one malformed field degrades to a best-effort string instead of
aborting the whole export.

## 3. Field Mapping & Transformation

Every `scalar-field` node's `Mapping` supports, at minimum:

| Capability | Mechanism |
|---|---|
| Rename | `TargetKey` differs from `SourceField` |
| Exclude | `Enabled = false` |
| Constant/default value | `Transform = constant` + `TransformArg`; or `DefaultValue` for null-fallback |
| Null handling | `DefaultValue` substituted when the source value is `NULL` |
| Data-type conversion | `DataType` — coerced in C# at read time, not a SQL cast (see §2) |
| Value transformation | `Transform` enum — a small closed set, not a scripting engine (see §6) |

Example: `article.article_number → product.sku` is one `scalar-field` node with
`SourceField = "article_number"`, `TargetKey = "sku"`.

## 4. Scheduling

* `ExportDefinition.Schedule` is a 5-field cron expression, or `null` for manual-only.
* The UI offers presets (Manual/Hourly/Daily/Weekly) plus an advanced free-text cron field. Cron
  granularity is hourly-or-coarser by convention, matching the project's existing scheduling
  convention — the matcher itself doesn't reject a finer-grained expression, it simply isn't
  offered in the UI presets.
* One background worker (`ExportDefinitionWorker`, a sibling of `ExportWorker`, not a replacement)
  polls enabled `ExportDefinition` rows whose cron is due.
* Every run — scheduled or manual — writes exactly one `ExportDefinitionRunEntity` row. "Test"
  shares the same run path as "Run Now", just capped and flagged, never a separate untracked path.

See [Scheduler](/dynamic-export/scheduler.md) for how the worker and cron matcher actually run.

## 5. UI

`ExportNodeTreeEditor.vue` is the recursive tree builder: root = table picker (reused from
`SchemaView.vue`); "Add field" → leaf node with inline mapping editor; "Add related entity" →
object/array node prefilled from FK auto-detection; reorder/rename/remove inline; the whole tree
renders as an indented outline. `ExportDefinitionsView.vue` lists definitions (name, root table,
format, enabled toggle, last run status, next scheduled run, actions). A preview panel runs the
same query path Run Now uses, capped to N rows; an execution-history panel reads
`ExportDefinitionRunEntity`. No new UI framework or component library was introduced — this
extends the existing recursive component and schema-introspection panel.

## 6. Non-Goals

* **No live write-back connector to a second database.** Output stays the staging-folder file
  contract; a real System B API/DB target is a separate, larger effort.
* **No scripting/expression engine.** `Transform` is a small closed enum (§3), not a formula
  language.
* **No multi-tenant / multi-source-system support.** One ERP source.
* **No workflow/approval chains for generic exports.** Four-eyes is a regulatory property of the
  legacy CI pipeline, not extended here unless a specific export requires it.
* **No general-purpose plugin/extension API.** New capability extends `ExportNode`, not a
  registration mechanism.

## 7. Design decisions

* **Cron minimum granularity** is hourly, matching the project's existing scheduling convention.
* **The legacy single-mapping flow stays fully read/write, unconditionally.** `ExportMappingEndpoints`'s
  `PUT` endpoint (mapping and presets) is never locked out once an `ExportDefinition` exists — the
  "configure via CMDB Export Mapping, save, trigger via `POST /api/pipeline/run`" workflow doesn't
  need `ExportDefinition`s at all. `ExportDefinition`s are a separate, opt-in feature: they don't
  gate or supersede the legacy single-mapping flow. See §8.
* **Test-run cap** is 50 rows, fixed — not user-configurable.
* An existing `ExportMappingConfig`/preset is converted once, on startup, into `ExportDefinition`
  rows by an idempotent migrator (`ExportMappingField`→scalar-field node,
  `ExportMappingRelation`→array node with flattening, `ExportMappingNestedGroup`→object/array node
  verbatim); `AppSettings` keys are left in place but unread. No data loss, no manual re-entry.

## 8. Legacy coexistence

`run`/`test`/`preview` on an `ExportDefinition` deliberately skip `ExportRunEntity`/
`FileSystemExportSink`/four-eyes — those model the legacy staging contract specifically, out of
scope for generic definitions (§6). `run` executes synchronously and returns the built artifact
directly in the HTTP response — usable as a one-shot trigger from an external program. `test`
shares that exact path (capped at 50 rows, flagged `IsTestRun`) but returns the tracked run row as
JSON instead of bytes, since its purpose is config validation. `preview` stays the lighter,
untracked, capped JSON call the UI needs — it writes no history row.

**SOLID, concretely:** SRP — validation, SQL generation, and format writing stay three separate
concerns. OCP — a new nesting shape or source table needs zero code; a new output *format* is the
one thing needing a new class, behind `IExportFormatWriter`. LSP — every format writer accepts the
same `ExportNode` tree and either honors nesting or documented-flattens it, never throws on a
shape another accepts. ISP — the UI talks to a narrow `IExportNodeApi` (CRUD + validate + preview).
DIP — `DynamicExportService` never builds or accepts a concrete `NpgsqlConnection` directly; every
query method takes an `IDataSourceProvider`/`DataSourceConfig` pair instead, resolved via
`IDataSourceProviderResolver`. See [Data Source Abstraction](/architecture/data-source-abstraction.md)
for the full picture, including what's deliberately still Postgres-specific (SQL dialect
generation) and what's still direct `NpgsqlConnection` (`ImportRunReleaser`'s four-eyes commit
transaction).

## Related

- [DynamicExportService](/pipeline/dynamic-export-service.md) — the live pipeline this design extends
- [Export Worker](/pipeline/export-worker.md) — the sibling `ExportDefinitionWorker` is modeled on
- [Dynamic Export](/dynamic-export/index.md) — the `ExportNode` tree, scheduler, and run history in operation
- [Export Definition API](/api/export-definition-api.md) — the CRUD/run/test/preview HTTP surface
- [Code Health Backlog](/planning/code-health-backlog.md) — orthogonal frontend-complexity backlog, not part of this design
- [Import Mapping Presets from Export Provenance](/pipeline/import-mapping-presets.md) — adds the
  optional `IntegrationKey`/`ContractVersion` pair and `CorrelationKeySourceField` to the
  `ExportDefinition` shape above
