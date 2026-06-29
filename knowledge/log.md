# Connector Knowledge Bundle — Update Log

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

## 2026-06-28
* **Initialization**: Created foundational OKF v0.1 knowledge bundle for the ERP-to-ServiceNow connector.
* **Creation**: Documented all domain types ([domain/](/domain/index.md)).
* **Creation**: Documented the export schema contract ([schema/export-schema.md](/schema/export-schema.md)).
* **Creation**: Documented all six pipeline stages and the ExportWorker orchestrator ([pipeline/](/pipeline/index.md)).
* **Creation**: Documented business processes: four-eyes release and GDPR compliance ([processes/](/processes/index.md)).
