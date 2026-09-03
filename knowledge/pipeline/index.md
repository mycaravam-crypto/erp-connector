# Pipeline

How the export actually runs today. The export pipeline transforms ERP data into a packaged
export file (Excel/CSV/JSON) + manifest ready for the four-eyes release authority.

* [DynamicExportService](dynamic-export-service.md) - The currently-executing query+build pipeline: runtime-configurable mapping → Postgres query (flat or nested-JSON) → CSV/JSON/Excel bytes
* [ExportWorker](export-worker.md) - Daily background service that calls DynamicExportService at a scheduled UTC time and format
* [IExportSink](export-sink.md) - Writes the data file and manifest atomically to the staging path

# Planned (Phase 14, in progress)

* [Export Definitions 2.0](export-definitions-2.0.md) - Generalizes the one mapping above into N independently scheduled, arbitrarily-nested export definitions (`ExportNode` tree). Spec + live per-slice implementation status

For the original fixed six-stage pipeline this replaced (`IErpReader`/`IExportFilter`/
`IDataMinimizer`/`ISchemaMapper`/`IPackager`) — deleted from the codebase, kept only as design
rationale — see [legacy/](/legacy/).
