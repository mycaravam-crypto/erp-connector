# ERP Connector 2.0 — Requirements

> Status: Draft for review. Grounded in the current codebase (Phase 12 / `main` as of 2026-08-19).
> Supersedes the generic first draft of this document with a spec fitted to this repository's
> actual architecture, so it can be estimated and built against directly rather than re-interpreted.

> **Correction (Phase 13 / 2.0 simplification, same day):** §1 below describes the legacy fixed
> CI pipeline (`Connector.Export`'s `Filter → Minimize → Map → Package` steps,
> `ISchemaMapper`/`IDataMinimizer`/`IExportFilter`/`IPackager`/`IErpReader`) as correctness-critical
> and untouched by 2.0. In fact, at the exact commit this document is grounded in, `Program.cs`
> never registered any of those five interfaces in DI and no endpoint referenced them — the
> pipeline had zero live traffic; only the dynamic-mapping path (`DynamicExportService`) was ever
> reachable. A parallel simplification pass on this same day deleted that dead pipeline outright
> (see `ROADMAP.md` Phase 13, `knowledge/pipeline/dynamic-export-service.md`) after verifying this
> against the codebase directly. Nothing live was removed, and every requirement in this document
> still applies unchanged to the dynamic-mapping path — but treat §1's framing of a protected
> "System A→B legacy pipeline" as historical, not current. §2's table has been updated in place
> where that pass also closed a listed gap or fixed a file-path error; other file paths in this
> document reflect the codebase as of the original commit and may have moved since.

---

## 0. Engineering Directive (non-negotiable)

Every design and implementation decision in 2.0 is judged against this directive before anything
else, including "does it match the requirement." A feature that satisfies the requirement but
violates the directive is not done — it needs a simpler design.

| Principle | What it means here, concretely |
|---|---|
| **Minimal code** | Prefer generalizing an existing type/service over adding a new one. Before writing a class, show that no existing class in `Connector.Core`/`Connector.Infrastructure` can be widened to cover the case. |
| **Minimal complexity** | One recursive data structure (`ExportNode`) replaces `Fields` + `Relations` + `NestedGroups` (three parallel, overlapping shapes today — see §2). One mental model, not three. |
| **Maximal documentation** | Every public type and non-trivial method gets an XML-doc (`.cs`) or JSDoc (`.ts`/`.vue`) comment stating *why*, not *what* — matching the existing style in `ExportMappingTypes.cs`. Every new subsystem gets a `knowledge/` entry (see §9). |
| **Highest code quality** | No silent failure paths (this codebase already treats "silently incomplete data" as a defect — see `ExportRunStatus`); every validation error is specific and actionable, matching the existing `ExportMappingEndpoints.ValidateConfigAsync` style. |
| **Clean code** | Small, single-purpose functions; no boolean-flag parameters where an enum reads clearer; no comments that restate the code. |
| **SOLID** | Applied concretely per-layer in §8, not as decoration. |

**Read together, these mean: build the smallest possible generic engine, not a framework.** Every
"arbitrary nesting" requirement below is satisfied by one recursive tree type walked by one
recursive service method — not by a plugin system, not by a rules DSL, not by code generation.

---

## 1. Vision

Today the connector does one job extremely well: extract CIs (Configuration Items) from ERP,
minimize for GDPR, and release to ServiceNow via a four-eyes, air-gapped staging folder — via a
**dynamic, schema-introspecting export builder** (`DynamicExportService`, `SchemaView.vue`,
Phase 8–12 of the roadmap). This is the part 2.0 grows. The four-eyes release, GDPR-denylist
enforcement, audit log, and sequence-integrity checks around it are correctness-critical and
**stay exactly as-is** — 2.0 does not touch them.

(An earlier, separately-designed fixed CI pipeline — `Connector.Export`'s
`Filter → Minimize → Map → Package` steps — was never wired into the running application and has
since been removed; see the Phase 13 correction above.)

