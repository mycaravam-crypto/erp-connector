---
type: Background Service
title: ImportWorker (Inbound Folder Watcher)
description: Polls an inbound/ staging folder, manifest- and idempotency-checks each vendor file, and stages it as a PendingReview ImportRunEntity — a sibling of ExportWorker, in reverse.
resource: src/Connector.Infrastructure/ImportWorker.cs
tags: [pipeline, orchestration, background-service, dynamic-import, phase-17]
timestamp: 2026-09-06T00:00:00Z
---

`ImportWorker` is Phase 17 Slice 4: the automatic half of the inbound pipeline — a saved
`ImportDefinition` otherwise has no way to receive vendor data. It is a **sibling** of
[ExportWorker](/pipeline/export-worker.md)/[ExportDefinitionWorker](/dynamic-export/scheduler.md),
not a replacement for either — it polls a folder (`inbound/`) instead of writing to one, and
nothing about the export side changes.

# Polling

The worker wakes every `ImportWorkerOptions.PollInterval` (default 30s) and, each tick, calls
`PollOnceAsync` — public specifically so a test can drive one pass directly against a real
`inbound/` directory and `testdb`, without waiting out the timer loop. A broken file or a
scope/DB failure during one file's processing is caught and logged; the rest of the pass
continues, matching `ExportWorker`'s resilience convention.

Per data file found in `inbound/` (its accompanying `<file>.manifest.json` is skipped as a
data file itself):

1. **Read the file.** An `IOException` (most likely a file still mid-copy from removable media)
   leaves the file for the next poll rather than quarantining something that will be complete a
   moment later.
2. **Manifest check.** Missing manifest, unparseable manifest JSON, missing
   `Sha256Checksum`, or a checksum mismatch against the actual file bytes → quarantined to
   `inbound/rejected/`, audit-logged (`import_file_rejected`). No sequence/gap check — Open
   Decision #8.
3. **Parse + route.** The file itself is parsed as JSON; its top-level `definition` property (Open
   Decision #14's `ImportEnvelope` field) names which saved, *enabled* `ImportDefinition` to use.
   Missing/invalid JSON, a missing `definition` property, or no matching enabled definition → all
   quarantined the same way as a manifest failure.
4. **Idempotency check (Open Decision #13).** `(ImportDefinitionId, Sha256Checksum)` is looked up
   against existing runs *before* parsing further. A match is classified via
   `ImportWorker.ClassifyDuplicate` — already-staged (`PendingReview`), already-released
   (`Released`), rejected duplicate (`Rejected`), or a bare "duplicate" for anything else
   (`Failed`) — audit-logged (`import_duplicate_detected`), and the file is moved to
   `inbound/processed/` without creating a second `ImportRunEntity`. A race between two pollers
   landing on the same pair is caught by the DB's own unique constraint on `INSERT` and handled
   identically, rather than crashing the poll.
5. **Walk + plan.** `ImportNodeWalker.WalkAsync` (Slice 2) parses the envelope against the
   definition's `RootNode`, then `ImportPlanBuilder.Build` (Slice 3) reshapes that into the
   persisted `ImportPlan`. An `ImportValidationException` from the walker (a structural problem
   with the saved definition, not an individual bad record) quarantines the file the same way a
   manifest failure does.
6. **Stage.** A new `ImportRunEntity` is inserted at `Status = PendingReview`, `TriggeredBy =
   "watcher"`, with `DefinitionSnapshotJson` set to the full `ImportDefinitionEntity` serialized
   at this exact moment (Open Decision #10) and `PlanJson` set to the built plan.
7. **Move.** On success, the source file + manifest move to `inbound/processed/` — never deleted,
   matching `FileSystemExportSink`'s atomic-move convention on the export side.

A worker-level exception during any of this is caught per-file and logged; it never crashes the
host process.

# Registration

`ImportSinkOptions.InboundPath` names the folder to poll (`processed/`/`rejected/` subfolders are
created under it on demand); `ImportWorkerOptions.PollInterval` defaults to 30 seconds. Registered
as a hosted service alongside `ExportWorker`/`ExportDefinitionWorker` in `Program.cs`.

# What this worker does *not* do

It never writes to the ERP — a staged run only ever reaches `PendingReview`. Committing an
approved run is [`ImportRunReleaser`](run-history.md)'s job (Slice 3), triggered by a human via
`POST /api/import-runs/{id}/release`, not by this worker.

# Related

- [Import Run & Four-Eyes Commit](run-history.md) — what happens to a run once this worker stages it
- [ImportNode Tree](import-node.md) — the tree this worker's walk step reads
- [ExportWorker](/pipeline/export-worker.md) — the outbound sibling this worker's polling model mirrors
- [Import Definitions §3](/pipeline/import-definitions.md#3-inbound-flow) — the full inbound flow, steps 1-5
