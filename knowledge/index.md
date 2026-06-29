---
okf_version: "0.1"
---

# ERP-to-ServiceNow Connector — Knowledge Bundle

This bundle documents the domain concepts, pipeline stages, schema contracts,
and business processes of the ERP-to-ServiceNow Configuration Item (CI) Connector.

The connector reads maintenance-relevant CIs from the ERP (read-only), runs them
through a 5-step data pipeline, and produces a daily Excel export + SHA-256 manifest
for four-eyes release to the vendor gateway. The connector never crosses the air gap —
its output ends at the staging folder.

# Domain

* [Domain Types](domain/) - Core data types that flow through the export pipeline (ErpConfigurationItem → ExportItem → MappedExportRecord → ExportPackage; ExportManifest; ExportRun)
* [Schema](schema/) - Export schema definition and ICD contract
* [Pipeline](pipeline/) - Pipeline stages, services, and orchestration
* [Processes](processes/) - Business processes: four-eyes release, GDPR compliance, authentication, data retention, on-demand run, open points
