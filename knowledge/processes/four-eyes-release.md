---
type: Business Process
title: Four-Eyes Release
description: Manual dual-approval process that must complete before an export file is transferred to the vendor gateway.
tags: [process, release, approval, four-eyes, compliance]
timestamp: 2026-06-28T00:00:00Z
---

Every export run must be approved by two distinct people — an **Operator** and an **Approver** —
before the export file is cleared for physical transfer to the vendor gateway. The connector
never crosses the air gap; physical transfer (gateway → USB → vendor) is a separate,
human-controlled process.

# Steps

1. [ExportWorker](/pipeline/export-worker.md) completes a run successfully. Status: `Pending`.
2. An Operator opens the release UI and reviews the run details (`GET /api/exports/{seqNo}`).
3. The Operator verifies:
   - Record count looks correct.
   - Sequence number is contiguous with the previous released run (no gaps).
   - SHA-256 in the UI matches the `.manifest.json` on disk.
4. The Operator submits their name and the Approver's name via `POST /api/exports/{seqNo}/release`.
5. The server validates `Operator != Approver` (case-insensitive). Rejects if identical.
6. Status advances to `Released`. `ReleasedAt`, `OperatedBy`, `ApprovedBy` are persisted.
7. The file at `staging/export_NNNN_...xlsx` may now be physically transferred.

# API

```
POST /api/exports/{seqNo}/release
Body: { "operator": "alice", "approver": "bob" }

200 OK         — released successfully
400 Bad Request — missing fields or operator == approver
404 Not Found  — unknown sequence number
409 Conflict   — run already Released or Failed
```

# Status Transitions

```
(pipeline running) → Pending
Pending → Released  (successful four-eyes approval)
Pending → Failed    (pipeline error during the export run)
```

`Released` and `Failed` are terminal. The release form is hidden for these runs in the UI.

# Sequence Integrity Check

Before releasing, verify the [ExportManifest](/domain/export-manifest.md) `SequenceNumber`
is contiguous with the previous released run. A gap (e.g. #41 → #43) signals that run #42
was lost and must be investigated before proceeding.

# Constraints

- Operator and Approver must be different people — enforced server-side (authoritative) and
  client-side (as a usability guard).
- No authentication in Iteration 1 — operator/approver are free-text strings.
  Authentication is planned for Iteration 2+.
- A `Failed` run cannot be released — a new export run must be triggered.

# Related

- [ExportWorker](/pipeline/export-worker.md)
- [ExportManifest](/domain/export-manifest.md)
- [IExportSink](/pipeline/export-sink.md)
