---
type: Pipeline Design
title: Import Definitions — inbound JSON write-back (Phase 17)
description: Spec for the reverse leg of the connector — vendor-supplied JSON written back into the live ERP database under the same air-gap and four-eyes controls as the existing export path. Not started; no code exists yet.
resource: src/Connector.Core/DynamicImport/ImportNode.cs
tags: [pipeline, dynamic-mapping, phase-17, planning, not-started]
timestamp: 2026-09-05T00:00:00Z
---

> **Status: planning only, decisions resolved.** Nothing in this document is implemented, but all
> eight items in [§6 Open Decisions](#6-open-decisions) now have an answer — nothing but the actual
> vendor ICD contract (for exact column names) blocks starting Slice 1. This exists so the design
> is settled, reviewed, and sliced into PRs before any code is written — the same process
> [Export Definitions 2.0](/pipeline/export-definitions-2.0.md) went through. See
> [Implementation status](#implementation-status) for the slice checklist and the tracking issue.

---

## 0. Why this exists

The connector today is one-way and air-gapped by design: it reads the ERP (System A), builds an
export file, and stops at a staging folder — [knowledge/index.md](/index.md) states this
explicitly ("the connector never crosses the air gap"). [Open Point #6](/planning/open-points.md)
("Return-channel timing — vendor will eventually write back CI update confirmations") and
[Export Definitions 2.0 §10](/pipeline/export-definitions-2.0.md#10-non-goals) ("No live
write-back connector to a second database") both deliberately deferred this. This document picks
that back up: the vendor needs to send confirmations/updates about CIs back to the ERP, and this
defines how that happens without breaking the compliance properties (air gap, four-eyes,
auditability) the rest of the system relies on.

**Engineering Directive (same as Phase 14 — non-negotiable):** minimal code (generalize existing
types before adding new ones — this reuses `FieldMapping`, `AuditService`, the JWT
Operator/Approver check, and the atomic-file-write pattern rather than reinventing them); minimal
complexity (one recursive `ImportNode` tree, mirroring `ExportNode`, not a bespoke shape per
table); maximal documentation; no silent failure paths; SOLID applied per layer (§7).

---

## 1. Vision

Stated symmetrically to the existing pipeline:

```
Export:  ERP (read)   --ExportNode tree-->  staging file (JSON/CSV/Excel)  --human carry-->  vendor
Import:  vendor JSON  --human carry-->  inbound/ folder  --ImportNode tree-->  ERP (write)
```

This is **not** a generic bidirectional sync engine. The actual requirement (Open Point #6) is
narrower and lower-risk: the vendor confirms or updates data about CIs the connector already
exported to it — it does not originate new CIs. Two policy decisions follow directly and shape
everything else in this document:

* **Root rows are matched, never blindly created.** Every inbound record's correlation key (the
  same `Guid` used today, per [Open Points #1](/planning/open-points.md)) must resolve to an
  existing CI row. No match → the record is quarantined, not inserted.
* **Writable columns are an explicit allowlist** per import definition — the inverse of the
  export side's GDPR denylist (default-deny instead of default-allow-except). A saved mapping can
  never touch a column outside its agreed scope: no primary keys, no untracked foreign keys,
  nothing outside the vendor contract.

Four-eyes review is required before any write reaches the ERP, matching the rigor already applied
to the outbound release — this writes to the system of record, which is a materially bigger risk
than reading from it.

**v1 scope (per [Open Decision #5](#6-open-decisions)):** the vendor is only allowed to write
confirmation/status fields on the root entity — the narrowest option, matching Open Point #6's
original framing ("write back CI update confirmations"). `ImportNode`'s `object`/`array`
child-node mechanism (§4) stays in the type model — the same "no new types for a new source table"
property `ExportNode` already has — but the first real `ImportDefinition`s won't exercise
`OnMissingChild = insert`; that's a v2+ scope expansion (e.g. the vendor adding a discovered
serial number), not a v1 requirement.

---

## 2. Current State

| Capability | Export side (exists) | Import side (this doc) |
|---|---|---|
| Direction | ERP → staging file | inbound file → ERP |
| Tree model | `ExportNode` (`Connector.Core.DynamicExport`) | `ImportNode` (`Connector.Core.DynamicImport`, new) |
| Field-level control | `FieldMapping` (transform, default, data type) — reused as-is | same type, reused as-is |
| Column scope control | GDPR denylist (default-allow-except) | `AllowedWritableColumns` allowlist (default-deny-except) |
| Trigger | `ExportDefinitionWorker` polls cron | `ImportWorker` polls an `inbound/` folder |
| Integrity | SHA-256 manifest + sequence number, `IExportSink` | SHA-256 manifest only for v1 — no sequence/gap detection, see Open Decision #8 |
| Human approval | Four-eyes release (Operator/Approver, distinct JWT users) | same contract, generalized into a shared helper |
| Run record | `ExportRunEntity` / `ExportDefinitionRunEntity` | `ImportRunEntity` (new) |
| Audit | `AuditService.LogAsync` | same service, new action names |
| Root-row semantics | N/A (read-only) | match-only — never auto-creates a root row |

---

## 3. Inbound flow

```
Vendor produces JSON (per the ICD) + SHA-256 manifest
  ↓ physical media, human-carried — same air gap, opposite direction
inbound/ folder on the connector host
  ↓ ImportWorker polls inbound/ (sibling of ExportDefinitionWorker; same "poll, don't push" model)
1. Manifest check     — SHA-256 of the file matches the accompanying manifest (no sequence/gap
                         check for v1 — see Open Decision #8)
2. Shape validation   — parse JSON, walk it against the saved ImportNode tree; malformed input is
                         quarantined (moved to inbound/rejected/), never partially processed
3. Row mapping        — per record: resolve the correlation key against RootTable/RootMatchColumn;
                         no match → quarantined per UnmatchedRootPolicy (reject | quarantine —
                         never auto-create, see §1)
4. Row validation     — target columns checked against AllowedWritableColumns; FieldMapping data-type
                         coercion; every JoinKey on an object/array child must resolve to a real
                         parent row or the child is rejected
5. Stage, don't write — ImportRun.Status = PendingReview; a field-level diff (old value → new value,
                         per row) is computed and persisted as DiffJson so review doesn't require
                         re-parsing the source file
  ↓
6. Four-eyes review   — an Operator reviews the diff (accepted-row sample, rejected/quarantined
                         count, record count) in the UI and submits a distinct Approver — the exact
                         POST .../release contract the export side already has
7. Commit             — on approval, one DB transaction applies every **accepted** row (rows
                         rejected/quarantined in steps 2-4 were already excluded before the
                         transaction opens — that's intentional, see Open Decision #6); if the
                         commit transaction itself fails (e.g. an unexpected constraint violation),
                         it rolls back completely and the run is marked Failed with a specific
                         error — the "never a silent partial success" rule still holds at the
                         transaction level, it just applies to the accepted set, not the raw file
8. Audit + archive    — AuditService logs Operator/Approver/accepted/rejected counts; the source
                         file + manifest move to inbound/processed/, never deleted
```

A rejected/quarantined **row** does not fail the run by design — see
[Open Decision #6](#6-open-decisions): the run commits its accepted rows and reports the rest for
manual follow-up, rather than one bad vendor row blocking every good one in the file.

---

## 4. Data Model

```
ImportDefinition                          (new EF Core entity)
├── Id, Name, Description
├── RootTable, RootMatchColumn            (column the correlation key matches against)
├── RootNode                : ImportNode  (the tree)
├── AllowedWritableColumns  : string[]    (explicit allowlist — validated at save time; a save
│                                           that targets a column outside this list is rejected.
│                                           v1 scope per Open Decision #5: confirmation/status
│                                           fields on the root only — see §1)
├── UnmatchedRootPolicy     : reject | quarantine   (never "auto-create" — see §1)
├── IsEnabled               : bool
├── ConfigVersion           : int
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

ImportRunEntity                           (new — mirrors ExportDefinitionRunEntity + ExportRun's
                                            four-eyes fields combined)
├── Id, ImportDefinitionId, ConfigVersion
├── SourceFileName, Sha256Checksum         (no SequenceNumber — deferred, see Open Decision #8)
├── StartedAt / FinishedAt (UTC)
├── Status                  : PendingReview | Released | Rejected | Failed
├── RecordCount, AcceptedCount, RejectedCount
├── DiffJson                : string      (the persisted preview — old → new per accepted field)
├── ErrorMessage            : string?
├── OperatedBy / ApprovedBy / ReleasedAt  (identical contract to ExportRunEntity's four-eyes fields)
└── TriggeredBy             : string      (username, or "watcher" for the folder-poll trigger)
```

**Why a tree, not a bespoke shape per table:** identical reasoning to `ExportNode` — one recursive
type walked by one writer, not a parallel shape per relationship kind. `ImportNode` intentionally
does **not** merge into `ExportNode` itself: the two trees flow in opposite directions (`SourceKey`
reads from JSON vs. `TargetKey` writes into it; `TargetColumn` writes to SQL vs. `SourceField`
reads from SQL) and carry direction-specific policy (`OnMissingChild`, `AllowedWritableColumns`)
that doesn't apply to the other side. Forcing them into one type would mean nullable fields that
are meaningless half the time — worse than two small, honest types.

---

## 5. Reused vs. New

**Reused as-is:**
- Schema introspection (`IntrospectSchemaAsync`) for the tree-builder's table/column pickers —
  same mechanism the export tree builder already uses for `RelatedTable`/`JoinKey` suggestions.
- `FieldMapping` and its `Transform`/`DataType` coercion logic (`Connector.Core.DynamicExport`) —
  no import-specific transform semantics are needed.
- `AuditService.LogAsync` — new action strings only (`ImportRunStaged`, `ImportRunReleased`, etc.).
- The four-eyes Operator/Approver-distinctness check currently inline in the export release
  endpoint — generalized into a shared helper both directions call, rather than duplicated.
- The atomic-write pattern from `FileSystemExportSink` (write to `.tmp`, rename, then manifest) —
  mirrored for moving processed/rejected inbound files, so a half-read file is never visible as
  "processed."

**New:**
- `ImportNode`/`ImportDefinition`/`FieldMapping`-consuming `ImportRunEntity` + EF migration.
- `ImportNodeWalker` — the write-side mirror of `DynamicExportService`'s tree walker. Walks the
  parsed JSON alongside the `ImportNode` tree and produces parameterized `UPDATE`/`INSERT`
  statements instead of a `SELECT` projection. Emits the `DiffJson` preview in the same pass used
  for the eventual write, so preview and commit can never disagree about what a row means (the
  same unification `BuildExportAsync` already enforces on the export side).
- `ImportWorker : BackgroundService` — polls `inbound/` on a timer (sibling of
  `ExportDefinitionWorker`, not a replacement for anything).
- `ImportDefinitionEndpoints.cs` — CRUD, preview (parse + diff, no write), and the two-step
  release flow (`POST .../release` reusing the shared four-eyes helper).
- `ImportNodeTreeEditor.vue` — structurally the same recursive component as
  `ExportNodeTreeEditor.vue`, built against `ImportNode` instead.
- A review/diff view reusing `ReleaseDialog`'s Operator/Approver form, extended with the
  accepted/rejected row summary.

---

## 6. Open Decisions

All eight resolved. Nothing here blocks starting Slice 1.

1. **Import target** — **Resolved: the live ERP database**, not a generic new target. This is the
   Open Point #6 return-channel, not a separate bidirectional-sync feature.
2. **Trigger** — **Resolved: an inbound staging folder**, mirroring the existing outbound
   folder + manifest pattern, polled by `ImportWorker`. Symmetric air-gap model: nothing reaches
   back over the gap automatically; a human still carries the file.
3. **Approval** — **Resolved: four-eyes required** before any row is committed to the ERP, using
   the same Operator/Approver contract as the export release.
4. **Root-match key** — **Resolved: the same `Guid` correlation key** from Open Point #1. It's
   already the field the connector exports to the vendor, so no new identifier needs to be
   introduced on their side. `RootMatchColumn` stays a per-`ImportDefinition` setting rather than
   a hardcoded assumption, in case a future definition needs something else.
5. **Scope of `AllowedWritableColumns`** — **Resolved: confirmation/status fields on the root
   entity only**, the narrowest option — matching Open Point #6's original framing ("write back CI
   update confirmations"). The exact column names still depend on the actual vendor ICD contract
   (not yet negotiated), but the *category* of what's writable is settled. See the v1 scope note
   in §1.
6. **Partial-file commit policy** — **Resolved: commit accepted rows, quarantine the rest.** A
   single bad row from the vendor doesn't block every good row in the same file; rejected rows are
   reported on the run for manual follow-up rather than failing the whole batch.
7. **GDPR on the inbound side** — **Resolved: not expected, but enforced defensively anyway.**
   Personal data isn't expected in a confirmation/status payload (§5), but the denylist check runs
   on write regardless — the same defence-in-depth posture the export side already applies on
   read, and it costs nothing to check even when it never trips.
8. **Sequence/gap detection** — **Resolved: not built for v1.** The SHA-256 manifest already gives
   per-file integrity; vendor confirmations won't arrive 1:1 per export run, so sequence-number gap
   detection doesn't map cleanly onto this direction the way it does for the outbound side. No
   `SequenceNumber` field on `ImportRunEntity` for v1 (§4) — revisit only if the vendor's actual
   cadence turns out to need it.

---

## 7. Non-Functional Requirements

| Quality | Requirement |
|---|---|
| **Reliability** | Row-level rejection is intentional (Open Decision #6) — an invalid row is excluded from the accepted set before the transaction opens. That accepted set then commits as one all-or-nothing transaction: a technical failure during commit rolls back completely and fails the whole run, never a half-applied write. |
| **Security** | Every endpoint requires authentication; the commit step additionally requires two distinct authenticated users (Operator + Approver), same as export release. Writable columns are allowlisted per definition, not just authenticated-user-gated. |
| **Traceability** | Every run carries `ConfigVersion`, `OperatedBy`, `ApprovedBy`, and a persisted `DiffJson` — "what was written and who approved it" is reconstructable after the fact. |
| **Auditability without a back-channel** | SHA-256 manifest on the inbound file (no sequence number for v1 — Open Decision #8). |
| **Maintainability** | A new source table needs zero new C# types — same OCP property `ExportNode` already has. |

**SOLID, concretely:** SRP — parsing/matching, validation, diff-building, and commit stay separate
concerns in `ImportNodeWalker`, same separation `DynamicExportService` keeps today. OCP — a new
target table needs zero code; a new inbound file format (only JSON is in scope, see Non-Goals)
would be the one thing needing a new parser. DIP — `ImportNodeWalker` takes a already-open
`NpgsqlConnection`/transaction, not a concrete provider, so the four-eyes commit step can run
preview and commit against the same transaction scope without opening two connections.

---

## 8. Documentation Requirements

* Every new type in `Connector.Core.DynamicImport` gets a why-not-what doc comment, matching
  `ExportNode`'s convention.
* [Open Point #6](/planning/open-points.md) gets updated from "Pending" to "Resolved" once this
  ships, with a link back here.
* Changelog gets a "Phase 17 — Inbound JSON import ✅" entry on completion (this doc is Phase 17
  because [Phase 15](/changelog.md) — UI redesign — and [Phase 16](/changelog.md) — nested JSON
  mapping UX — already used those numbers).

---

## 9. Non-Goals

* **No auto-creation of new root-level CIs from vendor data.** Matching against an existing row is
  mandatory at the root — see §1. Only object/array *children* (e.g. a new SerialNumber row under
  an existing CI) may be created, and only when a node's `OnMissingChild = insert`.
* **No live network write-back.** This stays file + air gap + human carry, just the reverse leg —
  not an API the vendor calls directly. (A live API is a materially different security posture and
  out of scope here, same reasoning as export's own Non-Goals.)
* **No scripting/expression engine for transforms.** Reuses the same closed `Transform` enum as
  export — no formula language.
* **No generic bidirectional sync engine.** This is the vendor-confirmation return channel
  (Open Point #6), not a symmetric System A ↔ System B replication tool.
* **No multi-tenant / multi-vendor support.** One vendor return channel, as today's one vendor
  export target.

---

## Implementation status

Nothing started. All eight design decisions in §6 are resolved — nothing blocks starting Slice 1
except the actual vendor ICD contract for the exact `AllowedWritableColumns` column names (§6 #5).
Tracking issue: [#51](https://github.com/mycaravam-crypto/erp-connector/issues/51), with one
sub-issue per slice (#52–58). Suggested slices, mirroring
[Export Definitions 2.0](/pipeline/export-definitions-2.0.md#implementation-status)'s shape —
each roughly PR-sized and independently reviewable:

- [ ] **Slice 1 — Data model + migration.** `ImportNode`/`FieldMapping` reuse, `ImportDefinitionEntity`/`ImportRunEntity`, EF migration. No behavior yet — just the shape.
- [ ] **Slice 2 — `ImportNodeWalker`: parse, match, diff.** Parses inbound JSON against a saved tree, resolves root/child matches, builds `DiffJson`. **No writes** — output is only the computed diff, so this slice is testable and reviewable in complete isolation from the compliance-sensitive commit path.
- [ ] **Slice 3 — Four-eyes commit path.** Applies an approved diff transactionally; `ImportRunEntity` lifecycle; the shared Operator/Approver helper (refactored out of the existing export release endpoint); audit logging.
- [ ] **Slice 4 — `ImportWorker`.** Polls `inbound/`; SHA-256 manifest validation (no sequence check — Open Decision #8); quarantine handling for malformed files.
- [ ] **Slice 5 — API endpoints.** CRUD, preview, release, run history — `ImportDefinitionEndpoints.cs`.
- [ ] **Slice 6 — Frontend.** `ImportNodeTreeEditor.vue`, review/diff UI, Import Definitions list + edit views.
- [ ] **Slice 7 — Docs.** This page's status flip to "shipped," changelog entry, Open Point #6 resolution.

Slice 2 is deliberately ordered before Slice 3 (commit) despite normally being "the same feature"
— being able to see and review a computed diff with zero write capability is a meaningfully lower
-risk deliverable than the commit path, and de-risks the compliance-sensitive part before it's built.

## Related

- [Export Definitions 2.0](/pipeline/export-definitions-2.0.md) — the export-side sibling this
  design mirrors throughout
- [DynamicExportService](/pipeline/dynamic-export-service.md) — the live export pipeline
  `ImportNodeWalker` is modeled on
- [Four-Eyes Release](/operations/four-eyes-release.md) — the approval contract this reuses
- [GDPR Compliance](/operations/gdpr-compliance.md) — the denylist model `AllowedWritableColumns` inverts
- [Open Points](/planning/open-points.md) — Open Point #6, which this document resolves
- [ExportManifest](/domain/export-manifest.md) — the integrity contract the inbound manifest mirrors
