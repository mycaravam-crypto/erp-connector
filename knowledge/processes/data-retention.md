---
type: Business Process
title: Data Retention
description: Daily purge of staging files and completed ExportRun records older than the configured retention window.
resource: src/Connector.Infrastructure/ExportWorker.cs
tags: [process, retention, gdpr, compliance, cleanup]
timestamp: 2026-06-28T00:00:00Z
---

After each daily export run the [ExportWorker](/pipeline/export-worker.md) runs a retention
cleanup step that purges artefacts older than the configured `RetentionDays` window.

# Scope of Deletion

| Target                         | Condition for deletion                                           |
|--------------------------------|------------------------------------------------------------------|
| Staging files (`staging/`)     | `File.LastWriteTimeUtc` older than the retention cutoff.         |
| `ExportRun` DB rows            | `ExtractedAt` older than the cutoff AND `Status != Pending`.     |

**`Pending` runs are never auto-deleted.** They await [four-eyes release](/processes/four-eyes-release.md)
and must remain visible in the UI until released or superseded.

# Configuration

```json
{
  "ExportWorker": {
    "RetentionDays": 30
  }
}
```

| Value | Behaviour                                          |
|-------|----------------------------------------------------|
| `> 0` | Delete artefacts older than this many days.        |
| `0`   | Retention cleanup is disabled entirely.            |

Default: **30 days**, pending legal/DPO review ([Open Point #7](/processes/open-points.md)).

# Execution

Retention runs **after** the export pipeline within the same daily `ExportWorker` tick:

```
ExportWorker tick:
  1. RunExportAsync()           ← full pipeline (fail-fast; no retry)
  2. RunRetentionCleanupAsync() ← purge old artefacts
```

A retention error is logged at `Error` level but does **not** affect the export run outcome and
does not block the next tick. This isolates retention failures from export reliability.

# Compliance Note

Staging files are downstream of the [GDPR](/processes/gdpr-compliance.md) minimization boundary
and contain no personal data, but the retention period still needs DPO agreement: long enough to
cover the four-eyes release window, short enough to not exceed operational or contractual need.

# Related

- [ExportWorker](/pipeline/export-worker.md) — orchestrates the cleanup step
- [ExportRun](/domain/export-run.md) — database records subject to retention
- [IExportSink](/pipeline/export-sink.md) — writes staging files that this process later purges
- [GDPR Compliance](/processes/gdpr-compliance.md)
- [Open Points](/processes/open-points.md) — Open Point #7
