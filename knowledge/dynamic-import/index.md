# Dynamic Import (Phase 17 — Import Definitions)

How a saved `ImportDefinition` actually runs: the tree shape it's configured from, how the
inbound folder watcher picks up a vendor file and stages it, and how a staged run is reviewed and
committed. This is the write-side mirror of [Dynamic Export](/dynamic-export/index.md) — same
shapes, opposite direction, and does not touch anything on the export side. See [Import
Definitions](/pipeline/import-definitions.md) for the full spec, all fifteen Open Decisions, and
the per-slice implementation status this bundle documents the shipped result of.

* [ImportNode Tree](import-node.md) — the recursive data shape (`scalar-field`/`object`/`array`)
  every definition is built from, its `AllowedWritableColumns` schema-aware validation, and how a
  `FieldMapping` transforms one scalar value (reused verbatim from the export side)
* [ImportWorker](import-worker.md) — the inbound folder watcher: manifest check, idempotency
  check, quarantine, and how a vendor file becomes a `PendingReview` run
* [Import Run & Four-Eyes Commit](run-history.md) — `ImportRunEntity`: one row per run, the
  `PlanJson` write protocol, and how `ImportRunReleaser` commits an approved run to the ERP under
  optimistic concurrency

# Related

- [Import Definitions](/pipeline/import-definitions.md) — the full spec and per-slice
  implementation status this bundle documents the shipped result of
- [Dynamic Export](/dynamic-export/index.md) — the export-side sibling this mirrors throughout
- [Open Points](/planning/open-points.md) — Open Point #6, which this feature resolves
- [Four-Eyes Release](/operations/four-eyes-release.md) — the approval contract `ImportRunReleaser` reuses
