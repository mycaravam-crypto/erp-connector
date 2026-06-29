# Connector Knowledge Bundle — Update Log

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
