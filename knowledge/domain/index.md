# Domain Types

Core data types used in the export pipeline.

# Live

* [ExportManifest](export-manifest.md) - Integrity and sequence metadata accompanying each export file
* [ExportPackage](export-package.md) - Complete export artifact: data file bytes + manifest
* [ExportRun](export-run.md) - Persisted pipeline execution record with lifecycle status and release metadata

# Superseded — original fixed transformation-boundary types (kept for historical context only)

Data now flows through [DynamicExportService](/pipeline/dynamic-export-service.md) as
untyped SQL result sets / JSON, not these fixed record types — none of them are reachable
from any live code path.

* [ErpConfigurationItem](erp-configuration-item.md) - Raw CI as read from the ERP; may contain personal data
* [ExportItem](export-item.md) - GDPR-minimized CI; only exportable fields remain
* [MappedExportRecord](mapped-export-record.md) - ICD-mapped record ready for packaging
