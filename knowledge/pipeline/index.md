# Pipeline

How the export actually runs today. The export pipeline transforms ERP data into a packaged
export file (Excel/CSV/JSON) + manifest ready for the four-eyes release authority.

* [DynamicExportService](dynamic-export-service.md) - The currently-executing query+build pipeline: runtime-configurable mapping → Postgres query (flat or nested-JSON) → CSV/JSON/Excel bytes
* [ExportWorker](export-worker.md) - Daily background service that calls DynamicExportService at a scheduled UTC time and format
* [IExportSink](export-sink.md) - Writes the data file and manifest atomically to the staging path

# Phase 14 — generic export definitions ✅

* [Export Definitions 2.0](export-definitions-2.0.md) - Generalizes the one mapping above into N independently scheduled, arbitrarily-nested export definitions (`ExportNode` tree). Full spec + final per-slice implementation status
* [dynamic-export/](/dynamic-export/) - How it actually runs: the `ExportNode` tree, its own scheduler, and its run-history entity

For the original fixed six-stage pipeline this replaced (`IErpReader`/`IExportFilter`/
`IDataMinimizer`/`ISchemaMapper`/`IPackager`) — deleted from the codebase, kept only as design
rationale — see [legacy/](/legacy/).

# Phase 17 — inbound JSON import ✅

* [Import Definitions](import-definitions.md) - Spec + final per-slice implementation status for
  the reverse leg: vendor-supplied JSON written back into the live ERP under the same air-gap and
  four-eyes controls as the export path (`ImportNode` tree, mirroring `ExportNode`). Resolves
  [Open Point #6](/planning/open-points.md)
* [dynamic-import/](/dynamic-import/) - How it actually runs: the `ImportNode` tree, `ImportWorker`
  (the inbound folder watcher), and the four-eyes commit path

# Proposed — not started

* [Import Mapping Presets from Export Provenance](import-mapping-presets.md) - Design-only
  proposal: tag a JSON export and its paired import with a shared, versioned `IntegrationKey`, and
  if a vendor's reply round-trips it, offer to create a new `ImportDefinition`'s root-matching
  (and, best-effort, shared field names) from the paired export. No slice started.
