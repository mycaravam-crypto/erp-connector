# Connector — Changelog

Last updated: 2026-08-06

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
