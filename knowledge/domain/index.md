# Domain Types

Core data types used in the export pipeline. Each record type represents a transformation
boundary: data progresses from raw ERP records → filtered → minimized → mapped → packaged.

# Record Types

* [ErpConfigurationItem](erp-configuration-item.md) - Raw CI as read from the ERP; may contain personal data
* [ExportItem](export-item.md) - GDPR-minimized CI; only exportable fields remain
* [MappedExportRecord](mapped-export-record.md) - ICD-mapped record ready for packaging
* [ExportManifest](export-manifest.md) - Integrity and sequence metadata accompanying each export file
* [ExportPackage](export-package.md) - Complete export artifact: data file bytes + manifest