Today the dynamic mapping supports *one* mapping (plus named presets) for *one* source table, with
relation-flattening for CSV/Excel and JSON-only nested groups. **2.0 turns this into what it was
always heading toward**: any number of independently named, saved, scheduled export definitions,
each rooted at any table, with unlimited nesting depth, available in every output format, each
with its own field-level transformation and its own schedule and run history.

The two systems in the original brief map onto this codebase as:

* **System A (master)** = the ERP Postgres database, read via the existing schema-introspection
  and direct-Npgsql query path (`ConnectionEndpoints`, `IntrospectSchemaAsync`,
  `DynamicExportService`) — not via `IErpReader`, which belonged to the removed fixed pipeline
  and was never used by the dynamic mapping.
* **System B (slave)** = the existing staging-folder output contract (Excel / CSV / JSON +
  SHA-256 manifest). **2.0 does not add a live write-back connector to a second database** — see
  Non-Goals (§10). "Target field/path in System B" means the exported column name, JSON key path,
  or nested-object shape in that output package, exactly as `TargetName`/`TargetKey` do today.

---

## 2. Current State — what already exists

This section exists so 2.0 is scoped as a **delta**, not rewritten from a blank page.

| Capability | Current implementation | File(s) |
|---|---|---|
| Schema introspection, FK auto-detection | `IntrospectSchemaAsync`, suggested-relations UI | `ConnectionEndpoints.cs`, `SchemaView.vue` |
| Field rename / exclude | `ExportMappingField(SourceName, TargetName, Enabled)` | `ExportMappingTypes.cs` |
| Flat 1:N relation flattening (CSV/Excel) | `ExportMappingRelation` + `FlattenStrategy`/`Delimiter` | `ExportMappingTypes.cs`, `DynamicExportService` |
| Arbitrary-depth nesting — **JSON only** | `ExportMappingNestedGroup` (self-referencing, `Kind: object\|array`) | `ExportMappingTypes.cs` |
| Recursive tree-editor UI — **JSON only** | `NestedGroupEditor.vue` (self-referencing Vue component) | `src/connector-ui/src/components/NestedGroupEditor.vue` |
| Config persistence | One `ExportMappingConfig` blob + a `name → config` presets dictionary, stored as raw JSON in the `AppSettings` key/value table | `ExportMappingEndpoints.cs`, `ExportLogDbContext.cs` |
| GDPR field denylist enforcement | Validated recursively at save time, at every nesting depth | `ExportMappingEndpoints.ValidateNestedGroups` |
| Scheduling | **One** global daily time-of-day + one global output format (`ExportWorker`, `SchedulerConfigData`) for the whole app — not per-export | `ExportWorker.cs` |
| Run history / traceability | `ExportRunEntity` — hardwired to the four-eyes/delivery fields (`ApprovedBy`, `DeliveredAt`, …) required for CI/ServiceNow releases | `ExportRunEntity.cs` |
| Preview | **Closed (Phase 13).** Preview, Run Now, and the scheduled worker now share one decision point (`DynamicExportService.BuildExportAsync`/`UsesNestedJson`) so preview reflects nested-group mappings too — was a documented gap in `ROADMAP.md` Phase 12, no longer current | `PipelineEndpoints.cs`, `DynamicExportService.cs` |
| Value transformation, constants, null handling, type conversion | **Does not exist** | — |
| Filters/conditions on rows | **Does not exist** | — |
| Multiple independent, named, schedulable exports | **Does not exist** (presets are save-slots for one mapping shape, not first-class scheduled entities) | — |
| Per-export enable/disable, duplicate, execution history | **Does not exist** | — |

---

## 3. Gap Analysis — requirement vs. current state

