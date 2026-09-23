---
type: Pipeline Stage
title: DynamicExportService — the live export pipeline
description: The query+build pipeline behind the legacy single-mapping export flow — runtime-configurable mapping against a non-fixed ERP schema, with cross-table joins.
resource: src/Connector.Infrastructure/DynamicExportService.cs
tags: [pipeline, dynamic-mapping, live]
timestamp: 2026-08-19T00:00:00Z
---

# The live pipeline

The export query and mapping are runtime-configurable, not a fixed shape against a hardcoded
single table: the source schema, cross-table joins, and column mapping are all driven by a
saved `ExportMappingConfig`. For the original fixed six-stage design this pipeline replaced
(`IErpReader`/`IExportFilter`/`IDataMinimizer`/`ISchemaMapper`/`IPackager`) — kept only as a
historical record of why the GDPR-minimization and correlation-key rules exist — see
[legacy/](/legacy/).

```
ExportMappingConfig (runtime-configurable: source table, columns, joins,
  optional nested-JSON groups — persisted as AppSetting JSON)
    ↓
DynamicExportService.BuildExportAsync
    ↓  UsesNestedJson(config, format)?
    ├─ yes → ExecuteNestedJsonQueryAsync → BuildNestedJsonBytes   (tree assembled in C#, one query per group)
    └─ no  → ExecuteQueryAsync → BuildCsvBytes / BuildJsonBytes / BuildExcelBytes
    ↓
ExportPackage (bytes + ExportManifest) → IExportSink → staging folder
```

`BuildExportAsync` is the single decision point shared by all three callers:

- `POST /api/pipeline/run` (manual run, any format)
- `ExportWorker.RunExportAsync` (nightly scheduled run, format from `SchedulerConfigData.Format`)
- `GET /api/pipeline/preview` calls the same `UsesNestedJson` check directly (it returns
  structured data for display rather than file bytes, so it can't share `BuildExportAsync`
  itself, but it agrees with the other two about which shape a mapping produces).

# Related mechanisms

- **GDPR minimization**: enforced by a runtime-editable denylist in `AppSetting` — the
  mapping-save endpoint rejects a denylisted field, and `ExecuteQueryAsync`/
  `ExecuteNestedJsonQueryAsync` strip any denylisted field from the result as defence-in-depth.
  See [GDPR Compliance](/operations/gdpr-compliance.md).
- **Correlation key / four-eyes / audit / retention**: live in `ExportRunEntity`, `AuditService`,
  and `ExportWorker`'s retention cleanup.
- **ICD contract**: [Export Schema](/schema/export-schema.md) is published as the read-only
  reference contract negotiated with the vendor (`GET /api/schema`, `IcdSchemaView.vue`). It
  doesn't drive the query — the dynamic mapping's `TargetName`/`TargetKey` strings are
  admin-chosen and independent of `ExportSchema.Columns` — so it is documentation, not
  configuration.

# Related

- [Export Schema](/schema/export-schema.md) — reference contract, decoupled from this pipeline
- [Export Worker](/pipeline/export-worker.md) — scheduled caller of `BuildExportAsync`
- [GDPR Compliance](/operations/gdpr-compliance.md) — how the runtime denylist is enforced
- [Dynamic Export](/dynamic-export/index.md) — the `ExportNode` tree, its own scheduler, and its
  run-history entity, extending this page rather than replacing it
