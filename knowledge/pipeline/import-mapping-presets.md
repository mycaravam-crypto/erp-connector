---
type: Pipeline Design
title: Import Mapping Presets from Export Provenance (proposal)
description: Design proposal — tag ExportDefinition/ImportDefinition with a shared, versioned IntegrationKey; if a vendor's inbound file round-trips it, offer to create a new ImportDefinition from the paired export's root-matching (and, best-effort, shared field names). Revised after design review. Slices 1-3 shipped.
resource: src/Connector.Core/DynamicImport/ImportNode.cs
tags: [pipeline, dynamic-mapping, proposal]
timestamp: 2026-09-06T00:00:00Z
---

> **Status: in progress — Slices 1–3 shipped, Slices 4–5 not started.** See
> [Implementation status](#7-implementation-status) for the per-slice checklist. This is not an
> Open Point from the original Technical Concept (see [Open Points](/planning/open-points.md)) —
> it's an internally-raised idea about the Phase 14/17 tree types. Revised once, per
> [Design Review Amendments](#design-review-amendments) below, before any slice began — the same
> "amend before Slice 2" pattern Import Definitions itself used for its own Slice 1b. The export
> side tags its JSON output and the pure suggestion function now exists, but nothing surfaces it
> to an operator yet — the UI (Slice 4) doesn't exist, so this remains invisible until then.

---

## Design Review Amendments

An external review of the first draft (which used `ExportDefinition.Kind` as a bare, unversioned
tag and picked "most recently updated" on a collision) raised eight points before any slice
started. Disposition:

| # | Review point | Verdict | Why |
|---|---|---|---|
| 1 | Rename `Kind` → a name that says "contract identifier," not "category" | **Adopted** — `IntegrationKey` | `Kind` implied a type/taxonomy; the field actually identifies a specific business exchange. §3.1 |
| 2 | Don't put `exportDefinitionId` on the wire | **Adopted** | It's a DB-internal int, non-portable across environments/restores, and the draft already admitted it wasn't used for matching — no reason to expose it externally. §3.2 |
| 3 | Model the exchange as a shared `IntegrationContract` entity, with `ExportDefinition`/`ImportDefinition` both referencing it | **Rejected, outcome adopted differently** | See [§3.5](#35-considered-and-rejected-a-shared-integrationcontract-entity) — the determinism this buys is real and worth having, but a new referenced entity is more structure than a UI-authoring convenience justifies, and it leans toward exactly the "generic bidirectional sync engine" [Import Definitions §9](/pipeline/import-definitions.md#9-non-goals) already rejects. Got the same determinism via plain matching columns on both sides instead — see point 8. |
| 4 | Version the contract explicitly, not just via `Name`/`ConfigVersion` | **Adopted** — `ContractVersion` | Distinguishes "the exchange changed shape" from "someone edited the mapping." §3.1 |
| 5 | Don't silently pick the newest match on a `Kind` collision | **Adopted, strengthened** | Enforced `(IntegrationKey, ContractVersion)` uniqueness at save time instead of a runtime tiebreak — the ambiguity is prevented, not resolved silently. §3.1, §6 |
| 6 | Tie provenance to the actual export run for traceability (e.g. a checksum) | **Partially adopted** | The suggested mechanism (reuse the SHA-256 manifest) doesn't exist on this path: `ExportDefinitionRunEntity`/the JSON `run`/`test` endpoints deliberately skip `FileSystemExportSink`/manifest generation ([Export Definitions 2.0 Slice 3 design note](/pipeline/export-definitions-2.0.md#implementation-status)). Kept `ConfigVersion` — already a standing traceability field for "which mapping produced this" — instead of inventing a new checksum mechanism for a suggestion feature. §3.2 |
| 7 | "Create from export," not "prefill" | **Adopted** | The operator is establishing that two definitions share an exchange, not just saving keystrokes. §3.4, §4 |
| 8 | Make the root correlation key an explicit, known field instead of inferring it by walking the export tree | **Adopted** | New `ExportDefinition.CorrelationKeySourceField`. Turns the one prefill this feature must get right from a heuristic into a stored fact. §3.1, §3.4 |

---

## 0. Origin

Raised as: could an `ExportDefinition`'s JSON output carry metadata identifying *which* export
produced it, so that if a vendor's inbound `ImportEnvelope` carries the same metadata, the
connector can infer "this reply probably corresponds to that export" and use it to save an
operator typing time when building the matching `ImportDefinition` — creating it from the export's
tree instead of starting from a blank `ImportNodeTreeEditor.vue`.

The idea survives contact with the actual v1 shape ([Import Definitions §1](/pipeline/import-definitions.md#1-vision),
[§6](/pipeline/import-definitions.md#6-open-decisions)) with one correction, unchanged by the
review: **the two trees don't share as many fields as "invert the export mapping" implies.**
`AllowedWritableColumns` restricts v1 writes to confirmation/status fields on the root entity —
fields the vendor *adds*, which by definition have no `SourceField` on the export side to invert
from. What the two sides genuinely do share is narrower and more valuable: the root correlation
key itself (Open Decision #4 — the same `Guid` the connector already exports is the field the
import side matches against). That narrower, honest scope is what §3 proposes, now made explicit
rather than inferred (review point 8).

---

## 1. What's already there vs. what's missing

| Needed | Exists today | Gap |
|---|---|---|
| A stable, versioned identifier for "which exchange does this belong to" | No — `ExportDefinition.Id` is a DB-internal int, `Name` is freely renamable, and `JsonExportFormatWriter` (Phase 14's JSON writer) emits only `{schema_version, extracted_at, records}` — no definition identity at all ([export-node.md](/dynamic-export/export-node.md), `DynamicExportService.BuildNestedJsonBytes`) | Two new, paired fields |
| An explicit, non-heuristic root correlation-key field on the export side | No — `ExportDefinition` has `RootTable` but nothing names *which* scalar-field node is the correlation key; today it would have to be found by walking the tree | One new field |
| A place in the inbound file to carry the exchange identity back | `ImportEnvelope` (Open Decision #14) already has a `definition` field, but it's an explicit *routing* selector ("stage this against ImportDefinition X"), not provenance — see the non-goal in §5 | A second, separate, advisory field |
| A way to turn a matched pair into a starting `ImportNode` tree | `ImportNode`/`ExportNode` already share `FieldMapping` verbatim and the same recursive shape ([import-node.md](/dynamic-import/import-node.md)) | A (small) generator function + a UI affordance to offer/accept it |

The legacy single-mapping flow (`ExportMappingConfig`/`SchemaView.vue`) already has a
free-text `ExportJsonWrapperConfig.MetadataFields` mechanism an operator could *manually* stuff a
tag into today — but it's manual, per-run, and that flow has no `ImportDefinition` counterpart at
all (Import Definitions only mirrors the Phase 14 `ExportDefinition`/`ExportNode` types). This
proposal is scoped to `ExportDefinition` ↔ `ImportDefinition` only, same scoping Import
Definitions itself already chose.

---

## 2. Vision

```
ExportDefinition (IntegrationKey = "ci-confirmation", ContractVersion = 1)
  → JSON output tagged { "provenance": { "integrationKey": "ci-confirmation", "contractVersion": 1,
                                          "configVersion": 7 } }
      → vendor round-trips integrationKey/contractVersion (if their ICD is told to) in their reply
          → ImportEnvelope { ..., "provenance": { "integrationKey": "ci-confirmation",
                                                    "contractVersion": 1 }, "records": [...] }
              → operator opens "New Import Definition", pastes/drops a sample file
                  → (integrationKey, contractVersion) matches exactly one enabled ExportDefinition
                    — uniqueness is enforced at save time, so this is a lookup, never a tiebreak
                      → suggestion banner: "Create from export 'CI confirmation export' (v1)?"
                          → accept → RootTable/RootMatchColumn read directly from the paired
                            export's RootTable/CorrelationKeySourceField — no tree walk; any
                            additional field names the sample actually contains that also appear
                            as SourceField/TargetKey pairs in the export tree are offered as
                            candidate scalar-field nodes — nothing is enabled, allowlisted, or
                            saved until the operator reviews it
```

This is a **UI-time authoring convenience**, not a runtime feature. It never runs during
`ImportWorker`'s inbound processing (§5 Non-Goals) and never changes what a saved
`ImportDefinition` does once it exists — a tree created from an export is edited, validated, and
saved through the exact same path as a hand-built one.

---

## 3. Design

### 3.1 `IntegrationKey`/`ContractVersion`/`CorrelationKeySourceField` — explicit, paired fields

```
ExportDefinition
├── ... (unchanged)
├── IntegrationKey            : string?  (new — optional; a short, stable slug identifying the
│                                          business exchange, e.g. "ci-confirmation"; independent
│                                          of Name (renamable) and Id (DB-internal, not portable
│                                          across environments/restores) so it survives both)
├── ContractVersion           : int?     (new — versions the exchange itself, separately from
│                                          ConfigVersion, which just versions this one mapping's
│                                          edits. "ci-confirmation" v1 → v2 is a deliberate,
│                                          reviewed change to what the exchange means; a mapping
│                                          tweak within v1 is not)
└── CorrelationKeySourceField : string?  (new — names which enabled root-level scalar-field
                                           node's SourceField is the correlation key, e.g. "guid".
                                           Purely advisory metadata for this feature — it does not
                                           change query building or output — but it turns the
                                           deterministic half of §3.4's suggestion from a tree walk
                                           into a stored fact)

ImportDefinition
├── ... (unchanged)
├── IntegrationKey  : string?  (new — same meaning as above)
└── ContractVersion : int?     (new — same meaning as above)
```

`IntegrationKey` is free-text, not an enum — one more closed list this codebase would otherwise
have to maintain in lockstep with vendor ICD negotiations it doesn't control (same reasoning
[Export Definitions 2.0 §10](/pipeline/export-definitions-2.0.md#10-non-goals) gives for rejecting
a scripting/expression engine: don't build a taxonomy for something that's inherently open-ended).

**Uniqueness, enforced, not advisory:** the save-time validator rejects a second *enabled*
`ExportDefinition` sharing an `(IntegrationKey, ContractVersion)` pair already used by another
enabled one (same rule applied independently among `ImportDefinition`s). `IntegrationKey` and
`ContractVersion` are set together or not at all — a version with no key, or vice versa, is
rejected at save time. This removes the collision case entirely: matching is always "find the one
enabled definition with this pair," never a heuristic tiebreak.

### 3.2 Carrying it into the JSON output

`JsonExportFormatWriter` (the only Phase 14 JSON writer — CSV/Excel have no natural place for
structured metadata and are out of scope, §5) gains one optional top-level key when
`IntegrationKey` is set:

```json
{
  "schema_version": "1",
  "extracted_at": "2026-09-06T00:00:00Z",
  "provenance": { "integrationKey": "ci-confirmation", "contractVersion": 1, "configVersion": 7 },
  "records": [ ... ]
}
```

Only `integrationKey`/`contractVersion` are ever matched on. `configVersion` rides along purely
for an operator-facing tooltip ("generated by config version 7") — it's already an established
per-run traceability field ([Export Definitions 2.0 §8](/pipeline/export-definitions-2.0.md#8-non-functional-requirements)),
not a new identifier. No internal database id (`ExportDefinition.Id`, any run id) is ever placed
on the wire — this is an external interchange payload, and an internal primary key has no meaning
outside this one connector instance. Omitted entirely when `IntegrationKey` is unset — zero shape
change for every export that doesn't opt in, and no `schemaVersion` bump needed (purely additive
to a key space nothing currently reads).

### 3.3 Reading it back on the import side

`ImportEnvelope` gains one more optional, informational field, parallel to `sourceSystem`/
`correlationId` (Open Decision #14) rather than folded into `definition`:

```
ImportEnvelope
├── schemaVersion, definition, generatedAt, sourceSystem, correlationId, records[]   (unchanged)
└── provenance : { integrationKey: string, contractVersion: int }?   (new, optional — purely
                                                                       advisory, see §5)
```

`ImportNodeWalker`/`ImportWorker` **never read this field** — it doesn't exist to them. It's
surfaced only by the frontend's `ImportDefinitionPreviewPanel.vue` (already the place an operator
pastes a sample inbound file) and the "New Import Definition" flow, both UI-only consumers.

### 3.4 The suggestion itself

A new pure function, `ImportMappingSuggestion.SuggestFrom(ExportDefinition, sampleRecordShape)`:

1. Look up the enabled `ExportDefinition` whose `(IntegrationKey, ContractVersion)` matches the
   sample's `provenance` pair. Thanks to §3.1's uniqueness constraint this is a lookup, not a
   ranking — at most one result. No match → no suggestion, silently — this must degrade to exactly
   today's blank-tree experience, never an error.
2. Read `RootTable`/`CorrelationKeySourceField` directly off the matched `ExportDefinition` and
   set `ImportDefinition.RootTable`/`RootMatchColumn` from them. Deterministic — no tree
   inspection. This is the one prefill that's always semantically sound, because both sides are
   contractually the same field already (Open Decision #4).
3. Best-effort only: for every other enabled root-level `scalar-field` node in the export tree, if
   the sample inbound record actually contains a JSON key equal to that node's `TargetKey`, offer
   it as a candidate `ImportNode` (`SourceKey = TargetKey`, `TargetColumn = SourceField`) — but
   **unchecked/disabled by default**, never auto-enabled, since matching a key name back to the
   same underlying column is a name-collision heuristic, not a proven contract (§0's correction).
   The operator still must enable each one, and each still goes through the existing
   `AllowedWritableColumns` schema-aware validator (Open Decision #9) before it can ever be saved
   as writable. This step is explicitly secondary to step 2 — a convenience on top of the
   deterministic prefill, not something the feature depends on to be useful.
4. Nested object/array export children are not walked — v1's `AllowedWritableColumns` scope is
   root-only (Open Decision #5) and `OnMissingChild = insert` stays rejected at save time (Open
   Decision #15, unchanged by this proposal), so there is nothing a nested suggestion could
   usefully prefill yet.

### 3.5 Considered and rejected: a shared `IntegrationContract` entity

An alternative raised in review: introduce a persisted `IntegrationContract` (key, version,
root table, correlation key) that both `ExportDefinition` and `ImportDefinition` reference by FK,
rather than each carrying its own copy of `IntegrationKey`/`ContractVersion`. It would remove the
duplication and make "these two definitions are paired" a first-class, queryable relationship.

Not adopted, for two reasons specific to this codebase's existing choices, not just taste:

* **It's more structure than a UI-authoring convenience needs.** A new entity means a migration,
  an FK, and lifecycle questions this feature has no actual requirement to answer yet — what
  happens to the contract row when the last definition referencing it is deleted; whether a
  contract can have more than one enabled `ImportDefinition` at once (e.g. during a vendor
  migration from v1 to v2). Plain matching columns answer "does this export and this import agree
  they're the same exchange" exactly as well, at save-time-validation cost instead of
  schema-and-lifecycle cost.
* **It nudges toward a shape this codebase has twice deliberately avoided.** [Import Definitions
  §9](/pipeline/import-definitions.md#9-non-goals) rejects "a generic bidirectional sync engine,"
  and [`ImportNode`'s own doc](/dynamic-import/import-node.md) explains why it doesn't merge into
  `ExportNode` despite being structurally identical: "the two trees flow in opposite directions...
  Forcing them into one type would mean nullable fields that are meaningless half the time — worse
  than two small, honest types." A shared parent entity for `ExportDefinition`/`ImportDefinition`
  is the same move one level up: it starts modeling the exchange as one bidirectional thing with
  two sides, rather than two independently-scoped, independently-lifecycled definitions that
  happen to agree on a key. The determinism review point 8 actually wanted (§3.4 step 2) doesn't
  require that — it only requires the export side to state its own correlation field explicitly,
  which `CorrelationKeySourceField` does without a new entity.

If a real future requirement needs the contract to be a first-class, independently-managed thing
(e.g. a UI page listing "integrations" rather than exports and imports separately), this can be
revisited then — nothing in §3.1's plain-column design forecloses adding a proper entity later and
migrating the columns onto it.

---

## 4. Reused vs. New

**Reused as-is:**
- `ImportNode`/`FieldMapping` shape — the suggestion produces ordinary `ImportNode` values, edited
  in the existing `ImportNodeTreeEditor.vue`, nothing new to render.
- `AllowedWritableColumns` schema-aware validator (Open Decision #9) and the identifier-safety /
  GDPR-denylist checks — a suggested node is exactly as untrusted as a hand-typed one until it
  passes save-time validation.
- `ImportDefinitionPreviewPanel.vue`'s existing "paste a sample inbound file" affordance — the
  natural place to also surface the suggestion banner, no new entry point needed.
- `ConfigVersion` — reused for the wire payload's operator-facing traceability field (§3.2)
  instead of inventing a new one.

**New:**
- `ExportDefinition.IntegrationKey`/`ContractVersion`/`CorrelationKeySourceField` and
  `ImportDefinition.IntegrationKey`/`ContractVersion` columns + EF migration (all nullable, no
  backfill needed — every existing row simply has none of this set, exactly today's behavior).
- The `(IntegrationKey, ContractVersion)` uniqueness-among-enabled-rows check, added to each
  definition type's existing save-time validator.
- `provenance` key in `JsonExportFormatWriter`'s output and in the `ImportEnvelope` parser
  (additive, optional, ignored by every writer/parser that predates it).
- `ImportMappingSuggestion.SuggestFrom` (`Connector.Core.DynamicImport`) — pure function, no I/O
  beyond the two already-loaded definition rows; easy to unit test in isolation the same way
  `ImportNodeWalker`'s diff logic is.
- A suggestion banner + "Create from export" action in the New/Edit `ImportDefinition` UI, offered
  alongside "Start blank."

---

## 5. Non-Goals (the guardrails this proposal must not cross)

* **Never a second routing mechanism.** `ImportEnvelope.definition` (Open Decision #14) stays the
  only field that decides which `ImportDefinition` a real inbound file is staged against.
  `provenance` is read by the authoring UI only, never by `ImportWorker`/`ImportNodeWalker` at
  processing time — conflating the two would mean a file's *processing* target could shift based
  on a field nobody is required to keep truthful.
* **Never a trust or bypass mechanism.** A matching `(integrationKey, contractVersion)` pair says
  "these two definitions were designed to pair," not "this specific file is safe to write." Every
  suggested node still needs `AllowedWritableColumns` + schema-aware validation (Open Decision #9)
  before it can be saved as writable, and every staged run still needs four-eyes review (Open
  Decision #3) before it touches the ERP. This proposal only ever saves an operator typing at
  authoring time.
* **No CSV/Excel support.** Structured provenance metadata has no natural home in a flat row
  format; `ImportEnvelope` itself is already JSON-only, so this stays JSON-only symmetrically.
* **No change to the legacy single-mapping flow.** Scoped to `ExportDefinition`/`ImportDefinition`
  only, matching how Import Definitions itself was scoped (§2 of that doc).
* **No enum/taxonomy for `IntegrationKey`.** Free text, operator-owned, exactly as unconstrained as
  `Name` — see §3.1.
* **No shared `IntegrationContract` entity** — see §3.5 for why, and what would have to change
  before revisiting it.

---

## 6. Open Decisions

1. **Where does the suggestion banner live?** `ImportDefinitionPreviewPanel.vue` (reuse the
   existing paste-a-sample flow) vs. a dedicated step in "New Import Definition." *Leaning:* reuse
   the preview panel — it already parses a sample file and is the one place both a real inbound
   file and a hypothetical "does my export/import pair make sense" check would look.
2. **Is the paired `IntegrationKey`/`ContractVersion` shown read-only on the Import Definition
   itself after creation**, purely for an operator auditing "why does this definition's tree look
   like that" months later? Cheap to add given §3.1 already stores it on `ImportDefinition`; the
   only question is whether the edit view surfaces it.
3. **Vendor ICD dependency.** The whole mechanism is inert until the vendor's ICD is told to echo
   `provenance.integrationKey`/`contractVersion` back — same posture as Import Definitions' own
   confirmation-field names (Open Decision #5): the *shape* can be built now, but it only starts
   firing once negotiated. Not a blocker to building this — the export side gets the tag
   regardless, and the import side's suggestion simply never triggers until a vendor file actually
   carries it.

(The review's point about picking among multiple `Kind`-matching definitions no longer applies —
§3.1's uniqueness constraint prevents the collision outright rather than needing a tiebreak
decision.)

---

## 7. Implementation status

5 slices, each independently shippable and leaving the system in a fully working state with the
feature simply not yet visible until Slice 4. Slice 1 came first since everything else depends on
the `IntegrationKey`/`ContractVersion` shape being settled. Tracking issue: #73, with one
sub-issue per slice (#74–78).

- [x] **Slice 1 — Data model + migration.** New nullable `ExportDefinition.IntegrationKey`/
      `ContractVersion`/`CorrelationKeySourceField` and `ImportDefinition.IntegrationKey`/
      `ContractVersion` columns; EF migration, no backfill. Save-time validator addition on each
      definition type independently: `IntegrationKey`/`ContractVersion` must be set together or
      not at all, and at most one *enabled* definition of a given type may ever claim a given
      pair — enforced in `ValidateRequestAsync` (create/update) and the `.../enable` endpoint,
      since either path can turn a definition enabled. `CorrelationKeySourceField` is validated
      only for identifier-safety at this slice; no query-building/tree-walk behavior changes.
      Shipped in #74.
- [x] **Slice 2 — Export-side wiring.** `JsonExportFormatWriter` emits an optional top-level
      `provenance: { integrationKey, contractVersion, configVersion }` key when
      `ExportDefinitionEntity.IntegrationKey` is set — omitted entirely otherwise, byte-identical
      to pre-Slice-2 output. New `ExportProvenance` record and an optional parameter threaded
      through `IExportFormatWriter.Write`/`DynamicExportService.BuildExportNodeAsync`/
      `BuildNestedJsonBytes`; `ExportDefinitionRunner.ExecuteAsync` builds it from the running
      `ExportDefinitionEntity`. CSV/Excel writers accept (and ignore) the same parameter rather
      than forking the interface — no behavior change, per the doc's Non-Goals (§5). No internal
      database id placed on the wire. Shipped in #75.
- [x] **Slice 3 — Import-side wiring.** `ImportNodeWalker.ParseRecords` already ignores every
      envelope key besides `schemaVersion`/`records`, so an inbound file's optional
      `provenance: { integrationKey, contractVersion }` block needed no parser code change to be
      "accepted" — added a regression test (`WalkAsync_ProvenanceBlockOnEnvelope_HasNoEffectOnTheWalk`)
      proving the walk is byte-identical with or without one, guarding that invariant against ever
      accidentally changing. New pure `ImportMappingSuggestion.SuggestFrom` (`Connector.Core.DynamicImport`)
      plus its `ExportDefinitionShape`/`ImportSampleShape`/`ImportMappingSuggestionResult` input/output
      shapes — Core-level types rather than referencing the `Connector.Infrastructure` EF entities
      directly, since Core has no dependency on Infrastructure. No I/O; unit-testable in complete
      isolation (exact match, no match, disabled-export/no-correlation-field no-ops, ambiguous-candidate
      safety, best-effort field candidates, nested children never walked). No UI or API endpoint yet —
      deliberately reviewable before any frontend surfaces it. Shipped in #76.
- [x] **Slice 4 — Frontend.** New `POST /api/import-definitions/suggest-from-export` endpoint
      (`ImportDefinitionEndpoints.BuildSuggestionAsync`) parses a pasted sample `ImportEnvelope`'s
      `provenance` block and first record's field names, then delegates to Slice 3's
      `ImportMappingSuggestion.SuggestFrom` against every enabled, tagged `ExportDefinition` — degrading to
      `null` (200 OK) for anything short of an exact match, never an error. Resolves the matched export's
      own `TargetKey` for the correlation field server-side (`RootMatchSourceKey`) so the suggested tree's
      `SourceKey` reads the real inbound JSON key rather than assuming it matches the column name. New
      `ImportMappingSuggestionPanel.vue` — settling Open Decision #1 as a **dedicated step** in the New
      Import Definition flow rather than reusing `ImportDefinitionPreviewPanel.vue` verbatim: that panel
      drives `.../{id}/preview` against an already-saved definition's live ERP connection, which doesn't
      exist yet at this point in the flow. Mirrors its paste-JSON UX instead. On accept,
      `applyImportMappingSuggestion` (`importNodeBuilders.ts`) builds the same disabled-scalar-fields-per-
      column starting tree a manual root-table pick gives, pre-enables and correctly keys the deterministic
      root match field, and pre-fills (still unchecked) every best-effort candidate's `SourceKey` — nothing
      is auto-enabled. Settled Open Decision #2: the paired `IntegrationKey`/`ContractVersion` is shown
      read-only on `ImportDefinitionBasicFields.vue` once set, cheap given Slice 1 already stores it.
      **Deviation:** the proposal's §4 "New" list didn't call out that no UI existed anywhere for an
      operator to actually set `ExportDefinition.IntegrationKey`/`ContractVersion`/
      `CorrelationKeySourceField` (Slice 2 was backend-only) — without it the feature would have shipped
      with no way to tag an export at all. Added a small "Integration tagging (optional)" section to
      `ExportDefinitionBasicFields.vue` in this slice rather than deferring it, since this slice's own
      verification step requires being able to "create an export with `IntegrationKey` set." Shipped in #77.
- [ ] **Slice 5 — Docs.** Status flip, changelog.

## Related

- [Import Definitions](/pipeline/import-definitions.md) — Open Decision #14 (`ImportEnvelope`
  shape) and #5/#9 (`AllowedWritableColumns`), both load-bearing constraints on this proposal, and
  §9 (Non-Goals), which §3.5's rejection of a shared entity is anchored to
- [Export Definitions 2.0](/pipeline/export-definitions-2.0.md) — `ExportDefinition`/`ExportNode`,
  which `IntegrationKey`/`ContractVersion`/`CorrelationKeySourceField` would extend
- [ImportNode Tree](/dynamic-import/import-node.md) / [ExportNode Tree](/dynamic-export/export-node.md) —
  the shared `FieldMapping` shape the suggestion generator reuses verbatim, and the "two small
  honest types" precedent §3.5 follows
- [Open Points](/planning/open-points.md) — Open Point #6/#8, the vendor-ICD negotiations this
  proposal's Open Decision #3 depends on
