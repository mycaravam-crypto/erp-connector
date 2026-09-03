---
type: Pipeline Design (Planned)
title: Export Definitions 2.0 — generic, tree-based multi-export
description: Spec and live implementation status for Phase 14, generalizing DynamicExportService's one mapping into N independently scheduled, arbitrarily-nested export definitions.
resource: src/Connector.Core/DynamicExport/ExportNode.cs
tags: [pipeline, dynamic-mapping, planned, phase-14]
timestamp: 2026-08-19T00:00:00Z
---

> Status: spec approved, implementation in progress (Phase 14 — see
> [Implementation status](#implementation-status)). The "legacy fixed CI pipeline" this doc
> originally treated as a protected, correctness-critical system was in fact already dead code
> (zero DI registration, zero live traffic) and was deleted in the same Phase 13 pass — see the
> [changelog](/changelog.md) and [DynamicExportService](/pipeline/dynamic-export-service.md).
> Nothing live was removed; every requirement below still applies unchanged to the dynamic-mapping
> path. File paths reflect the codebase as of 2026-08-19 and may have moved since.

---

## 0. Engineering Directive (non-negotiable)

Every decision is judged against this before "does it match the requirement." Satisfying the
requirement while violating the directive means the design isn't done yet.

| Principle | Concretely |
|---|---|
| **Minimal code** | Generalize an existing type/service before adding a new one. |
| **Minimal complexity** | One recursive `ExportNode` tree replaces the three parallel, overlapping shapes today (`Fields`/`Relations`/`NestedGroups` — §2). One mental model, not three. |
| **Maximal documentation** | Every public type/method gets a why-not-what doc comment; every new subsystem gets a `knowledge/` entry (§9). |
| **Highest code quality** | No silent failure paths; every validation error is specific and actionable. |
| **Clean code** | Small, single-purpose functions; enums over boolean flags; no comments restating the code. |
| **SOLID** | Applied per-layer in §8. |

**Build the smallest possible generic engine, not a framework.** Arbitrary nesting is one
recursive tree type walked by one recursive service method — not a plugin system, DSL, or codegen.

---

## 1. Vision

Today the connector does one job well: extract CIs from ERP, minimize for GDPR, and release to
ServiceNow via a four-eyes, air-gapped staging folder — via `DynamicExportService` and
`SchemaView.vue` (Phase 8–12). Four-eyes release, GDPR-denylist enforcement, audit log, and
sequence-integrity checks are correctness-critical and **stay exactly as-is** — 2.0 does not
touch them.

Today the dynamic mapping supports *one* mapping (plus named presets) for *one* source table,
with relation-flattening for CSV/Excel and JSON-only nested groups. **2.0 turns this into any
number of independently named, saved, scheduled export definitions**, each rooted at any table,
with unlimited nesting depth, in every output format, each with its own field-level
transformation, schedule, and run history.

Mapping the original System A→B brief onto this codebase:

* **System A (master)** = the ERP Postgres database, read via existing schema-introspection and
  direct-Npgsql query (`ConnectionEndpoints`, `IntrospectSchemaAsync`, `DynamicExportService`).
* **System B (slave)** = the existing staging-folder output (Excel/CSV/JSON + SHA-256 manifest).
  **No live write-back connector to a second database** — see Non-Goals (§10).

---

## 2. Current State

| Capability | Current implementation | File(s) |
|---|---|---|
| Schema introspection, FK auto-detection | `IntrospectSchemaAsync`, suggested-relations UI | `ConnectionEndpoints.cs`, `SchemaView.vue` |
| Field rename / exclude | `ExportMappingField(SourceName, TargetName, Enabled)` | `ExportMappingTypes.cs` |
| Flat 1:N relation flattening (CSV/Excel) | `ExportMappingRelation` + `FlattenStrategy`/`Delimiter` | `ExportMappingTypes.cs`, `DynamicExportService` |
| Arbitrary-depth nesting — **JSON only** | `ExportMappingNestedGroup` (self-referencing, `Kind: object\|array`) | `ExportMappingTypes.cs` |
| Recursive tree-editor UI — **JSON only** | `NestedGroupEditor.vue` | `src/connector-ui/src/components/NestedGroupEditor.vue` |
| Config persistence | One `ExportMappingConfig` blob + presets dict, raw JSON in `AppSettings` | `ExportMappingEndpoints.cs`, `ExportLogDbContext.cs` |
| GDPR field denylist | Validated recursively at save time, every nesting depth | `ExportMappingEndpoints.ValidateNestedGroups` |
| Scheduling | One global daily time + one global format for the whole app | `ExportWorker.cs` |
| Run history | `ExportRunEntity` — hardwired to four-eyes/delivery fields | `ExportRunEntity.cs` |
| Preview | **Closed (Phase 13).** Shares `BuildExportAsync`/`UsesNestedJson` with Run Now and the worker | `PipelineEndpoints.cs`, `DynamicExportService.cs` |
| Value transformation, constants, null handling, type conversion | **Does not exist** | — |
| Filters/conditions on rows | **Does not exist** | — |
| Multiple independent, named, schedulable exports | **Does not exist** (presets are save-slots, not first-class entities) | — |
| Per-export enable/disable, duplicate, execution history | **Does not exist** | — |

---

## 3. Gap Analysis

| 2.0 Requirement | Blocked by | Resolution |
|---|---|---|
| Arbitrary nesting in **every** format | `NestedGroups` is JSON-only; CSV/Excel use the separate flat `Relations` shape | Unify into one `ExportNode` tree (§4); each format writer flattens or nests as appropriate |
| Field-level transformation | Not modeled | `FieldMapping.Transform` (§5) |
| Row filters/conditions per node | Not modeled | `ExportNode.Filter` (§4) |
| N independent, named, schedulable exports | Only one mapping + presets exist | First-class DB rows (§4), not an `AppSettings` blob |
| Per-export schedule | One global daily time | `ExportDefinition.Schedule` (cron), one generalized scheduler loop |
| Per-export execution history | `ExportRunEntity` is CI-pipeline-specific | New `ExportDefinitionRunEntity`, separate — four-eyes/delivery semantics don't apply to generic exports |
| Save, edit, duplicate, enable/disable | Presets support save/edit/delete only | CRUD + `Duplicate` + `IsEnabled` on `ExportDefinition` |

---

## 4. Data Model

Replaces the three parallel shapes in `ExportMappingTypes.cs` (`Fields`, `Relations`,
`NestedGroups`) with **one** recursive tree — one shape, one validator, one query builder, one
UI component instead of three of each.

```
ExportDefinition                          (new EF Core entity — replaces the AppSettings blob)
├── Id, Name, Description
├── RootTable            : string
├── RootNode              : ExportNode      (the tree — see below)
├── OutputFormat          : csv | xlsx | json
├── IsEnabled             : bool
├── Schedule              : string?         (cron expression; null = manual only)
├── ConfigVersion         : int             (incremented on every save; carried onto each run)
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

ExportDefinitionRunEntity                    (new — execution history, one row per run)
├── Id, ExportDefinitionId, ConfigVersion    (which saved version ran — traceability)
├── StartedAt / FinishedAt (UTC)
├── Status                : Success | Failed | Running
├── RecordCount
├── ErrorMessage           : string?          (populated on Failed — never a silent empty result)
└── TriggeredBy             : string           (username, or "scheduler")
```

**Why a tree, not three shapes:** `Kind` extends `ExportMappingNestedGroup.Kind`
(`"object"|"array"`) with `scalar-field` and `root`. A flat CSV/Excel row is just the case where
every node's `Kind` is `scalar-field` at depth 1 — no separate `Relations` shape needed. Adding a
fourth CSV nesting level requires zero new types, only a smarter writer.

**Migration:** existing `ExportMappingConfig`/preset JSON is read once at startup by a one-time
converter (`ExportMappingField`→scalar-field node, `ExportMappingRelation`→array node with
flattening, `ExportMappingNestedGroup`→object/array node verbatim) and written as `ExportDefinition`
rows; `AppSettings` keys are left in place but unread. No data loss, no manual re-entry.

---

## 5. Field Mapping & Transformation

Every `scalar-field` node's `Mapping` supports, at minimum:

| Capability | Mechanism |
|---|---|
| Rename | `TargetKey` differs from `SourceField` (unchanged) |
| Exclude | `Enabled = false` (unchanged) |
| Constant/default value | `Transform = constant` + `TransformArg`; or `DefaultValue` for null-fallback |
| Null handling | `DefaultValue` substituted when the source value is `NULL` |
| Data-type conversion | `DataType` — coerced in C# at read time, not a SQL cast (see Slice 2 deviation below) |
| Value transformation | `Transform` enum — a small closed set, not a scripting engine (see Non-Goals) |

Example: `article.article_number → product.sku` is one `scalar-field` node with
`SourceField = "article_number"`, `TargetKey = "sku"`.

---

## 6. Scheduling

* `ExportDefinition.Schedule` is a 5-field cron expression, or `null` for manual-only.
* UI offers presets (Manual/Hourly/Daily/Weekly) plus an advanced free-text cron field.
* One background worker (`ExportDefinitionWorker`, a sibling of `ExportWorker`, not a replacement)
  polls enabled `ExportDefinition` rows whose cron is due.
* Every run — scheduled or manual — writes exactly one `ExportDefinitionRunEntity` row. "Test"
  shares the same run path as "Run Now", just capped and flagged, never a separate untracked path.

---

## 7. UI Requirements

Generalizes `NestedGroupEditor.vue` — already the recursive tree-editor this calls for — to stop
being JSON-only and become the single editor for every `ExportNode`, every format.

1. **Export list view**: name, root table, format, enabled toggle, last run status, next
   scheduled run, actions (edit/duplicate/test/run now/delete).
2. **Tree builder**: root = table picker (reused from `SchemaView.vue`); "Add field" → leaf node
   with inline mapping editor; "Add related entity" → object/array node prefilled from FK
   auto-detection; reorder/rename/remove inline; whole tree rendered as an indented outline.
3. **Preview panel**: runs the same query path Run Now uses, capped to N rows.
4. **Execution history panel**: per-definition table from `ExportDefinitionRunEntity`.

No new UI framework or component library — an extension of the existing recursive component and
schema-introspection panel.

---

## 8. Non-Functional Requirements

| Quality | Requirement |
|---|---|
| **Usability** | Build a 3-level nested export using only the tree UI — no JSON, no SQL. |
| **Maintainability** | New source table needs zero new C# types. New transform kinds are the only case requiring code (a new `Transform` enum member), by design (§10). |
| **Flexibility** | Nesting depth bounded only by `MaxNestedDepth`, not the data model. |
| **Reliability** | A failed run writes `Status = Failed` + a specific error — never a partial output with `Status = Success`. |
| **Security** | `RequireAuthorization()` on every endpoint; GDPR denylist applies at every node, every depth. |
| **Traceability** | Every run carries `ConfigVersion` — "what ran" is reconstructable after later edits. |

**SOLID, concretely:** SRP — validation, SQL generation, and format writing stay three separate
concerns. OCP — a new nesting shape or source table needs zero code; a new output *format* is the
one thing needing a new class, behind `IExportFormatWriter`. LSP — every format writer accepts
the same `ExportNode` tree and either honors nesting or documented-flattens it, never throws on a
shape another accepts. ISP — the UI talks to a narrow `IExportNodeApi` (CRUD + validate +
preview). DIP — `DynamicExportService` builds a concrete `NpgsqlConnection` directly; introduce a
connection-provider abstraction only if per-export connection sourcing becomes a real need.

---

## 9. Documentation Requirements

* Every new type in `Connector.Core.DynamicExport` gets a why-not-what doc comment.
* This page extends [DynamicExportService](/pipeline/dynamic-export-service.md) rather than
  replacing it.
* Changelog gets a "Phase 14 — Generic export definitions ✅" entry on completion.

---

## 10. Non-Goals

* **No live write-back connector to a second database.** Output stays the staging-folder file
  contract; a real System B API/DB target is a separate, larger effort.
* **No scripting/expression engine.** `Transform` is a small closed enum (§5), not a formula
  language.
* **No multi-tenant / multi-source-system support.** One ERP source, as today.
* **No workflow/approval chains for generic exports.** Four-eyes is a regulatory property of the
  legacy CI pipeline, not extended here unless a specific export requires it.
* **No general-purpose plugin/extension API.** New capability extends `ExportNode`, not a
  registration mechanism.

---

## 11. Open Decisions

1. **Cron minimum granularity** — **Resolved: hourly**, matching the project's existing
   scheduling convention.
2. **Legacy mapping cutover** — **Resolved: `ExportMappingEndpoints` `PUT` (mapping and presets)
   stays fully read/write, unconditionally.** A read-only lock once the migrator ran shipped
   first, then was reversed: it broke the still-supported "configure via Step 3, save, trigger via
   `POST /api/pipeline/run`" workflow, which never needed ExportDefinitions and got silently
   locked out the moment the migrator saw a legacy config. ExportDefinitions remains a separate,
   opt-in feature — it doesn't gate or supersede the legacy single-mapping flow.
3. **Test-run cap** — **Resolved: 50 rows, fixed** (not user-configurable) for this phase.

---

## Appendix — Original Requirements (verbatim, for traceability)

<details>
<summary>Click to expand the original System A → System B brief this document refines</summary>

Build a generic ERP connector that transfers configurable datasets from **Master System A** to
**Slave System B**. The two systems have completely different database schemas and no
native/direct integration.

The connector must not be limited to predefined objects or structures. A user must be able to
define arbitrary hierarchical/nested exports using data and relationships available in System A —
for example Articles → related Orders → Serial Numbers / Shipment Details, and Articles → related
Manufacturers → multiple Addresses — with structure and nesting depth dynamically configurable
rather than hardcoded.

The primary UI requirement is a tree-based export builder: select a root entity, add fields, add
related entities as child nodes recursively, remove/reorder/rename/configure nodes and fields, see
the full tree, and preview representative output before saving or executing.

Every exported field must support explicit source→target mapping (e.g.
`article.article_number → product.sku`), including custom target names, field exclusion,
constants/default values, optional transformation, null/default handling, and data-type
conversion.

An export definition is a persistent configuration (name, description, root entity, fields,
relationships, filters, mappings, transformations, output structure, target configuration,
schedule) that can be saved, edited, duplicated, tested, enabled/disabled, and manually executed,
with scheduled execution (manual/hourly/daily/weekly/cron) and visible execution history.

Architecturally: Source Model → Export Model → Mapping/Transformation → Target Model, interpreted
recursively at runtime from metadata rather than implemented per object type. Quality bar:
usable without programming, maintainable without new export types per entity, flexible nesting and
mapping, reliable (no silent partial data), secure (permission-controlled), and traceable
(config version, timestamp, result, errors per execution).

</details>

---

## Implementation status

6 slices, each roughly PR-sized and independently shippable. Start with Slice 1 — everything else
depends on the `ExportNode`/`ExportDefinition` shape being settled first.

- [x] **Slice 1 — Data model + migration converter.** `ExportNode`/`FieldMapping` records,
      `ExportDefinitionEntity`/`ExportDefinitionRunEntity`, EF migration, and a one-time,
      idempotent converter from legacy config blobs to `ExportDefinition` rows (`IsEnabled = false`
      on migrated rows). Verified: build/test/csharpier clean, migration cross-checked against a
      freshly-generated `dotnet ef migrations add`.
- [x] **Slice 2 — Query/format-writer engine.** `DynamicExportService` extended with an
      `ExportNode` tree-walking query builder (generalizes `BuildNestedGroupExpr` to also emit
      scalar columns, apply `Filter` fragments, and nest arbitrarily in every format). New
      `IExportFormatWriter`/`CsvExportFormatWriter`/`ExcelExportFormatWriter`/`JsonExportFormatWriter`
      (`ExportFormatWriters.cs`). 98/98 tests passing including real-DB integration tests.
      **Deviation:** scalar columns are always read as SQL `::text`, not cast per
      `FieldMapping.DataType` — a DB-side numeric cast fails the entire query on one bad row, so
      `DataType` coercion happens in C# instead (`ApplyExportNodeMappingsRecursive`/
      `CoerceToDataType`), degrading only the one bad field to a best-effort string.
- [x] **Slice 3 — API endpoints (CRUD + run + history).** New `ExportDefinitionEndpoints.cs`:
      `GET/POST /api/export-definitions`, `GET/PUT/DELETE /api/export-definitions/{id}`,
      `.../duplicate`, `PATCH .../enable`, `POST .../preview|run|test`, `GET .../runs`. A
      recursive validator (depth guard, identifier-safety regex, GDPR denylist at every depth,
      duplicate-`TargetKey` check).
      **Design note:** `run`/`test` deliberately skip `ExportRunEntity`/`FileSystemExportSink`/
      four-eyes — those model the legacy staging contract, out of scope per §10. `run` executes
      synchronously and returns the built artifact directly in the HTTP response
      (`Content-Disposition: attachment`, plus `X-Export-Run-Id`/`X-Record-Count`/
      `X-Config-Version` headers) — usable as a one-shot trigger from an external program. `test`
      shares that exact path (capped at 50 rows, flagged `IsTestRun`) but returns the tracked run
      row as JSON instead of bytes, since its purpose is config validation. `preview` stays the
      lighter, untracked, capped JSON call the UI needs — it writes no history row. Every
      endpoint requires authentication only, no special role.
- [ ] **Slice 4 — Scheduler.** New `ExportDefinitionWorker : BackgroundService`, polling enabled
      `ExportDefinition` rows on cron (hourly minimum). Every run writes one
      `ExportDefinitionRunEntity` row, `Status = Failed` + a specific error on any failure or
      zero-record result.
- [ ] **Slice 5 — Frontend.** New `api/exportDefinitions.ts`; generalize `NestedGroupEditor.vue`
      in place (add a `scalar-field` node kind with inline transform UI; stop gating it to JSON);
      new `ExportDefinitionsListView.vue` + `ExportDefinitionEditView.vue`; new routes. Acceptance
      check: build a 3-level nested export using only the tree UI in a running browser.
- [ ] **Slice 6 — Docs.** New `knowledge/dynamic-export/` bundle for the `ExportNode` tree, the
      scheduler, and the run-history entity — extends, doesn't replace, the legacy single-mapping
      docs. New changelog entry.

**Before Slice 3:** no outstanding verification debt from Slice 2 (build/test/format green
against a real database). Slice 3 builds on `BuildExportNodeAsync`/`ExecuteExportNodeQueryAsync`/
`GetExportNodeColumnNames` as its Preview/Run-Now/Test entry points, and on
`ExportFormatWriterFactory` to resolve a writer directly. The validator still needs its own
identifier-safety regex pass over `ExportNode.Filter`/`SourceField`/`RelatedTable`/`JoinKey`/
`SourceJoinKey` before user-supplied trees reach production — same trust boundary as the legacy
`DynamicExportService`, which relies on save-time validation, not query-time.

### Verification (end to end, after all slices)

1. `dotnet build Connector.sln -c Release` clean, `dotnet csharpier check .` clean, full
   `dotnet test` green including Postgres-integration tests against the `testdb` fixture.
2. `npm run type-check && npm run test` in `src/connector-ui`, `npx fallow audit --base
   origin/main` clean (or document new findings — see [Code Health Backlog](/processes/code-health-backlog.md)).
3. Manual browser walkthrough: create a definition rooted at a table with a 3-level nested
   relation (`manufacturer` → `manufacturer_address`, `testdb` fixture), add a field transform and
   a `DefaultValue`, preview, save, set an hourly schedule, run manually, confirm the run appears
   in history with the right `ConfigVersion`, confirm CSV/Excel output contains the nested data
   flattened.
4. Confirm the legacy single-mapping flow (`/export-schema`) still round-trips read/write after
   the one-time migration converter runs, and the converter doesn't duplicate rows on a second
   app restart.

## Related

- [DynamicExportService](/pipeline/dynamic-export-service.md) — the live pipeline this design extends
- [Export Worker](/pipeline/export-worker.md) — the sibling the new scheduler (Slice 4) is modeled on
- [Code Health Backlog](/processes/code-health-backlog.md) — orthogonal frontend-complexity backlog, not part of this plan
