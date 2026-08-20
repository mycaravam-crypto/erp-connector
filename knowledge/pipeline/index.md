# Pipeline

The export pipeline transforms ERP data into a packaged export file (Excel/CSV/JSON) +
manifest ready for the four-eyes release authority.

# Live (2.0)

* [DynamicExportService](dynamic-export-service.md) - The actual, currently-executing query+build pipeline: runtime-configurable mapping → Postgres query (flat or nested-JSON) → CSV/JSON/Excel bytes
* [ExportWorker](export-worker.md) - Daily background service that calls DynamicExportService at a scheduled UTC time and format

# Superseded — original fixed six-stage design (kept for historical context only)

None of the interfaces below are registered in dependency injection or reachable from any
endpoint; the types have been deleted from the codebase. Each page explains what replaced
it. See [DynamicExportService](dynamic-export-service.md) for why the design changed.

* [IErpReader — Stage 1](erp-reader.md) - Read all maintainable CIs from the ERP (read-only, full snapshot)
* [IExportFilter — Stage 2](export-filter.md) - Scope filter: exclude CIs without a GUID (missing Coalesce key)
* [IDataMinimizer — Stage 3](data-minimizer.md) - GDPR minimization: strip TechnicianName and StorageLocation
* [ISchemaMapper — Stage 4](schema-mapper.md) - Map ExportItem fields to ICD export schema
* [IPackager — Stage 5](packager.md) - Serialize records to Excel and compute SHA-256 manifest

# Still live

* [IExportSink — Stage 6](export-sink.md) - Write data file and manifest atomically to the staging path
