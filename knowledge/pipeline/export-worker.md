---
type: Background Service
title: ExportWorker
description: Daily background service that orchestrates the full 5-stage export pipeline at a scheduled UTC time.
resource: src/Connector.Infrastructure/ExportWorker.cs
tags: [pipeline, orchestration, scheduling, background-service]
timestamp: 2026-06-28T00:00:00Z
---

`ExportWorker` is an ASP.NET Core `BackgroundService` that runs the export pipeline once per
day at a configured UTC time. It assigns sequence numbers, persists run state to the
export-log SQLite database, and calls the live query+build pipeline.

# Schedule

Default run time: **06:00 UTC** (configurable via `ExportWorker.ScheduledTimeUtc` in `appsettings.json`).

For development, set to `00:00:01` (one second after midnight) or a near-future time
to trigger an immediate run without waiting.

# Pipeline Execution

```
DynamicExportService.BuildExportAsync(config, format)  →  ExportPackage
IExportSink.WriteAsync()                               →  staging/*.{xlsx|csv|json} + staging/*.manifest.json
```

`RunExportAsync` calls the same `BuildExportAsync` decision point as `POST /api/pipeline/run`
and Preview — see [DynamicExportService](/pipeline/dynamic-export-service.md). The originally
documented fixed six-stage pipeline (`IErpReader → IExportFilter → IDataMinimizer →
ISchemaMapper → IPackager → IExportSink`) was never wired into this worker's live code path
and has since been removed; see that page for why the design changed.

# Run Lifecycle

1. A monotonically increasing sequence number is assigned (`MAX(SequenceNo) + 1` from the DB).
2. An `ExportRun` record is created with `Status = Pending`.
3. The pipeline executes. On failure: status is set to `Failed`; no retry.
4. On success: `RecordCount`, `Sha256`, and `DataFileName` are persisted. Status remains `Pending`.
5. A release operator and a separate approver advance the run to `Released` via the API.

# Error Handling

Export failures are logged at `Error` level. The run is marked `Failed`. No retry loop is
implemented — the next daily full-snapshot run heals the gap idempotently.

# Related

- [DynamicExportService](/pipeline/dynamic-export-service.md) — the query+build pipeline this worker calls
- [IExportSink](/pipeline/export-sink.md)
- [Four-Eyes Release](/operations/four-eyes-release.md)
