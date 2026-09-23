---
type: Pipeline Design
title: Import Definitions — inbound JSON write-back
description: The reverse leg of the connector — vendor-supplied JSON written back into the live ERP database under the same air-gap and four-eyes controls as the export path.
resource: src/Connector.Core/DynamicImport/ImportNode.cs
tags: [pipeline, dynamic-mapping]
timestamp: 2026-09-06T00:00:00Z
---

## 1. Why this exists

The connector is one-way and air-gapped by design on the export side: it reads the ERP, builds an
export file, and stops at a staging folder — see [knowledge/index.md](/index.md) ("the connector
never crosses the air gap"). The vendor also needs to send confirmations/updates about CIs back to
the ERP. This document describes how that happens without breaking the compliance properties (air
gap, four-eyes, auditability) the rest of the system relies on.

This reuses `FieldMapping`, `AuditService`, the JWT Operator/Approver check, and the atomic-file-write
pattern rather than reinventing them; one recursive `ImportNode` tree mirrors `ExportNode` rather
than a bespoke shape per table.

## 2. Vision

Stated symmetrically to the existing pipeline:

```
Export:  ERP (read)   --ExportNode tree-->  staging file (JSON/CSV/Excel)  --human carry-->  vendor
Import:  vendor JSON  --human carry-->  inbound/ folder  --ImportNode tree-->  ERP (write)
```

This is **not** a generic bidirectional sync engine. The vendor confirms or updates data about CIs
the connector already exported to it — it does not originate new CIs. Two policy decisions follow
directly and shape everything else in this document:

* **Root rows are matched, never blindly created.** Every inbound record's correlation key (the
  same `Guid` used on the export side) must resolve to an existing CI row. No match → the record
  is quarantined, not inserted.
* **Writable columns are an explicit allowlist** per import definition — the inverse of the export
  side's GDPR denylist (default-deny instead of default-allow-except). A saved mapping can never
  touch a column outside its agreed scope: no primary keys, no untracked foreign keys, nothing
  outside the vendor contract.

Four-eyes review is required before any write reaches the ERP, matching the rigor already applied
to the outbound release — this writes to the system of record, a materially bigger risk than
reading from it.

**Scope:** the vendor is only allowed to write confirmation/status fields on the root entity — the
narrowest option. `ImportNode`'s `object`/`array` child-node mechanism (§4) stays in the type model
— the same "no new types for a new source table" property `ExportNode` already has — but no
`ImportDefinition` today exercises `OnMissingChild = insert` (e.g. the vendor adding a discovered
serial number); the save-time validator rejects any definition that sets it, so the capability
can't reach production before a real vendor ICD requires it.

## 3. Comparison with the export side

| Capability | Export side | Import side |
|---|---|---|
| Direction | ERP → staging file | inbound file → ERP |
| Tree model | `ExportNode` (`Connector.Core.DynamicExport`) | `ImportNode` (`Connector.Core.DynamicImport`) |
| Field-level control | `FieldMapping` (transform, default, data type) — reused as-is | same type, reused as-is |
| Column scope control | GDPR denylist (default-allow-except) | `AllowedWritableColumns` allowlist (default-deny-except) |
| Trigger | `ExportDefinitionWorker` polls cron | `ImportWorker` polls an `inbound/` folder, or an operator stages a file manually from the UI (`POST /api/import-definitions/{id}/runs`) |
| Integrity | SHA-256 manifest + sequence number, `IExportSink` | SHA-256 manifest only — no sequence/gap detection (§6) |
| Human approval | Four-eyes release (Operator/Approver, distinct JWT users) | same contract, generalized into a shared helper |
| Run record | `ExportRunEntity` / `ExportDefinitionRunEntity` | `ImportRunEntity` |
| Audit | `AuditService.LogAsync` | same service, new action names |
| Root-row semantics | N/A (read-only) | match-only — never auto-creates a root row |

## 4. Inbound flow

