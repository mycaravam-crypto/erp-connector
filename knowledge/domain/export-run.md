---
type: Infrastructure Domain Type
title: ExportRun
description: Persisted record of a single pipeline execution — carries lifecycle status, sequence number, SHA-256, and four-eyes release metadata.
resource: src/Connector.Infrastructure/ExportRunEntity.cs
tags: [domain, infrastructure, lifecycle, audit, sqlite]
timestamp: 2026-06-28T00:00:00Z
---

Every execution of the export pipeline — whether triggered by the daily [ExportWorker](/pipeline/export-worker.md)
or by an on-demand [Run Now](/processes/on-demand-run.md) call — is recorded as an `ExportRun` row in the
export-log SQLite database.

# Schema

| Field          | Type    | Description                                                                   |
|----------------|---------|-------------------------------------------------------------------------------|
| `Id`           | int     | Auto-incremented primary key (internal; not exposed in the API).              |
| `SequenceNo`   | int     | Monotonically increasing export sequence number. Gaps signal lost runs.       |
| `ExtractedAt`  | string  | ISO 8601 UTC timestamp of the ERP read (`DateTimeOffset.UtcNow` at run start).|
| `RecordCount`  | int     | Number of exported CI records. 0 for Failed runs.                             |
| `Sha256`       | string  | SHA-256 of the data file (hex, lowercase). Empty for Failed runs.             |
| `Status`       | string  | Lifecycle state: `Pending` | `Released` | `Failed`.                         |
| `ReleasedAt`   | string? | ISO 8601 UTC timestamp of the four-eyes release. Null until released.         |
| `OperatedBy`   | string? | Username of the JWT-authenticated user who triggered the release.             |
| `ApprovedBy`   | string? | Username of the approver (must differ from `OperatedBy`).                     |
| `DataFileName` | string  | Filename of the Excel file in the staging directory. Empty for Failed runs.   |

# Lifecycle

```
pipeline running  →  Pending
Pending           →  Released   (successful four-eyes approval via POST /api/exports/{seqNo}/release)
Pending           →  Failed     (pipeline error during execution)
```

`Released` and `Failed` are terminal states. A Failed run cannot be re-released; a new run must be triggered.

# Sequence Number

`SequenceNo` is assigned as `MAX(SequenceNo) + 1` from the database at run start.
The number is monotonically increasing and never reused.
The receiving gateway uses sequence gaps to detect lost exports without a back-channel.
See [ExportManifest](/domain/export-manifest.md) which carries the same sequence number into the manifest JSON.

# Persistence

Stored in the SQLite database configured via `ConnectionStrings:ExportLog` in `appsettings.json`.
Schema is managed by EF Core (`ExportLogDbContext.EnsureCreatedAsync` on startup).

# Data Retention

Completed runs (`Released` and `Failed`) are purged after the configured `RetentionDays` window.
`Pending` runs are never deleted automatically.
See [Data Retention](/processes/data-retention.md).

# Related

- [ExportWorker](/pipeline/export-worker.md) — creates and updates ExportRun records
- [ExportManifest](/domain/export-manifest.md) — carries SequenceNo and SHA-256 in the manifest file
- [Four-Eyes Release](/processes/four-eyes-release.md) — advances status from Pending to Released
- [On-Demand Run](/processes/on-demand-run.md) — alternative trigger that also creates an ExportRun
- [Data Retention](/processes/data-retention.md) — purge policy for completed runs
