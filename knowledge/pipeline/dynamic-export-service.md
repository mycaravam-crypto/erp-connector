---
type: Pipeline Stage
title: DynamicExportService — the live export pipeline (2.0)
description: The actual, currently-executing query+build pipeline. Supersedes the fixed six-stage design described in erp-reader.md, export-filter.md, data-minimizer.md, schema-mapper.md, and packager.md.
resource: src/Connector.Infrastructure/DynamicExportService.cs
tags: [pipeline, dynamic-mapping, live, 2.0]
timestamp: 2026-08-19T00:00:00Z
---

# What changed

The original Technical Concept described a fixed six-stage pipeline — `IErpReader` →
`IExportFilter` → `IDataMinimizer` → `ISchemaMapper` → `IPackager` → `IExportSink` — built
against a hardcoded, single-table ERP shape. During development (Phases 6–12) the real
requirement turned out to be different: the source schema is not fixed, joins across
related tables are required, and the operator needs to configure column mapping at
runtime rather than at compile time. `DynamicExportService` was built to answer that need,
and by Phase 9 it had fully replaced the fixed pipeline for every live code path — the
fixed-pipeline types (`ErpConfigurationItem`, `ExportItem`, `MappedExportRecord`,
`ISchemaMapper`/`SchemaMapper`, `IDataMinimizer`/`DataMinimizer`,
`IExportFilter`/`ExportFilter`, `IPackager`/`ExcelPackager`, `IErpReader`/`DemoErpReader`)
were never wired into dependency injection and carried no production traffic. As of the
2.0 cleanup they have been deleted from the codebase; `erp-reader.md`, `export-filter.md`,
`data-minimizer.md`, `schema-mapper.md`, and `packager.md` remain only as a historical
record of the original design intent (in particular, *why* the GDPR-minimization and
correlation-key rules exist) — they no longer describe running code.

# The live pipeline

```
ExportMappingConfig (runtime-configurable: source table, columns, joins,
  optional nested-JSON groups — persisted as AppSetting JSON)
    ↓
DynamicExportService.BuildExportAsync
    ↓  UsesNestedJson(config, format)?
    ├─ yes → ExecuteNestedJsonQueryAsync → BuildNestedJsonBytes   (json_build_object/json_agg in SQL)
    └─ no  → ExecuteQueryAsync → BuildCsvBytes / BuildJsonBytes / BuildExcelBytes
    ↓
ExportPackage (bytes + ExportManifest) → IExportSink → staging folder
```

`BuildExportAsync` is the single decision point shared by all three callers that used to
each re-implement this branch separately:

- `POST /api/pipeline/run` (manual run, any format)
- `ExportWorker.RunExportAsync` (nightly scheduled run, format from `SchedulerConfigData.Format`)
- `GET /api/pipeline/preview` calls the same `UsesNestedJson` check directly (it returns
  structured data for display rather than file bytes, so it can't share `BuildExportAsync`
  itself, but it can no longer disagree with the other two about which shape a mapping
  produces — previously it always fell back to the flat shape for a nested-group mapping).

# What stayed the same

- **GDPR minimization**: still enforced — the mapping-save endpoint rejects a denylisted
  field, and `ExecuteQueryAsync`/`ExecuteNestedJsonQueryAsync` strip any denylisted field
  from the result as defence-in-depth. The mechanism changed (runtime-editable denylist
  in `AppSetting`, not a fixed type with the fields simply absent), which is an accepted,
  intentional deviation from the original "removed at the type level" design — see
  [GDPR Compliance](/processes/gdpr-compliance.md).
- **Correlation key / four-eyes / audit / retention**: unaffected — those live in
  `ExportRunEntity`, `AuditService`, and `ExportWorker`'s retention cleanup, none of which
  depended on the fixed-pipeline types.
- **ICD contract**: [Export Schema](/schema/export-schema.md) is still published as the
  read-only reference contract negotiated with the vendor (`GET /api/schema`,
  `IcdSchemaView.vue`). It no longer drives the query — the dynamic mapping's `TargetName`/
  `TargetKey` strings are admin-chosen and independent of `ExportSchema.Columns` — so it is
  documentation, not configuration.

# Related

- [Export Schema](/schema/export-schema.md) — reference contract, decoupled from this pipeline
- [Export Worker](/pipeline/export-worker.md) — scheduled caller of `BuildExportAsync`
- [GDPR Compliance](/processes/gdpr-compliance.md) — how the runtime denylist is enforced
