# Business Processes

Operational and compliance processes governing the connector.

# Processes

* [Four-Eyes Release](four-eyes-release.md) - Manual dual-approval before an export file is transferred to the vendor gateway; includes delivery acknowledgement
* [GDPR Compliance](gdpr-compliance.md) - Data minimization policy and personal data handling rules
* [Authentication](authentication.md) - JWT Bearer auth protecting all API routes; BCrypt user store with dev seed and production config
* [Data Retention](data-retention.md) - Daily purge of staging files and completed ExportRun records past the retention window
* [On-Demand Run](on-demand-run.md) - API-triggered full pipeline run and read-only preview
* [Open Points](open-points.md) - Tracked outstanding decisions from the Technical Concept that will drive future code changes
* [Operational Monitoring](operational-monitoring.md) - Health check, stale-pending indicator, and sequence gap detection
* [Code Health Backlog](code-health-backlog.md) - Open frontend template-complexity items and ground rules for tackling them — not CI-gated
