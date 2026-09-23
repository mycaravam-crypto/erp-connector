# Pipeline

How the export actually runs today. The export pipeline transforms ERP data into a packaged
export file (Excel/CSV/JSON) + manifest ready for the four-eyes release authority.

* [DynamicExportService](dynamic-export-service.md) - The currently-executing query+build pipeline: runtime-configurable mapping → Postgres query (flat or nested-JSON) → CSV/JSON/Excel bytes
* [ExportWorker](export-worker.md) - Daily background service that calls DynamicExportService at a scheduled UTC time and format
* [IExportSink](export-sink.md) - Writes the data file and manifest atomically to the staging path

# Generic export definitions

* [Export Definitions](export-definitions-2.0.md) - Generalizes the one mapping above into N independently scheduled, arbitrarily-nested export definitions (`ExportNode` tree)
* [dynamic-export/](/dynamic-export/) - How it actually runs: the `ExportNode` tree, its own scheduler, and its run-history entity

For the original fixed six-stage pipeline this replaced (`IErpReader`/`IExportFilter`/
`IDataMinimizer`/`ISchemaMapper`/`IPackager`) — deleted from the codebase, kept only as design
rationale — see [legacy/](/legacy/).

# Inbound JSON import

* [Import Definitions](import-definitions.md) - The reverse leg: vendor-supplied JSON written back
  into the live ERP under the same air-gap and four-eyes controls as the export path (`ImportNode`
  tree, mirroring `ExportNode`). Resolves [Open Point #6](/planning/open-points.md)
* [dynamic-import/](/dynamic-import/) - How it actually runs: the `ImportNode` tree, `ImportWorker`
  (the inbound folder watcher), and the four-eyes commit path

# Import mapping presets from export provenance

* [Import Mapping Presets from Export Provenance](import-mapping-presets.md) - Tag a JSON export
  and its paired import with a shared, versioned `IntegrationKey`, and if a vendor's reply
  round-trips it, offer to create a new `ImportDefinition`'s root-matching (and, best-effort,
  shared field names) from the paired export — a "Create from export" suggestion in the New
  Import Definition flow. Inert for any given exchange until the vendor's ICD is told to echo the
  pair back
