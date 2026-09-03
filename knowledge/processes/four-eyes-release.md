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
   - Sequence number is contiguous with the previous released run (no gaps). The UI surfaces a `SequenceGapWarning` banner if a gap is detected.
   - SHA-256 in the UI matches the `.manifest.json` on disk.
4. The Operator submits the Approver's username via `POST /api/exports/{seqNo}/release`. The Operator identity is inferred from the JWT — it cannot be supplied in the body.
5. The server validates `Operator != Approver` (case-insensitive) and that the Approver is a registered user. Rejects if either check fails.
6. Status advances to `Released`. `ReleasedAt`, `OperatedBy`, `ApprovedBy` are persisted.
7. The file at `staging/export_NNNN_...xlsx` may now be physically transferred.
8. After handover, the Operator records the delivery via `POST /api/exports/{seqNo}/deliver` (optional import count and notes). This closes the custody chain.

# API

```
POST /api/exports/{seqNo}/release
Body: { "approver": "bob" }          ← operator is inferred from JWT

200 OK          — released successfully
400 Bad Request — approver missing, operator == approver, or approver unknown
404 Not Found   — unknown sequence number
409 Conflict    — run already Released or Failed

POST /api/exports/{seqNo}/deliver
Body: { "importedRecordCount": 5, "notes": "USB-007, J. Smith" }   ← all fields optional

200 OK          — delivery recorded
400 Bad Request — run is not in Released status
404 Not Found   — unknown sequence number
409 Conflict    — run already marked as delivered
```

# Status Transitions

```
(pipeline running) → Pending
Pending → Released  (successful four-eyes approval)
Pending → Failed    (pipeline error during the export run)
```

`Released` and `Failed` are terminal. The release form is hidden for these runs in the UI.

# Constraints

- Operator and Approver must be different people, enforced server-side by comparing the
  JWT-authenticated Operator username against the submitted Approver username
  (case-insensitive) — see [Authentication](/processes/authentication.md).
- A `Failed` run cannot be released — a new export run must be triggered.

# Related

- [ExportWorker](/pipeline/export-worker.md)
- [ExportManifest](/domain/export-manifest.md)
- [IExportSink](/pipeline/export-sink.md)
- [Authentication](/processes/authentication.md) — JWT-based Operator/Approver identity
