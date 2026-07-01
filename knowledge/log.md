# Connector Knowledge Bundle — Update Log

## 2026-07-01
* **Phase 7 enhancements + Phase 8 Skipped run status** shipped (56 .NET + 187 Vitest tests green):
  * **Preview count clarity**: header shows `50+` when preview hits cap; truncation note reworded to make clear it is a preview cap, not the export total.
  * **DeliveryNotes max-length**: `POST /api/exports/{seqNo}/deliver` rejects `Notes` > 2,000 chars (400). `ExportDetail.vue` textarea has `maxlength="2000"` and a live character counter.
  * **SettingsView range hint**: retention days field shows "1–3,650 days" hint text.
  * **Excel date formatting**: `BuildExcelBytes` auto-detects ISO-date values (`yyyy-MM-dd`), writes as real Excel DateTime with `yyyy-mm-dd` number format; non-date columns force text (format 49).
  * **Route guards**: Vue Router `beforeEach` now async; `source-schema` and `export-schema` routes redirect to `/connect?notice=needs-connection` if no connection is configured. `ConnectionView` shows an amber notice and calls `invalidateConnectionCache()` after successful save.
  * **ERP pagination cap**: `GET /api/erp/records` returns `{ records, total }` (`ErpRecordsResult`); applies a default cap of 500 (or caller-supplied `?limit=N`); `ErpDatabaseView` shows a "Showing N of M CIs" banner when truncated.
  * **GDPR denylist as runtime AppSetting**: `GET`/`PATCH /api/gdpr-denied-fields`; `DynamicExportService.GetDeniedFieldsAsync` reads from DB with fallback to hardcoded defaults; SettingsView shows tag-pill editor with Save button.
  * **Audit log**: `AuditLog` table (Id, Timestamp, Username, Action, Detail); `LogAuditAsync` helper (non-fatal try/catch); wired into 8 state-changing endpoints (login, release, deliver, skip, export mapping, preset save/delete, scheduler, GDPR); `GET /api/audit?limit=N`; `AuditView.vue` at `/audit`.
  * **Skipped run status**: `ExportRunStatus.Skipped`; `POST /api/exports/{seqNo}/skip` (valid for Pending/Failed; optional reason logged to audit); gap detection updated to treat Skipped as resolved; ExportDetail shows skip form for Pending/Failed runs; `StatusBadge` renders Skipped in neutral grey.
* **Updated**: ROADMAP.md Phase 7 completed, Phase 8 added.

## 2026-06-30
* **Connection config wired to backend** (42 .NET + 55 Vitest tests green):
  * `GET /api/connection` — returns stored ERP connection info (host/port/db/user; no password), 404 if none.
  * `POST /api/connection` — accepts full credentials, opens Npgsql connection, introspects `information_schema.columns`, persists config in `AppSetting` key `erp_connection`, returns live `SourceSchemaDto`. Returns 400 on connection failure.
  * `GET /api/source-schema` — now async; if `erp_connection` is persisted, introspects real Postgres and returns live schema; falls back to demo schema when absent or unreachable.
  * `ConnectionView.vue` — loads from `GET /api/connection` on mount; "Test Connection" calls `POST /api/connection`; shows green "Connected" banner when a live connection is stored; password stays server-side only.
  * `connection.ts` — added `ErpConnectionInfo`, `ConnectionConfig` interfaces; `getConnection()`, `saveConnection()` functions; `getSourceSchema()` retained.
  * New test file `connection-api.test.ts` — 7 tests covering `getConnection`, `saveConnection`, `getSourceSchema`.
  * Added Npgsql 10.0.3 package reference.
* **Updated**: ROADMAP.md Phase 6 → Iteration 2 connection item completed.

## 2026-06-29
* **Phase 6 — Operational Enhancements** shipped and committed (42 .NET + 48 Vitest tests green):
  * 6.1 `GET /api/health` — health check (ERP DB, log DB, staging writability); no auth.
  * 6.2 `IsStale` flag on `ExportRunSummary`; ExportView shows overdue callout when Pending > 24 h.
  * 6.3 `SequenceGapWarning` on `GET /api/exports/{seqNo}`; ExportDetail shows banner before release form.
  * 6.4 Delivery acknowledgement: `POST /api/exports/{seqNo}/deliver`; four new nullable columns on `ExportRunEntity`; additive SQL migration on startup; ExportDetail delivery form + done indicator.
  * 6.5 Schema column persistence: `AppSetting` table; `PATCH /api/schema/columns`; `GET /api/schema` reads persisted active flags; SchemaView saves on every toggle.
* **Updated**: [Four-Eyes Release](/processes/four-eyes-release.md) — added delivery acknowledgement step.
* **Updated**: [Operational Monitoring](/processes/operational-monitoring.md) — new doc covering health check, stale indicator, gap detection.

## 2026-06-28 (update 2)
* **Creation**: Added [ExportRun domain type](/domain/export-run.md) — lifecycle record created by every pipeline execution.
* **Creation**: Added [Authentication process](/processes/authentication.md) — JWT Bearer auth, BCrypt user store, dev/prod config.
* **Creation**: Added [Data Retention process](/processes/data-retention.md) — daily purge of staging files and completed ExportRun records.
* **Creation**: Added [On-Demand Run process](/processes/on-demand-run.md) — Run Now trigger and read-only Preview endpoints.
* **Creation**: Added [Open Points process](/processes/open-points.md) — tracking all 8 outstanding Technical Concept decisions.
* **Update**: Extended [domain/index.md](/domain/index.md) and [processes/index.md](/processes/index.md) with the new entries.

## 2026-06-28
* **Initialization**: Created foundational OKF v0.1 knowledge bundle for the ERP-to-ServiceNow connector.
* **Creation**: Documented all domain types ([domain/](/domain/index.md)).
* **Creation**: Documented the export schema contract ([schema/export-schema.md](/schema/export-schema.md)).
* **Creation**: Documented all six pipeline stages and the ExportWorker orchestrator ([pipeline/](/pipeline/index.md)).
* **Creation**: Documented business processes: four-eyes release and GDPR compliance ([processes/](/processes/index.md)).