| 2.0 Requirement | Blocked by | Resolution direction |
|---|---|---|
| Arbitrary nesting in **every** output format | `NestedGroups` is JSON-only; CSV/Excel use the separate flat `Relations` shape | Unify `Fields` + `Relations` + `NestedGroups` into one recursive `ExportNode` tree (§4); each format's writer flattens or nests it as appropriate for that format |
| ~~Preview reflects the full nested structure~~ | **Closed (Phase 13)** — was: Preview endpoint used the flat query path only | Preview, Run Now, and the scheduled worker now share one decision point (`BuildExportAsync`/`UsesNestedJson`); no remaining work here |
| Field-level transformation (constants, null handling, type conversion) | Not modeled at all today | Add `FieldMapping.Transform` (§5) |
| Row filters/conditions per node | Not modeled | Add `ExportNode.Filter` (§4) |
| N independent, named, schedulable exports | Only one mapping + presets-as-save-slots exist | Promote export definitions to first-class DB rows (§4) instead of an `AppSettings` blob |
| Per-export schedule (manual/hourly/daily/weekly/cron) | One global daily time for the whole app | `ExportDefinition.Schedule` (cron expression), one generalized scheduler loop instead of one hardcoded worker |
| Per-export execution history | `ExportRunEntity` is CI-pipeline-specific | New `ExportDefinitionRunEntity`, separate from the legacy `ExportRunEntity` — the legacy pipeline's four-eyes/delivery semantics do not apply to arbitrary generic exports and must not be bolted onto them |
| Save, edit, duplicate, enable/disable | Presets dictionary supports save/edit/delete only | CRUD + `Duplicate` + `IsEnabled` on the new `ExportDefinition` entity |

---

## 4. Data Model

Replaces the three parallel shapes in `ExportMappingTypes.cs` (`Fields`, `Relations`,
`NestedGroups`) with **one** recursive tree. This is the single biggest complexity reduction in
2.0: one shape, one validator, one query builder, one UI component — instead of three of each.