```
Vendor produces JSON (per the ICD) + SHA-256 manifest
  ↓ physical media, human-carried — same air gap, opposite direction
inbound/ folder on the connector host
  ↓ ImportWorker polls inbound/ (sibling of ExportDefinitionWorker; same "poll, don't push" model)
1. Manifest check     — SHA-256 of the file matches the accompanying manifest (no sequence/gap
                         check — §6), then an idempotency check: (ImportDefinitionId,
                         Sha256Checksum) is looked up against existing runs — an exact repeat of an
                         already-staged/released file is reported back as a duplicate
                         (distinguishing already-staged / already-released / rejected duplicate)
                         and never creates a second ImportRunEntity
2. Envelope + shape validation — the file is parsed as the canonical ImportEnvelope: schemaVersion
                         is checked before anything else — an unknown or missing version is
                         rejected outright, never shape-guessed — then the records[] payload is
                         walked against the saved ImportNode tree; malformed input is quarantined
                         (moved to inbound/rejected/), never partially processed
3. Row mapping        — per record: resolve the correlation key against RootTable/RootMatchColumn;
                         no match → quarantined per UnmatchedRootPolicy (reject | quarantine —
                         never auto-create, see §2)
4. Row validation     — target columns checked against AllowedWritableColumns; FieldMapping data-type
                         coercion; every JoinKey on an object/array child must resolve to a real
                         parent row or the child is rejected
5. Stage, don't write — ImportRun.Status = PendingReview; every record is classified as
                         matched/changed, matched/unchanged, rejected, or invalid. The definition
                         actually used is frozen onto the run as DefinitionSnapshotJson so a later
                         edit to the live definition can never change what an already-staged run
                         means to its reviewer. The walker's output is PlanJson: a structured,
                         versioned list of operations (table, row-key, column, old value, new
                         value) — the write-side source of truth. Unchanged rows produce no
                         operation at all; the UI diff is a read-only projection of PlanJson, never
                         a second, independently-authoritative shape
  ↓
6. Four-eyes review   — an Operator reviews the plan (matched/changed, unchanged, rejected, invalid
                         counts, an accepted-row sample) in the UI and submits a distinct Approver
                         — the exact POST .../release contract the export side already has
7. Commit             — on approval, each operation in PlanJson commits as a conditional write —
                         UPDATE ... WHERE <pk> AND <column> IS NOT DISTINCT FROM @expectedOldValue
                         — guarding against the ERP row having changed since the plan was computed;
                         a row whose expected-old-value no longer matches is excluded from this run
                         as Conflicted, not overwritten, and counted separately from Rejected. One
                         DB transaction applies every row still matched/changed after the
                         concurrency check; if the commit transaction itself fails for an unrelated
                         reason, it rolls back completely and the run is marked Failed with a
                         specific error
8. Audit + archive    — AuditService logs Operator/Approver/matched/changed/unchanged/rejected/
                         conflicted counts; the source file + manifest move to inbound/processed/,
                         never deleted
```

A rejected/quarantined/conflicted **row** does not fail the run by design: the run commits its
matched/changed rows and reports the rest for manual follow-up, rather than one bad or stale
vendor row blocking every good one in the file.

## 5. Data Model

```
ImportDefinition                          (EF Core entity)
├── Id, Name, Description
├── RootTable, RootMatchColumn            (column the correlation key matches against)
├── RootNode                : ImportNode  (the tree — the save-time validator rejects any node with
│                                           OnMissingChild = insert)
├── AllowedWritableColumns  : string[]    (explicit allowlist — validated at save time not just for
│                                           presence in the list but against the introspected schema:
│                                           a column must exist on its table, and must not be the
│                                           primary key, an identity/computed column, or an untracked
│                                           foreign key unless explicitly permitted. This is the
│                                           primary safety boundary of the whole feature, so it's
│                                           re-checked at run time by the walker too — a stale saved
│                                           definition is never trusted silently. Confirmation/status
│                                           fields on the root only — see §2)
├── UnmatchedRootPolicy     : reject | quarantine   (never "auto-create" — see §2)
├── IsEnabled               : bool
├── ConfigVersion           : int
├── IntegrationKey / ContractVersion      (see [Import Mapping Presets](/pipeline/import-mapping-presets.md))
├── CreatedBy / CreatedAt / UpdatedBy / UpdatedAt

ImportNode                                (recursive — mirrors ExportNode exactly in shape)
├── SourceKey               : string      (the JSON property / array name this node reads)
├── Kind                    : root | scalar-field | object | array
├── TargetColumn            : string?     (set when Kind = scalar-field)
├── RelatedTable / JoinKey / SourceJoinKey  (set when Kind = object | array)
├── OnMissingChild          : insert | reject  (array children MAY be created — e.g. a new
│                                                SerialNumber row on an existing CI — since that's
│                                                additive, not a new top-level entity; object
│                                                children, being N:1, are always match-only)
├── Mapping                 : FieldMapping?    (reused verbatim from Connector.Core.DynamicExport —
│                                                same DefaultValue/Transform/TransformArg/DataType)
├── Children                : ImportNode[]
└── Enabled                 : bool

ImportRunEntity                           (mirrors ExportDefinitionRunEntity + ExportRun's
                                            four-eyes fields combined)
├── Id, ImportDefinitionId, ConfigVersion
├── DefinitionSnapshotJson  : string      (the ImportDefinition — RootNode, AllowedWritableColumns,
│                                           UnmatchedRootPolicy, everything — exactly as it stood at
│                                           staging time. ConfigVersion alone only names *which*
│                                           version was used; this is that version, frozen, so editing
│                                           the live definition can never change what an
│                                           already-staged run means to its reviewer)
├── SourceFileName, Sha256Checksum         (no SequenceNumber — see §6; the pair
│                                           (ImportDefinitionId, Sha256Checksum) is unique)
├── StartedAt / FinishedAt (UTC)
├── Status                  : PendingReview | Released | Rejected | Failed
├── RecordCount, MatchedCount, ChangedCount, UnchangedCount, RejectedCount, ConflictCount,
│   InvalidCount            (unchanged rows are counted explicitly, not folded silently into
│                            "accepted"; ConflictCount is populated at commit time)
├── PlanJson                : string      (the persisted write plan — structured operations with
│                                           table/row-key/column/oldValue/newValue/expectedOldValue
│                                           per matched/changed row; the UI diff is a read-only
│                                           projection of this, never a second authoritative shape)
├── ErrorMessage            : string?
├── OperatedBy / ApprovedBy / ReleasedAt  (identical contract to ExportRunEntity's four-eyes fields)
└── TriggeredBy             : string      (username, or "watcher" for the folder-poll trigger)
```

