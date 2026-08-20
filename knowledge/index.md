---
okf_version: "0.1"
---

# ERP-to-ServiceNow Connector — Knowledge Bundle

This bundle documents the domain concepts, pipeline stages, schema contracts,
and business processes of the ERP-to-ServiceNow Configuration Item (CI) Connector.

The connector reads maintenance-relevant CIs from the ERP (read-only) via a
runtime-configurable mapping (source table, columns, joins — no hardcoded schema) and
produces a daily export (Excel/CSV/JSON) + SHA-256 manifest for four-eyes release to the
vendor gateway. The connector never crosses the air gap — its output ends at the staging
folder.

> **2.0 note:** the original Technical Concept described a fixed six-stage pipeline
> against a hardcoded ERP shape. That design was superseded during development by the
> runtime-configurable [DynamicExportService](/pipeline/dynamic-export-service.md) — the
> fixed-pipeline docs under `domain/` and `pipeline/` are kept as historical record but no
> longer describe running code; each is marked accordingly.

# Domain

* [Domain Types](domain/) - Core data types that flow through the export pipeline (ErpConfigurationItem → ExportItem → MappedExportRecord → ExportPackage; ExportManifest; ExportRun)
* [Schema](schema/) - Export schema definition and ICD contract
* [Pipeline](pipeline/) - Pipeline stages, services, and orchestration
* [Processes](processes/) - Business processes: four-eyes release, GDPR compliance, authentication, data retention, on-demand run, open points, code health backlog
* [Changelog](changelog.md) - Phase-by-phase record of what shipped, plus current in-progress status
* [Update Log](log.md) - Dated engineering session journal (root causes, bugs found, verification detail)
