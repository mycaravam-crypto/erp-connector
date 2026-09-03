# Dynamic Export (Phase 14 — Export Definitions 2.0)

How a saved, named [ExportDefinition](/api/export-definition-api.md) actually runs: the tree
shape it's configured from, how a schedule fires it automatically, and how each run is recorded.
This extends — does not replace — [DynamicExportService](/pipeline/dynamic-export-service.md),
which still owns the legacy single-mapping pipeline (`/export-schema`, `POST
/api/pipeline/run`) exactly as before; see [Export Definitions 2.0](/pipeline/export-definitions-2.0.md)
§11 decision #2 for why that flow was deliberately kept, not superseded.

* [ExportNode Tree](export-node.md) — the recursive data shape (`scalar-field`/`object`/`array`)
  every definition is built from, and how a `FieldMapping` transforms one scalar value
* [Scheduler](scheduler.md) — `ExportDefinitionWorker`, the cron matcher behind
  `ExportDefinition.Schedule`, and how a scheduled run differs (barely) from a manual one
* [Run History](run-history.md) — `ExportDefinitionRunEntity`: one row per run, `TriggeredBy`,
  and why a zero-record result is `Failed`, never a silent empty success

# Related

- [Export Definitions 2.0](/pipeline/export-definitions-2.0.md) — the full spec and per-slice
  implementation status this bundle documents the shipped result of
- [Export Definition API](/api/export-definition-api.md) — CRUD/run/test/preview HTTP surface
- [DynamicExportService](/pipeline/dynamic-export-service.md) — the legacy single-mapping
  pipeline this generalizes, and the query/format-writer engine both paths share
