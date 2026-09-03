---
type: Changelog
title: Connector — Changelog
description: Phase-by-phase record of what shipped, newest first, plus current in-progress status.
tags: [changelog, roadmap, history]
timestamp: 2026-09-03T00:00:00Z
---

Last updated: 2026-09-03

---

## Phase 14 — Generic export definitions ✅

Generalizes [DynamicExportService](/pipeline/dynamic-export-service.md)'s one configurable
mapping into any number of independently named, scheduled, tree-based export definitions — see
[Export Definitions 2.0](/pipeline/export-definitions-2.0.md) for the full spec and
[Dynamic Export](/dynamic-export/index.md) for how the shipped result actually runs. The legacy
single-mapping flow (`/export-schema`, `POST /api/pipeline/run`) is untouched and stays a fully
supported, separate workflow — this was never a cutover (§11 decision #2).

| Slice | Item | Notes |
|---|---|---|
| 1 | Data model + migration converter | `ExportNode`/`FieldMapping`, `ExportDefinitionEntity`/`ExportDefinitionRunEntity`, EF migration, idempotent one-time converter from legacy config blobs |
| 2 | Query/format-writer engine | `DynamicExportService`'s `ExportNode` tree walker; `IExportFormatWriter`/CSV/Excel/JSON writers |
| 3 | API endpoints | `ExportDefinitionEndpoints.cs` — CRUD, duplicate, enable, preview, run, test, run history |
| 4 | Scheduler | New `ExportDefinitionWorker` (sibling of `ExportWorker`, not a replacement) polls enabled definitions every minute against a new purpose-built `CronSchedule` matcher; every trigger (manual, test, or scheduled) now shares one `ExportDefinitionRunner` and treats a zero-record result as `Failed`, closing a Slice 3 gap |
| 5 | Frontend | `api/exportDefinitions.ts` client; new `ExportNodeTreeEditor.vue` tree builder (add/remove fields and related entities, inline `FieldMapping` transform editing, per-node `Filter`) alongside — not replacing — the legacy `NestedGroupEditor.vue`; expanded list view (enable toggle, schedule, last-run status, duplicate/test/delete) and edit view (create-or-edit, root-table picker, preview panel, execution history) |
| 6 | Docs | This entry; new [`knowledge/dynamic-export/`](/dynamic-export/index.md) bundle; [Export Definitions 2.0](/pipeline/export-definitions-2.0.md) implementation-status checklist closed out |

**Verification:** full end-to-end pass, not just unit tests — `dotnet build`/`dotnet test`/
`dotnet csharpier check` clean (128 backend tests total, 20 of them against a real Postgres
`testdb`), `npm run type-check && npm run test` clean (231/231) and `npx fallow audit` clean
(scoped `.fallowrc.json` threshold overrides for the new tree-builder's inherently large
components — see [Code Health Backlog](/planning/code-health-backlog.md)), a real browser session
driving the actual tree-builder UI against a running backend, and the scheduler confirmed firing
a run unattended against a live app. See
[Export Definitions 2.0's Verification section](/pipeline/export-definitions-2.0.md#verification-end-to-end-after-all-slices)
for the full account.

---

## Phase 13 — 2.0: simplification after the exploration phase ✅

Phases 1–12 partly served as discovery — figuring out what the connector actually needed (fixed
schema vs. runtime-configurable mapping, single-table vs. joins, flat vs. nested export) while a
working system was already in production. That produced real accidental complexity alongside the
real requirements. This phase cuts it back down now that the shape of the requirement is known,
without touching the compliance-critical paths (four-eyes release, GDPR, audit log, sequence
integrity) that were requirements from day one, not discovery artifacts.

| Item | Notes |
|---|---|
| Removed the dead fixed pipeline | `Connector.Export`, `ErpConfigurationItem`/`ExportItem`/`MappedExportRecord`, `ISchemaMapper`/`IDataMinimizer`/`IExportFilter`/`IPackager`/`IErpReader` — never registered in DI, no live traffic since dynamic mapping (Phase 9–10) replaced them. See [DynamicExportService](/pipeline/dynamic-export-service.md) |
| Removed the demo-ERP browsing feature | `ErpDatabaseView`, `BomTree`/`BomTreeRow`/`CiDetailPanel`, `GET /api/erp/records`, the seeded SQLite `DemoErpDbContext` — an exploration/demo view with no production requirement; superseded by `SourceSchemaView` + the Postgres `testdb` fixture |
| Removed inert schema-mapping endpoints | `PATCH /api/schema/columns`/`/api/schema/mappings` persisted state nothing read; `GET /api/schema` is now a pure static read of the ICD reference |
| ICD contract decoupled explicitly | `IcdSchemaView`/`GET /api/schema` documented as read-only reference, independent of the live dynamic mapping — resolves the earlier "two competing schema models" ambiguity |
| Unified export execution path | `DynamicExportService.BuildExportAsync` + `UsesNestedJson` are now the single decision point for Run Now, `ExportWorker`, and Preview — closes the Phase 12 gap where nested-JSON only worked from Run Now. Scheduler settings gained a persisted `Format` field |
| SchemaView format-toggle bug fixed | The Step 3 "which JSON options to show" toggle silently shared a `localStorage` key with the Step 4 export-format picker, so peeking at nested-group config could silently change what Run Now exported. Decoupled; Step 3 now auto-selects when a loaded mapping has nested groups |
| API module boundaries cleaned up | `api/erp.ts` split into `api/mapping.ts` and `api/icdSchema.ts`; GDPR denylist endpoints moved from `api/audit.ts` to `api/scheduler.ts` |
| Docs reconciled with the running code | `knowledge/pipeline/*` and `knowledge/domain/*` pages for the deleted fixed pipeline marked superseded, pointing to [DynamicExportService](/pipeline/dynamic-export-service.md) |

---

## Phase 12 — Nested JSON export ✅

| Item | Notes |
|---|---|
| Nested JSON structure | `ExportMappingNestedGroup`/`ExportMappingNestedField` — JSON-only, additive to `ExportMappingConfig`; `object` (N:1) or `array` (1:N), nestable via `Children` |
| SQL generation | `ExecuteNestedJsonQueryAsync`/`BuildNestedGroupExpr` build one query with native `json_build_object`/`json_agg`; a zero-match array COALESCEs to `[]`, not `null` |
| JSON envelope wrapper | `ExportJsonWrapperConfig` — optional root key, items key, dynamic-timestamp metadata block; unset reproduces the legacy flat envelope |
| `NestedGroupEditor.vue` | Recursive, self-referencing component in `SchemaView`'s "Nested JSON Structure" section (JSON format only) |
| Save-time validation | `ValidateNestedGroups` checks depth cap (16), required fields, identifier safety, GDPR denylist, duplicate export keys at every depth. Does **not** check `JoinKey`/`SourceJoinKey` type-compatibility — a bad pairing only surfaces as a raw Postgres error at export time |
| Wired into Run Now only | `POST /api/pipeline/run?format=json` branches to nested when set. Preview and the nightly worker (Excel-only) still used the flat path — a gap closed in Phase 13 |
| Local Postgres test fixture | `docker-compose --profile test up -d testdb` seeds `manufacturer`/`manufacturer_address` (array-of-objects + empty-array case); backs `connection.spec.ts` and the integration tests below |
| Tests | `DynamicExportServiceNestedJsonPostgresTests.cs` — 7 real-Postgres integration tests |

---

## Phase 11 — Legacy mapping data regression fix ✅

| Item | Notes |
|---|---|
| Fixed crash on pre-Phase-10 mapping data | `export_mapping`/`export_presets` saved before relations gained `Fields`/`Delimiter`/`FlattenStrategy` deserialized those as `null`, crashing `SchemaView.vue`'s load path (misreported as "Could not reach the API") and, latently, Preview/Run Now/the scheduled worker |
| `ExportMappingJson` normalization helper | `DeserializeConfig`/`DeserializePresets` backfill `Fields → []`, `Delimiter → ", "`, `FlattenStrategy → "string_join"`; all 6 backend read sites route through it |
| Defense-in-depth guards | `DynamicExportService` null-coalesces `Fields`/`Delimiter` at point of use; `SchemaView.vue`/`ExportView.vue` guard the same way |

---

## Phase 10 — Export mapping usability ✅

| Item | Notes |
|---|---|
| Foreign-key auto-detection | `IntrospectSchemaAsync` detects FK constraints; `SourceColumnDto` carries `ForeignKeyTable`/`ForeignKeyColumn`; `SchemaView.vue` shows one-click "Suggested Relations" |
| Multi-field relations | `ExportMappingRelation.Fields` replaces the single source/target pair — a 1:N join now pulls any number of independently renamed columns |
| GDPR denylist gap closed | Save-time validation now scans relation fields too, not just primary columns |

---

## Phase 9 — Production hardening ✅

| Item | Notes |
|---|---|
| EF Core migrations | Replaced startup DDL; `MigrateAsync()` + bootstrap for pre-migration databases |
| Program.cs split | 9 endpoint modules; `Dtos.cs`; Program.cs down to ~170 lines |
| Serilog | Structured JSON in production; readable console in dev; bootstrap logger |
| Docker | Multi-stage build (node → sdk → aspnet); non-root user; named volumes; docker-compose |
| Security headers | CSP, X-Frame-Options, X-Content-Type-Options, Referrer-Policy, Permissions-Policy; HSTS in production |
| AuditService | Scoped, non-fatal writes; wired to all state-changing endpoints and ExportWorker |
| 404 catch-all | `NotFoundView.vue`; Vue Router catch-all route |
| Playwright E2E | `login.spec.ts`, `navigation.spec.ts`, `audit.spec.ts`; Vitest exclude configured |

---

## Phase 8 — UX hardening, compliance depth & gap recovery ✅

| Item | Notes |
|---|---|
| Preview count clarity | Header shows `50+` at cap; truncation note says "preview cap, not export total" |
| DeliveryNotes max-length | API rejects Notes > 2,000 chars; UI textarea has live counter |
| SettingsView range hint | Retention days shows "1–3,650 days"; validated server-side |
| Excel date columns | `BuildExcelBytes` auto-detects ISO dates, writes as Excel DateTime `yyyy-mm-dd` |
| Route guards | `source-schema`/`export-schema` redirect to `/connect?notice=needs-connection` |
| ERP pagination cap | `GET /api/erp/records` returns `{records, total}`, default cap 500 |
| GDPR denylist as runtime config | `GET`/`PATCH /api/gdpr-denied-fields`; stored in `AppSetting`; tag-pill editor |
| Audit log | `AuditLog` table; non-fatal writes; 8 endpoints wired; `GET /api/audit`; `AuditView.vue` |
| Skipped run status | `ExportRunStatus.Skipped`; `POST /api/exports/{seqNo}/skip`; gap detection treats it as resolved |

---

## Phase 7 — Requirements gap closure ✅

| Item | Notes |
|---|---|
| Zero-count abort | Scheduled worker + on-demand handler mark run `Failed` on 0 records |
| ISO 8601 date coercion | `date`/`timestamp`/`timestamptz` columns formatted `yyyy-MM-dd` |
| GDPR field denylist | Enforced at mapping-save (400 on violation) and stripped in query results |
| ICD Schema view | `IcdSchemaView.vue` at `/icd-schema` — read-only ICD reference |
| ERP Database CI browser | `ErpDatabaseView.vue` at `/erp-database` — BOM tree, scope filter, per-row detail |

---

## Phase 6 — Operational enhancements ✅

| Item | Notes |
|---|---|
| Health check | `GET /api/health` — ERP DB, log DB, staging writability; no auth |
| Stale pending indicator | `IsStale` on `ExportRunSummary`; UI callout when Pending > 24 h |
| Sequence gap detection | `GET /api/exports/{seqNo}` returns `SequenceGapWarning` |
| Delivery acknowledgement | `POST /api/exports/{seqNo}/deliver`; closes custody chain |
| Schema column persistence | `AppSetting` table; `PATCH /api/schema/columns` |
| Connection config backend | `GET`+`POST /api/connection`; Npgsql live schema introspection |

---

## Phase 5 — Tests ✅

56 .NET tests (unit + integration), 187 Vitest tests, all passing. Playwright E2E wired (requires
both servers running).

---

## Phase 4 — API & frontend ✅

| Item | Notes |
|---|---|
| ASP.NET Minimal API | `GET /api/exports`, `GET /api/exports/{seqNo}`, `POST /api/exports/{seqNo}/release` |
| Vue 3 UI scaffolding | Vite + Vue 3 + TypeScript + Tailwind; proxy to :5189 |
| Four-step workflow | Connect → Source Schema → Export Schema → Export |
| ConnectionView | Postgres host/port/db/user/password form; persisted to `localStorage` |
| SourceSchemaView | Expandable table/column browser; calls `/api/source-schema` |
| Export Schema column toggles | Checkboxes + format picker (xlsx/csv/json) |
| ExportView | Format picker, Run Export, preview table, run history |
| Multi-format export | `POST /api/pipeline/run?format=xlsx\|csv\|json` |
| ERP Database view | BOM tree; flat list with search + sort; per-row detail panel |

---

## Phase 3 — Infrastructure, I/O & orchestration ✅

| Item | Notes |
|---|---|
| ExcelPackager | `guid` as first column; ClosedXML |
| SQLite Export Log | `ExportRun` table with all required fields |
| ExportWorker | `BackgroundService` with `PeriodicTimer`; `Failed` status on exception |
| Data retention cleanup | Daily purge of staging files + Released/Failed rows; configurable `RetentionDays` |

---

## Phase 2 — Pipeline implementation ✅

| Item | Notes |
|---|---|
| ExportFilter | Blocks on missing GUID; missing serial number allowed |
| DataMinimizer | Removes personal-data fields at type level; preserves GUID |
| SchemaMapper | Throws `InvalidCorrelationKeyException` on empty GUID; maps all ICD columns |

---

## Phase 1 — Solution setup & domain contracts ✅

5 projects with strict dependency rules; domain models `ErpConfigurationItem`, `ExportItem`,
`MappedExportRecord`; 6 pipeline interfaces with XML documentation.

---

## Open points (future iterations)

Tracked in [Open Points](/planning/open-points.md) — stakeholder ownership, code impact, and
resolution workflow for each pending item (classification marking, `storagelocation` entitlement,
snapshot volume, return-channel timing, retention periods, allocation chart import).