```
ExportDefinition                          (new EF Core entity — replaces the AppSettings blob)
├── Id, Name, Description
├── RootTable            : string
├── RootNode              : ExportNode      (the tree — see below)
├── OutputFormat          : csv | xlsx | json
├── IsEnabled             : bool
├── Schedule              : string?         (cron expression; null = manual only)
├── ConfigVersion         : int             (incremented on every save; carried onto each run for traceability)
├── CreatedBy / CreatedAt / UpdatedBy / UpdatedAt

ExportNode                                  (recursive — this IS the arbitrary-nesting mechanism)
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

**Why a tree instead of three shapes:** `Kind` on `ExportNode` is exactly the discriminator
`ExportMappingNestedGroup.Kind` already uses (`"object" | "array"`), extended with `scalar-field`
and `root`. A flat CSV/Excel row is just the special case where every node's `Kind` is
`scalar-field` at depth 1 — no separate `Relations` shape is needed to express it. This is Open/Closed
in practice: CSV support for a fourth nesting level requires *zero* new types, only a smarter
writer.

**Migration note:** existing `ExportMappingConfig`/preset JSON blobs are read once at startup by a
one-time converter (`ExportMappingField`→scalar-field node, `ExportMappingRelation`→array node with
flattening, `ExportMappingNestedGroup`→object/array node verbatim) and written as `ExportDefinition`
rows, then the `AppSettings` keys are left in place but unread. No data loss, no manual re-entry.

---

## 5. Field Mapping & Transformation

Every `scalar-field` node's `Mapping` supports, at minimum:

| Capability | Mechanism |
|---|---|
| Rename | `TargetKey` differs from `SourceField` (already exists today — unchanged) |
| Exclude | `Enabled = false` (already exists today — unchanged) |
| Constant/default value | `Transform = constant`, `TransformArg` holds the literal; or `DefaultValue` for null-fallback only |
| Null handling | `DefaultValue` substituted when the source value is `NULL` |
| Data-type conversion | `DataType` — coercion applied at query-build time (e.g. `::text`, `::numeric` in the generated SQL, matching the existing `QI()`-quoted, parameterized query style) |
| Value transformation | `Transform` enum — deliberately a **small, closed set** (uppercase/lowercase/trim/date-format), not a scripting engine (see Non-Goals) |

This mirrors the source→target example from the original brief directly:
`article.article_number → product.sku` becomes one `scalar-field` `ExportNode` with
`SourceField = "article_number"`, `TargetKey = "sku"`.

---

## 6. Scheduling

* `ExportDefinition.Schedule` is a standard 5-field cron expression, or `null` for manual-only.
* UI offers presets (Manual / Hourly / Daily / Weekly) that populate the cron string, plus an
  advanced free-text cron field — same UX pattern as picking a cron schedule already used
  elsewhere in this project's tooling (see `create_trigger`'s cron convention), so it isn't a new
  concept for anyone touching this codebase.
* One background worker (generalizing `ExportWorker`) polls enabled `ExportDefinition` rows whose
  cron schedule is due, rather than one hardcoded time-of-day for the whole app. The legacy CI
  pipeline's `ExportWorker` is untouched; the new worker is a sibling, not a replacement.
* Every run — scheduled or manual — writes exactly one `ExportDefinitionRunEntity` row. Manual
  "Run Now" and "Test" both go through the same run path so history is complete and honest; "Test"
  differs only in that it caps `RecordCount` (e.g. top 50 rows) and is flagged as such, it never
  takes a separate untracked code path.

---

## 7. UI Requirements

Builds on `NestedGroupEditor.vue`, which is **already the recursive tree-editor component this
requirement calls for** — it just needs to stop being JSON-only and become the single editor for
every `ExportNode`, for every format.

1. **Export list view**: name, root table, format, enabled/disabled toggle, last run status,
   next scheduled run, actions (edit / duplicate / test / run now / delete).
2. **Tree builder** (generalized `NestedGroupEditor.vue`):
   * Root node = table picker (reuses the existing schema-introspection dropdown from
     `SchemaView.vue`).
   * "Add field" → leaf `scalar-field` node with mapping editor inline.
   * "Add related entity" → `object`/`array` node, prefilled from FK auto-detection
     (`ForeignKeyTable`/`ForeignKeyColumn`, already implemented — reused, not rebuilt).
   * Reorder via drag-or-buttons; rename via inline `TargetKey` edit; remove via a per-node
     delete affordance already established in `SchemaView.vue`'s field tables.
   * Whole tree renders as a visual outline (indentation = nesting depth), matching how
     `NestedGroupEditor.vue` already renders JSON nesting today.
3. **Preview panel**: runs the *same* query path Run Now uses (§6), capped to N rows, so preview
   output is never allowed to drift out of sync with real output — this closes the exact gap
   documented in `ROADMAP.md` Phase 12.
4. **Execution history panel**: per-`ExportDefinition`, a table backed by `ExportDefinitionRunEntity`
   — timestamp, status, record count, config version, error message when failed.

No new UI framework, no new component library — this is an extension of an existing, working
recursive component plus the existing schema-introspection panel.

---

## 8. Non-Functional Requirements

| Quality | Requirement |
|---|---|
| **Usability** | A non-programmer builds a 3-level nested export using only the tree UI — no JSON hand-editing, no SQL. |
| **Maintainability** | Adding a new source table to an export requires zero new C# types — only schema introspection results and tree configuration. New field-transform kinds are the *only* case that requires a code change (a new `Transform` enum member), by design (§10). |
| **Flexibility** | Nesting depth is bounded only by the existing `MaxNestedDepth` guard (already enforced in `ValidateNestedGroup`), not by the data model. |
| **Reliability** | A failed run writes `Status = Failed` and a specific `ErrorMessage` — never a partial/truncated output file with `Status = Success`. Matches the existing project value already encoded in `ExportRunStatus`/`ExportSinkException`. |
| **Security** | `RequireAuthorization()` on every export-definition and execution endpoint (already the pattern for every existing endpoint in this codebase); GDPR denylist validation (already implemented, recursive) applies unchanged to every node at every depth. |
| **Traceability** | Every run row carries `ConfigVersion`, so "what ran" is always reconstructable even after the definition is edited later — this is the audit property the legacy pipeline already guarantees via `AuditService`, extended to generic exports. |

### SOLID, applied to this codebase specifically

* **SRP** — tree validation, SQL generation, and format writing stay three separate concerns (as
  they already are: `ExportMappingEndpoints` validates, `DynamicExportService` builds SQL, format
  writers serialize). 2.0 does not merge these.
* **OCP** — a new nesting shape or a new source table needs zero code changes (it's data); a new
  output *format* is the one thing that legitimately requires a new class — and it implements a
  shared `IExportFormatWriter` interface so adding one never touches existing formats.
* **LSP** — every `IExportFormatWriter` implementation (CSV/Excel/JSON) must accept the exact same
  `ExportNode` tree and either honor nesting or documented-flatten it; none may throw on a shape
  another accepts.
* **ISP** — the tree-editor UI talks to a narrow `IExportNodeApi` (CRUD + validate + preview), not
  the full backend surface.
* **DIP** — *Correction: `DynamicExportService` today builds a concrete `NpgsqlConnection` directly
  (`BuildConnectionString`), not behind an abstraction — `IErpReader` belonged to the removed fixed
  pipeline and was never used here.* If per-export connection-source flexibility becomes a real
  requirement, introduce a narrow connection-provider abstraction at that point; don't assume one
  already exists.

---

## 9. Documentation Requirements

* Every new type in `Connector.Core.DynamicExport` gets an XML-doc comment matching the existing
  style in `ExportMappingTypes.cs` — state *why*, not *what*.
* A new `knowledge/dynamic-export/` bundle (mirroring the existing `knowledge/domain|schema|pipeline|processes/`
  structure) documents: the `ExportNode` tree, the scheduler, and the run-history entity — extending
  `knowledge/pipeline/dynamic-export-service.md` (added in Phase 13), which is the current
  documentation baseline to match, not the removed legacy pipeline.
* `ROADMAP.md` gets a new "Phase 14 — Generic export definitions" entry on completion (Phase 13 is
  already taken by the 2.0 simplification pass — see the correction above), continuing
  the existing changelog convention rather than starting a separate doc.

---

## 10. Non-Goals (explicit, to hold the line on minimal complexity)

* **No live write-back connector to a second database.** Output remains the existing staging-folder
  file contract. Introducing a real "System B" API/DB target is a separate, much larger effort and
  is out of scope here.
* **No scripting/expression engine for transformations.** `Transform` is a small closed enum
  (§5), not a formula language. If a future export genuinely needs arbitrary logic, that is a
  deliberate, separately-scoped decision — not something 2.0 should back into via a generic
  "expression" field.
* **No multi-tenant / multi-source-system support.** One ERP source, as today.
* **No workflow engine or approval chains for generic exports.** Four-eyes release is a property
  of the legacy CI pipeline (regulatory requirement for ServiceNow CI data) and is not extended to
  generic exports unless a specific one requires it.
* **No general-purpose plugin/extension API.** New capability is added by extending `ExportNode`
  fields, not by a registration/plugin mechanism.

---

## 11. Open Decisions

1. **Cron minimum granularity** — reuse this project's existing "hourly minimum" convention (as
   used elsewhere for scheduled triggers), or allow sub-hourly for exports? *Recommendation:
   hourly minimum, for consistency and to bound worker load.*
2. **Legacy mapping cutover** — after the one-time converter (§4) runs, should the old
   `/api/export-mapping` single-config endpoints be kept read-only for backward compatibility, or
   removed once the UI moves to the new list view? *Recommendation: keep read-only for one release,
   remove in the following one.*
3. **Test-run cap** — is 50 rows the right default for "Test", or should it be user-configurable
   per definition?

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
