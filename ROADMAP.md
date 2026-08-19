# Connector — Changelog

Last updated: 2026-08-19

Planned next: [`REQUIREMENTS-2.0.md`](REQUIREMENTS-2.0.md) — generic, tree-based multi-export
definitions (Phase 14, not yet started). It was drafted in parallel with Phase 13 below and has a
correction note at its top reconciling the two.

---

## Phase 13 — 2.0: simplification after the exploration phase ✅

Phases 1–12 were partly a discovery process: figuring out what the connector actually
needed to do (fixed schema vs. runtime-configurable mapping, single-table vs. joins,
flat vs. nested export) while a working system was already in production. That produced
real accidental complexity alongside the real requirements. This phase cuts it back down
now that the shape of the requirement is known — without touching the compliance-critical
paths (four-eyes release, GDPR, audit log, sequence integrity), which were requirements
from day one, not discovery artifacts.

| Item | Notes |
|---|---|
| Removed the dead fixed pipeline | `Connector.Export` project, `ErpConfigurationItem`/`ExportItem`/`MappedExportRecord`, `ISchemaMapper`/`IDataMinimizer`/`IExportFilter`/`IPackager`/`IErpReader` and their implementations — never registered in DI, carried no live traffic since the dynamic mapping (Phase 9–10) fully replaced them. See [`knowledge/pipeline/dynamic-export-service.md`](knowledge/pipeline/dynamic-export-service.md) |
| Removed the demo-ERP browsing feature | `ErpDatabaseView`, `BomTree`/`BomTreeRow`/`CiDetailPanel`, `GET /api/erp/records`, the seeded SQLite `DemoErpDbContext` — an exploration/demo view over fake data with no requirement calling for it in production; superseded by `SourceSchemaView` + the Postgres `testdb` fixture |
| Removed inert schema-mapping endpoints | `PATCH /api/schema/columns`/`/api/schema/mappings` persisted an "active columns"/rename state nothing in the frontend called or read; `GET /api/schema` is now a pure static read of the ICD reference contract |
| ICD contract decoupled explicitly | `IcdSchemaView`/`GET /api/schema` documented as read-only reference, independent of the live dynamic mapping — resolves the earlier "two competing schema models" ambiguity in favor of the dynamic mapping as the real, evolving design |
| Unified export execution path | New `DynamicExportService.BuildExportAsync` + `UsesNestedJson` are the single decision point shared by Run Now, the scheduled `ExportWorker`, and Preview — closes the Phase 12 gap where nested-JSON mappings only worked from Run Now. Scheduler settings gained a persisted `Format` field so the nightly export can also produce nested JSON |
| SchemaView format-toggle bug fixed | The Step 3 "which JSON options to show" toggle silently shared a `localStorage` key with the Step 4 "which format to actually export" picker — selecting JSON to peek at nested-group config would silently change what Run Now exported. Decoupled; the Step 3 toggle now also auto-selects when a loaded mapping already has nested groups |
| API module boundaries cleaned up | `api/erp.ts` split into `api/mapping.ts` (dynamic mapping) and `api/icdSchema.ts` (ICD reference) now that the ERP-browsing half is gone; GDPR denylist endpoints moved from `api/audit.ts` to `api/scheduler.ts` (both are Settings-page concerns) |
| Docs reconciled with the running code | `knowledge/pipeline/*` and `knowledge/domain/*` pages describing the deleted fixed pipeline marked superseded, pointing to the new [`dynamic-export-service.md`](knowledge/pipeline/dynamic-export-service.md) |

---

## Phase 12 — Nested JSON export ✅

| Item | Notes |
|---|---|
| Nested JSON structure | `ExportMappingNestedGroup`/`ExportMappingNestedField` — JSON-export-only, additive to `ExportMappingConfig`; `object` (N:1 lookup) or `array` (1:N) kind, nestable to arbitrary depth via `Children` |
| SQL generation | `DynamicExportService.ExecuteNestedJsonQueryAsync`/`BuildNestedGroupExpr` build one query using native `json_build_object`/`json_agg`; a zero-match array COALESCEs to `[]`, not `null` |
| JSON envelope wrapper | `ExportJsonWrapperConfig` — optional root key, items key, and a metadata block with dynamic-timestamp fields; `BuildNestedJsonBytes` reproduces the legacy flat envelope when unset, so existing saved mappings are unaffected |
| `NestedGroupEditor.vue` | Recursive, self-referencing component in `SchemaView`'s "Nested JSON Structure" section (shown for JSON format only); per-group field picker, add/remove children |
| Save-time validation | `ValidateNestedGroups` checks depth cap (16), required fields, identifier safety, GDPR denylist, and duplicate export keys at every depth. It does **not** check that `JoinKey`/`SourceJoinKey` exist in the schema or are type-compatible — a bad pairing still only surfaces as a raw Postgres error (e.g. `42883: operator does not exist`) at export time, not a validation message at save time |
| Wired into Run Now only | `POST /api/pipeline/run?format=json` branches to the nested path when `NestedGroups`/`JsonWrapper` are set. **Preview** and the nightly `ExportWorker` (Excel-only) still use the flat query path — Preview output will not reflect a nested-group mapping |
| Local Postgres test fixture | `docker-compose --profile test up -d testdb` (`testdb/init.sql`) seeds a schema including `manufacturer`/`manufacturer_address` (exercises array-of-objects nesting and the empty-array case) — backs both `connection.spec.ts`'s live-connection e2e test and the integration tests below |
| Tests | `DynamicExportServiceNestedJsonPostgresTests.cs` — 7 real-Postgres integration tests (object/array kinds, 2-hop nesting, GDPR stripping, empty-array coalesce, identifier escaping) run against the `testdb` fixture |

