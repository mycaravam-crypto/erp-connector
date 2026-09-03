# Legacy

The original fixed six-stage pipeline (`IErpReader` → `IExportFilter` → `IDataMinimizer` →
`ISchemaMapper` → `IPackager` → `IExportSink`) against a hardcoded, single-table ERP shape.

**None of this is running code.** These types were never wired into dependency injection, carried
no production traffic, and were deleted from the codebase in the Phase 13 2.0 cleanup — see the
[changelog](/changelog.md) and [DynamicExportService](/pipeline/dynamic-export-service.md), which
replaced all of it. These pages exist only to preserve *why* certain rules exist (the GDPR field
exclusions, the correlation-key requirement, the string-not-numeric identifier rule) — each
still applies today, just enforced differently.

# Pipeline stages

* [IErpReader — Stage 1](erp-reader.md) - Read all maintainable CIs from the ERP (read-only, full snapshot)
* [IExportFilter — Stage 2](export-filter.md) - Scope filter: exclude CIs without a GUID (missing Coalesce key)
* [IDataMinimizer — Stage 3](data-minimizer.md) - GDPR minimization: strip TechnicianName and StorageLocation
* [ISchemaMapper — Stage 4](schema-mapper.md) - Map ExportItem fields to ICD export schema
* [IPackager — Stage 5](packager.md) - Serialize records to Excel and compute SHA-256 manifest

# Domain types

* [ErpConfigurationItem](erp-configuration-item.md) - Raw CI as read from the ERP; may contain personal data
* [ExportItem](export-item.md) - GDPR-minimized CI; only exportable fields remain
* [MappedExportRecord](mapped-export-record.md) - ICD-mapped record ready for packaging

`IExportSink` (Stage 6) is still live — see [pipeline/export-sink.md](/pipeline/export-sink.md).