**Why a tree, not a bespoke shape per table:** identical reasoning to `ExportNode` — one recursive
type walked by one writer, not a parallel shape per relationship kind. `ImportNode` intentionally
does **not** merge into `ExportNode`: the two trees flow in opposite directions (`SourceKey` reads
from JSON vs. `TargetKey` writes into it; `TargetColumn` writes to SQL vs. `SourceField` reads from
SQL) and carry direction-specific policy (`OnMissingChild`, `AllowedWritableColumns`) that doesn't
apply to the other side. Forcing them into one type would mean nullable fields that are meaningless
half the time — worse than two small, honest types.

## 6. Design decisions

* **Root-match key** is the same `Guid` correlation key the connector already exports — no new
  identifier needs to be introduced on the vendor's side. `RootMatchColumn` stays a
  per-`ImportDefinition` setting rather than a hardcoded assumption, in case a future definition
  needs something else.
* **Partial-file commit policy:** commit accepted rows, quarantine the rest. A single bad row from
  the vendor doesn't block every good row in the same file; rejected rows are reported on the run
  for manual follow-up rather than failing the whole batch.
* **GDPR on the inbound side** is enforced defensively even though personal data isn't expected in
  a confirmation/status payload — the denylist check runs on write regardless, the same
  defence-in-depth posture the export side already applies on read.
* **Sequence/gap detection is not built for v1.** The SHA-256 manifest already gives per-file
  integrity; vendor confirmations don't arrive 1:1 per export run, so sequence-number gap detection
  doesn't map cleanly onto this direction the way it does for the outbound side. No
  `SequenceNumber` field on `ImportRunEntity`.
* **`AllowedWritableColumns` is validated against the real schema, not just list membership.** A
  `TargetColumn` must exist on its table, and must not be a primary key, an identity/computed
  column, or an untracked foreign key unless explicitly permitted. Checked at save time and
  re-checked by the walker at run time — a stale saved definition is never trusted silently.
* **The effective definition is frozen at staging time.** `ConfigVersion` alone only names which
  version of a definition was used; it doesn't preserve what that version actually said if the live
  definition is edited between staging and review. `ImportRunEntity.DefinitionSnapshotJson` freezes
  the full definition — tree, allowlist, policy — at the moment a run is staged, so everything the
  reviewer saw is exactly what gets committed, regardless of later definition edits.
* **`PlanJson` is the write protocol; the diff is a view of it, not a second source of truth.** The
  walker computes preview and commit input in the same pass (so they can never disagree), and its
  output is a structured, versioned `PlanJson` (table/row-key/column/oldValue/newValue/
  expectedOldValue per operation) rather than a UI-shaped diff blob. The UI diff, the commit step,
  and the audit log are all projections of one `ImportPlan`, not three consumers of a JSON string
  that happens to also be a write command.
* **Optimistic concurrency at commit time.** Each `PlanJson` operation carries the value the walker
  observed when the plan was built; commit applies it as `UPDATE ... WHERE <pk> AND <column> IS NOT
  DISTINCT FROM @expectedOldValue`. Zero affected rows means the ERP state moved on — that row is
  marked Conflicted, excluded from the commit, and reported distinctly from Rejected, rather than
  silently overwriting a legitimate newer change.
* **Idempotency via source-file hash.** `(ImportDefinitionId, Sha256Checksum)` is a unique
  constraint — the same vendor file dropped into `inbound/` twice, by mistake or a re-triggered
  watcher, cannot create a second pending run. The worker reports which of duplicate /
  already-staged / already-released / rejected-duplicate applies, rather than silently
  rediscovering and re-queuing the same file.