---

## Phase 11 — Legacy mapping data regression fix ✅

| Item | Notes |
|---|---|
| Fixed crash on pre-Phase-10 mapping data | `export_mapping`/`export_presets` saved before relations gained `Fields`/`Delimiter`/`FlattenStrategy` deserialized those properties as `null` (System.Text.Json leaves missing properties `null`, not empty), crashing `SchemaView.vue`'s load path (misreported to users as "Could not reach the API") and, latently, `DynamicExportService.ExecuteQueryAsync`/`GetColumnNames` for Preview, Run Now, and the scheduled `ExportWorker` |
| `ExportMappingJson` normalization helper | New `Connector.Core.DynamicExport.ExportMappingJson.DeserializeConfig`/`DeserializePresets` backfill `Fields → []`, `Delimiter → ", "`, `FlattenStrategy → "string_join"` on read; all 6 backend read sites (`ExportMappingEndpoints`, `PipelineEndpoints` ×2, `ExportWorker`) route through it instead of raw `JsonSerializer.Deserialize` |
| Defense-in-depth guards | `DynamicExportService.GetColumnNames`/`ExecuteQueryAsync` null-coalesce `r.Fields`/`r.Delimiter` at point of use; `SchemaView.vue`'s `cloneRelation` and `ExportView.vue`'s `enabledRelationFields` guard `r.fields` the same way |

---

## Phase 10 — Export mapping usability ✅

| Item | Notes |
|---|---|
| Foreign-key auto-detection | `IntrospectSchemaAsync` detects FK constraints via `information_schema`; `SourceColumnDto` carries `ForeignKeyTable`/`ForeignKeyColumn`; `SchemaView.vue` shows a "Suggested Relations" list with one-click add, prefilling the join |
| Multi-field relations | `ExportMappingRelation.Fields` replaces the old single source/target field pair; each 1:N join now pulls any number of independently renamed columns from the related table, with a Select All / Deselect All picker mirroring the primary column table |
| GDPR denylist gap closed | Save-time denylist validation now also scans relation fields, not just primary columns |

---

## Phase 9 — Production hardening ✅

| Item | Notes |
|---|---|
| EF Core migrations | Replaced startup DDL; `MigrateAsync()` + bootstrap for pre-migration databases |
| Program.cs split | 9 endpoint modules; `Dtos.cs`; Program.cs reduced to ~170-line startup file |
| Serilog | Structured JSON in production; readable console in dev; bootstrap logger |
| Docker | Multi-stage build (node → sdk → aspnet); non-root user; named volumes; docker-compose |
| Security headers | CSP, X-Frame-Options, X-Content-Type-Options, Referrer-Policy, Permissions-Policy; HSTS in production |
| AuditService | Scoped service; non-fatal writes; wired to all state-changing endpoints and ExportWorker |
| 404 catch-all | `NotFoundView.vue`; Vue Router catch-all route |
| Playwright E2E | `login.spec.ts`, `navigation.spec.ts`, `audit.spec.ts`; Vitest exclude configured |

---

## Phase 8 — UX hardening, compliance depth & gap recovery ✅

| Item | Notes |
|---|---|
| Preview count clarity | Header shows `50+` when at cap; truncation note clearly says "preview cap, not export total" |
| DeliveryNotes max-length | API rejects Notes > 2,000 chars (400); UI textarea has `maxlength` + live character counter |
| SettingsView range hint | Retention days field shows "1–3,650 days" hint; validates 1–3,650 server-side |
| Excel date columns | `BuildExcelBytes` auto-detects ISO dates, writes as Excel DateTime with `yyyy-mm-dd` format |
| Route guards | `source-schema` and `export-schema` routes redirect to `/connect?notice=needs-connection` |
| ERP pagination cap | `GET /api/erp/records` returns `{records, total}`; default cap 500; UI shows "Showing N of M" |
| GDPR denylist as runtime config | `GET`/`PATCH /api/gdpr-denied-fields`; stored in `AppSetting`; SettingsView tag-pill editor |
| Audit log | `AuditLog` table; non-fatal writes; wired to 8 endpoints; `GET /api/audit`; `AuditView.vue` |
| Skipped run status | `ExportRunStatus.Skipped`; `POST /api/exports/{seqNo}/skip`; gap detection treats Skipped as resolved |

