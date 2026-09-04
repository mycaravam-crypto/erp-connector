---
type: Infrastructure Domain Type
title: ExportDefinitionRunEntity
description: One row per execution of an ExportDefinition — manual, test, or scheduled — with Status/RecordCount/ErrorMessage and the ConfigVersion that ran.
resource: src/Connector.Infrastructure/ExportDefinitionEntity.cs
tags: [domain, infrastructure, dynamic-export, phase-14]
timestamp: 2026-09-03T00:00:00Z
---

`ExportDefinitionRunEntity` is the per-definition execution-history analogue of
[ExportRun](/domain/export-run.md) — deliberately a **separate** entity, not a reuse of it:
`ExportRun` models the legacy CI-to-vendor pipeline's sequence-number/four-eyes/staging-file
contract, none of which applies to a generic, independently-triggered export (see
[Export Definitions 2.0 §10](/pipeline/export-definitions-2.0.md#10-non-goals)).

# Schema

| Field                | Type    | Description                                                                 |
|----------------------|---------|-------------------------------------------------------------------------------|
| `Id`                 | int     | Auto-incremented primary key.                                                |
| `ExportDefinitionId` | int     | The definition this run belongs to.                                         |
| `ConfigVersion`      | int     | The definition's `ConfigVersion` *at the time this run executed* — traceable even after later edits. |
| `StartedAt`          | string  | ISO 8601 UTC timestamp, written before the query runs.                       |
| `FinishedAt`         | string? | ISO 8601 UTC timestamp; null while `Status = Running`.                       |
| `Status`             | string  | `Running` \| `Success` \| `Failed`.                                          |
| `RecordCount`        | int     | Records produced. 0 only ever appears transiently — see below.               |
| `ErrorMessage`       | string? | Populated whenever `Status = Failed`; never null on failure.                 |
| `TriggeredBy`        | string  | Username for a manual run/test, or the literal `"scheduler"`.                |
| `IsTestRun`          | bool    | True for a capped `POST .../test` run — excluded from normal execution-history read as "real" runs. |

# One code path, every trigger

`ExportDefinitionRunner.ExecuteAsync` is the *only* place a row is written — called identically by
`POST /api/export-definitions/{id}/run`, `POST .../test`, and
[the scheduler](scheduler.md). It:

1. Inserts the row up front with `Status = Running`, so a crash mid-query still leaves a row
   behind instead of silently disappearing.
2. Runs the query + format-writer engine (`DynamicExportService.BuildExportNodeAsync`).
3. **A zero-record result is `Status = Failed`** with `ErrorMessage = "Export returned 0
   records."` — never a silent, technically-successful empty export. This mirrors
   `ExportWorker`'s existing zero-record handling for the legacy pipeline.
4. Any exception (bad `RootNode` JSON, missing connection config, query error) is caught and
   also finalizes the row as `Failed` with the exception's message, rather than leaving it stuck
   at `Running` forever.

`preview` (`POST .../preview`) is the one exception: it deliberately writes **no** row at all — it's
the lighter, untracked, capped call the UI's tree builder uses while a definition is still being
edited, per [Export Definitions 2.0 §6](/pipeline/export-definitions-2.0.md#6-scheduling).

# Reading history

`GET /api/export-definitions/{id}/runs` returns rows newest-first, capped at 200 — see
[Export Definition API](/api/export-definition-api.md#run-history). The frontend's
`ExportDefinitionRunsTable.vue` renders `TriggeredBy`/`IsTestRun` alongside status, distinguishing
it from the legacy `ExportRunsTable.vue` (which renders SHA-256/staging-file columns that don't
exist on this entity).

# Related

- [Scheduler](scheduler.md) — the other caller of `ExportDefinitionRunner.ExecuteAsync`
- [ExportRun](/domain/export-run.md) — the legacy, unrelated per-pipeline-run entity
- [Export Definition API](/api/export-definition-api.md) — the HTTP surface over this entity
- [ExportNode Tree](export-node.md) — what a run actually queries
