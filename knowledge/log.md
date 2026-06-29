# Connector Knowledge Bundle — Update Log

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