---

## Phase 7 — Requirements gap closure ✅

| Item | Notes |
|---|---|
| Zero-count abort | Scheduled worker + on-demand handler mark run `Failed` when query returns 0 records |
| ISO 8601 date coercion | `DynamicExportService` formats `date`/`timestamp`/`timestamptz` columns as `yyyy-MM-dd` |
| GDPR field denylist | Enforced at mapping-save (400 on violation) and stripped in query results |
| ICD Schema view | `IcdSchemaView.vue` at `/icd-schema` — read-only ICD column contract reference |
| ERP Database CI browser | `ErpDatabaseView.vue` at `/erp-database` — BOM tree, scope filter, per-row detail panel |

---

## Phase 6 — Operational enhancements ✅

| Item | Notes |
|---|---|
| Health check | `GET /api/health` — ERP DB, log DB, staging writability; no auth |
| Stale pending indicator | `IsStale` on `ExportRunSummary`; UI callout when Pending > 24 h |
| Sequence gap detection | `GET /api/exports/{seqNo}` returns `SequenceGapWarning`; ExportDetail banner |
| Delivery acknowledgement | `POST /api/exports/{seqNo}/deliver`; closes custody chain |
| Schema column persistence | `AppSetting` table; `PATCH /api/schema/columns`; column toggles saved server-side |
| Connection config backend | `GET`+`POST /api/connection`; Npgsql live schema introspection |

---

## Phase 5 — Tests ✅

- 56 .NET tests (unit + integration) — all passing
- 187 Vitest tests (Vue 3 component and API wrapper tests) — all passing
- Playwright E2E infrastructure wired (requires both servers running)

---

## Phase 4 — API & frontend ✅

| Item | Notes |
|---|---|
| ASP.NET Minimal API | `GET /api/exports`, `GET /api/exports/{seqNo}`, `POST /api/exports/{seqNo}/release` |
| Vue 3 UI scaffolding | Vite + Vue 3 + TypeScript + Tailwind; proxy to :5189 |
| Four-step workflow | Connect → Source Schema → Export Schema → Export |
| ConnectionView | Form for Postgres host/port/db/user/password; persisted to `localStorage` |
| SourceSchemaView | Expandable table/column browser; calls `/api/source-schema` |
| Export Schema column toggles | Checkboxes enable/disable columns; format picker (xlsx/csv/json) |
| ExportView | Format picker + Run Export button + preview table + run history |
| Multi-format export | `POST /api/pipeline/run?format=xlsx\|csv\|json` |
| ERP Database view | BOM tree; flat list with search + sort; per-row detail panel |

---

## Phase 3 — Infrastructure, I/O & orchestration ✅

| Item | Notes |
|---|---|
| ExcelPackager | `guid` written as first column; ClosedXML |
| SQLite Export Log | `ExportRun` table with all required fields |
| ExportWorker | `BackgroundService` with `PeriodicTimer`; `Failed` status on exception |
| Data retention cleanup | Daily purge of staging files + Released/Failed log rows; configurable `RetentionDays` |

---

## Phase 2 — Pipeline implementation ✅

| Item | Notes |
|---|---|
| ExportFilter | Blocks on missing GUID; missing serial number allowed |
| DataMinimizer | Removes personal-data fields at type level; preserves GUID |
| SchemaMapper | Throws `InvalidCorrelationKeyException` on empty GUID; maps all ICD columns |

---

## Phase 1 — Solution setup & domain contracts ✅

| Item | Notes |
|---|---|
| Project scaffolding | 5 projects with strict dependency rules |
| Domain models | `ErpConfigurationItem`, `ExportItem`, `MappedExportRecord` |
| Pipeline interfaces | 6 interfaces with XML documentation |

---

## Open points (future iterations)

| Open Point | When it unblocks | Code impact |
|---|---|---|
| Classification marking | Legal decision | Release API may need a marking field |
| `storagelocation` entitlement | Data owner + legal | `DataMinimizer` and export schema update if confirmed in scope |
| Snapshot volume | ERP data steward | Pagination in query if > ~500 k CIs |
| Return-channel timing | Vendor + sponsor | Iteration 2 scope and schedule |
| Retention periods | Legal + DPO | `RetentionDays` config value (default 30 — adjust when decided) |
| Allocation chart import | ERP + vendor | Maintenance plan predicate enforcement in mapping validation |