* **Canonical, versioned import envelope.** `ImportEnvelope`: `schemaVersion`, `definition` (which
  saved mapping this targets), `generatedAt`, `sourceSystem`, `correlationId`, `records[]`.
  `schemaVersion` is checked first and an unrecognized or missing value is rejected outright — the
  parser never shape-guesses a version.

  **Known confusion:** an *exported* job file (`JsonExportFormatWriter`'s own output — `schema_version`
  snake_case, `extracted_at`, optional `provenance`, `records`) is not an `ImportEnvelope` and cannot be
  edited and pasted back in as one, even though both shapes carry `provenance`/`records` — they're
  unrelated formats that happen to share vocabulary. `ImportNodeWalker.ParseRecords` detects this specific
  case (`schema_version` present, `schemaVersion` absent) and throws a message naming it explicitly.
* **`OnMissingChild = insert` is removed from v1, enforced, not just unexercised.** The save-time
  validator rejects any definition setting it. A capability sitting unused in production is still a
  capability a mistake can reach; rejecting it at save time closes that gap until a real vendor ICD
  requires child-row creation (e.g. a discovered serial number), at which point it becomes a
  deliberate scope expansion with its own review.

## 7. Non-Functional Requirements

| Quality | Requirement |
|---|---|
| **Reliability** | Row-level rejection is intentional — an invalid row is excluded from the accepted set before the transaction opens. That accepted set then commits as one all-or-nothing transaction: a technical failure during commit rolls back completely and fails the whole run, never a half-applied write. |
| **Consistency** | Every commit operation is a conditional write against an expected-old-value captured when the plan was built; a row whose ERP state moved on since staging is marked Conflicted, not overwritten. The connector never silently clobbers a change made by another process while a run sat in review. |
| **Idempotency** | `(ImportDefinitionId, Sha256Checksum)` is a unique constraint — the same vendor file dropped into `inbound/` twice cannot produce a second run. |
| **Security** | Every endpoint requires authentication; the commit step additionally requires two distinct authenticated users (Operator + Approver), same as export release. Writable columns are allowlisted per definition and validated against the introspected schema — not just checked for list membership. |
| **Traceability** | Every run carries `ConfigVersion`, an immutable `DefinitionSnapshotJson`, `OperatedBy`, `ApprovedBy`, and a persisted `PlanJson` — "what was written, against what definition, and who approved it" is reconstructable after the fact, independent of later definition edits. |
| **Auditability without a back-channel** | SHA-256 manifest on the inbound file (no sequence number — §6). |
| **Maintainability** | A new source table needs zero new C# types — same OCP property `ExportNode` already has. |

**SOLID, concretely:** SRP — parsing/matching, validation, diff-building, and commit stay separate
concerns in `ImportNodeWalker`, same separation `DynamicExportService` keeps on the export side.
OCP — a new target table needs zero code; a new inbound file format (only JSON is in scope) would
be the one thing needing a new parser. DIP — `ImportNodeWalker` takes an already-open
`NpgsqlConnection`/transaction, not a concrete provider, so the four-eyes commit step can run
preview and commit against the same transaction scope without opening two connections.

## 8. Non-Goals

* **No auto-creation of new root-level CIs from vendor data.** Matching against an existing row is
  mandatory at the root — see §2. Only object/array *children* (e.g. a new SerialNumber row under
  an existing CI) may be created, and only when a node's `OnMissingChild = insert`.
* **No live network write-back.** This stays file + air gap + human carry, just the reverse leg —
  not an API the vendor calls directly.
* **No scripting/expression engine for transforms.** Reuses the same closed `Transform` enum as
  export — no formula language.
* **No generic bidirectional sync engine.** This is the vendor-confirmation return channel, not a
  symmetric System A ↔ System B replication tool.
* **No multi-tenant / multi-vendor support.** One vendor return channel, as today's one vendor
  export target.

## Related

- [Dynamic Import](/dynamic-import/index.md) — how this runs: the `ImportNode` tree, `ImportWorker`,
  and `ImportRunEntity`/`ImportRunReleaser`
- [Export Definitions](/pipeline/export-definitions-2.0.md) — the export-side sibling this design mirrors throughout
- [DynamicExportService](/pipeline/dynamic-export-service.md) — the live export pipeline
  `ImportNodeWalker` is modeled on
- [Four-Eyes Release](/operations/four-eyes-release.md) — the approval contract this reuses
- [GDPR Compliance](/operations/gdpr-compliance.md) — the denylist model `AllowedWritableColumns` inverts
- [ExportManifest](/domain/export-manifest.md) — the integrity contract the inbound manifest mirrors
- [Import Mapping Presets from Export Provenance](/pipeline/import-mapping-presets.md) — prefills a
  new `ImportDefinition` from a paired, provenance-tagged `ExportDefinition`
