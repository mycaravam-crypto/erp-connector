---
type: Infrastructure Domain Type
title: ImportRunEntity & Four-Eyes Commit
description: One row per staged/released/rejected/failed ImportDefinition run, its PlanJson write protocol, and how ImportRunReleaser commits an approved run to the ERP under optimistic concurrency.
resource: src/Connector.Infrastructure/ImportRunEntity.cs
tags: [domain, infrastructure, dynamic-import, phase-17]
timestamp: 2026-09-06T00:00:00Z
---

`ImportRunEntity` is the per-definition run-history analogue of
[ExportDefinitionRunEntity](/dynamic-export/run-history.md), combined with `ExportRunEntity`'s
four-eyes fields — an import commits to the system of record, a materially bigger risk than
reading, so it requires the same Operator/Approver review as export release (see [Import
Definitions §3](/pipeline/import-definitions.md#3-inbound-flow)).

# Schema

| Field | Type | Description |
|---|---|---|
| `Id`, `ImportDefinitionId`, `ConfigVersion` | | Standard run-history identity, same convention as `ExportDefinitionRunEntity`. |
| `DefinitionSnapshotJson` | string? | The full `ImportDefinitionEntity` — `RootNode`, `AllowedWritableColumns`, `UnmatchedRootPolicy`, everything — frozen exactly as it stood at staging time (Open Decision #10). `ConfigVersion` alone only names *which* version ran; this field *is* that version, so an approver reviewing this run is never looking at a diff computed against a definition that's since been edited. |
| `SourceFileName`, `Sha256Checksum` | string | No `SequenceNumber` field — the manifest alone covers file integrity for v1 (Open Decision #8). `(ImportDefinitionId, Sha256Checksum)` is a unique constraint (Open Decision #13). |
| `Status` | string | `PendingReview` \| `Released` \| `Rejected` \| `Failed` — an approval step `ExportDefinitionRunEntity` doesn't have. |
| `RecordCount`, `MatchedCount`, `ChangedCount`, `UnchangedCount`, `RejectedCount`, `ConflictCount`, `InvalidCount` | int | Explicit breakdown per Open Decision #11 — `MatchedCount = ChangedCount + UnchangedCount`; a row whose target fields already equal the incoming values is `Unchanged`, not folded into a bare "accepted" count. `ConflictCount` stays 0 until commit time. |
| `PlanJson` | string? | The persisted write protocol — see below. |
| `OperatedBy` / `ApprovedBy` / `ReleasedAt` | string? | Identical contract to `ExportRunEntity`'s four-eyes fields. |
| `TriggeredBy` | string | Username for a manual trigger, or the fixed marker `"watcher"` for [`ImportWorker`](import-worker.md). |

# PlanJson — the write protocol, not a second source of truth

`ImportPlan` (`Connector.Core.DynamicImport`) is a structured, versioned list of
`ImportPlanOperation`s — `(CorrelationValue, Table, KeyColumn, KeyValue, Column,
ExpectedOldValue, NewValue)` — one per column a matched/changed row will actually write. Building
it is a two-stage pipeline:

1. **`ImportNodeWalker.WalkAsync`** (Slice 2) parses the inbound `ImportEnvelope`, resolves each
   record's root-row match, and produces an `ImportWalkResult` — a per-row field-level diff
   (`ImportRowResult`/`ImportFieldDiff`), including object/array child resolution. This step
   performs **no writes** and is fully testable in isolation from the commit path.
2. **`ImportPlanBuilder.Build`** (Slice 3) reshapes that diff into the persisted `ImportPlan`:
   root-row field diffs become `ImportPlanOperation`s (child-table diffs stay preview-only in v1 —
   no real `ImportDefinition` writes to a child table yet, see `ImportNode`'s `OnMissingChild`
   note); an unchanged column produces no operation, since there's nothing to write.

The same walk-then-build pass is used for both `POST /api/import-definitions/{id}/preview`
(untracked, no `ImportRunEntity` row) and staging a real run, so preview and commit can never
disagree about what a row means — the same unification `DynamicExportService.BuildExportAsync`
already enforces on the export side. A UI diff or an audit-log line is a read projection of
`PlanJson`, never an independently-computed shape.

# Committing an approved run — `ImportRunReleaser`

`POST /api/import-runs/{id}/release` (`ImportRunEndpoints.cs`) validates the Operator/Approver
distinctness via the shared `FourEyesReview.ValidateApprover` helper (now used by the export
release endpoint too), confirms the run is still `PendingReview`, and calls
`ImportRunReleaser.ReleaseAsync`:

1. Groups `PlanJson`'s operations by row (`Table`/`KeyColumn`/`KeyValue`) and issues one
   conditional `UPDATE` per row inside a single transaction: every changed column in one `SET`
   clause, guarded by *every* one of that row's `ExpectedOldValue`s in one `WHERE` clause
   (`col::text IS NOT DISTINCT FROM @old`) — so a row commits all its changed columns or none,
   never half (Open Decision #12).
2. **Zero affected rows** means the ERP row's state moved on since the plan was built; that row is
   counted in `ConflictCount`, excluded, and left untouched — it does **not** fail the run (Open
   Decision #6 — one stale or bad row doesn't block every good one in the same file).
3. Any other exception (e.g. an unrelated constraint violation) rolls back everything committed so
   far in this call — connection/transaction disposal on the way out of the `try` block rolls back
   an uncommitted transaction automatically — and marks the run `Failed` with the exception
   message. No silent partial success at the transaction level.
4. On success, `Status = Released`, `OperatedBy`/`ApprovedBy`/`ReleasedAt` are set, and
   `AuditService` logs the full matched/changed/unchanged/rejected/invalid/conflict breakdown.

`ImportRunReleaser.RejectAsync` (`POST .../reject`) is the single-person decline path — it never
writes to the ERP, so it needs no distinct Approver, the same reasoning the export side's own
single-person "skip a run" action rests on.

# Reading a run

`GET /api/import-definitions/{id}/runs` returns the summary counts, newest-first, capped at 200 —
the list view's data source. `GET /api/import-runs/{id}` (added in Slice 6, alongside the
frontend) returns one run's full detail *including* `PlanJson`'s operations — the review dialog
needs the actual per-row diff before an Approver can meaningfully decide, which the summary
endpoint alone doesn't carry. Unlike release/reject, this is a plain read available regardless of
`Status` — revisiting a `Released`/`Rejected`/`Failed` run's plan after the fact is useful too.

# Related

- [ImportWorker](import-worker.md) — the only automated way a `PendingReview` run is created
- [ImportNode Tree](import-node.md) — the tree `ImportNodeWalker` walks to build the diff
- [Import Definitions §3](/pipeline/import-definitions.md#3-inbound-flow) — the full flow, steps 5-8
- [ExportDefinitionRunEntity](/dynamic-export/run-history.md) — the read-side run-history sibling
- [Four-Eyes Release](/operations/four-eyes-release.md) — the Operator/Approver contract this reuses
