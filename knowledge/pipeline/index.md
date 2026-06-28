# Pipeline

The export pipeline transforms raw ERP data into a packaged Excel file + manifest ready
for the four-eyes release authority. All stages are interface-driven; implementations are
registered in `Connector.Api/Program.cs`.

# Orchestration

* [ExportWorker](export-worker.md) - Daily background service that runs the full pipeline at a scheduled UTC time

# Stages (in execution order)

* [IErpReader — Stage 1](erp-reader.md) - Read all maintainable CIs from the ERP (read-only, full snapshot)
* [IExportFilter — Stage 2](export-filter.md) - Scope filter: exclude CIs without a GUID (missing Coalesce key)
* [IDataMinimizer — Stage 3](data-minimizer.md) - GDPR minimization: strip TechnicianName and StorageLocation
* [ISchemaMapper — Stage 4](schema-mapper.md) - Map ExportItem fields to ICD export schema
* [IPackager — Stage 5](packager.md) - Serialize records to Excel and compute SHA-256 manifest
* [IExportSink — Stage 6](export-sink.md) - Write data file and manifest atomically to the staging path
