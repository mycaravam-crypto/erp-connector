# Domain Types

Live data types used in the export pipeline today.

* [ExportManifest](export-manifest.md) - Integrity and sequence metadata accompanying each export file
* [ExportPackage](export-package.md) - Complete export artifact: data file bytes + manifest
* [ExportRun](export-run.md) - Persisted pipeline execution record with lifecycle status and release metadata

Data flows through [DynamicExportService](/pipeline/dynamic-export-service.md) as untyped SQL
result sets / JSON, not fixed record types. For the original fixed transformation-boundary types
(`ErpConfigurationItem`, `ExportItem`, `MappedExportRecord`) — deleted from the codebase, kept
only as design rationale — see [legacy/](/legacy/).
