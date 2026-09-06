---
type: Pipeline Design
title: Import Mapping Presets from Export Provenance (proposal)
description: Design proposal — tag a JSON ExportDefinition's output with a stable Kind; if a vendor's inbound file round-trips the same tag, offer to prefill a new ImportDefinition's root-matching (and, best-effort, shared field names) from the paired export. Not started.
resource: src/Connector.Core/DynamicImport/ImportNode.cs
tags: [pipeline, dynamic-mapping, proposal]
timestamp: 2026-09-06T00:00:00Z
---

> **Status: proposal, design only.** No slice has started. This is not an Open Point from the
> original Technical Concept (see [Open Points](/planning/open-points.md)) — it's an
> internally-raised idea about the Phase 14/17 tree types, written up before deciding whether to
> schedule it. Nothing here changes shipped behavior.

---

## 0. Origin

Raised as: could an `ExportDefinition`'s JSON output carry metadata identifying *which* export
produced it, so that if a vendor's inbound `ImportEnvelope` carries the same metadata, the
connector can infer "this reply probably corresponds to that export" and use it to save an
operator typing time when building the matching `ImportDefinition` — prefilling its mapping from
the export's tree instead of starting from a blank `ImportNodeTreeEditor.vue`.

The idea survives contact with the actual v1 shape ([Import Definitions §1](/pipeline/import-definitions.md#1-vision),
[§5](/pipeline/import-definitions.md#6-open-decisions)) with one correction: **the two trees don't
share as many fields as "invert the export mapping" implies.** `AllowedWritableColumns` restricts
v1 writes to confirmation/status fields on the root entity — fields the vendor *adds*, which by
definition have no `SourceField` on the export side to invert from. What the two sides genuinely
do share is narrower and more valuable: the root correlation key itself (Open Decision #4 — the
same `Guid` the connector already exports is the field the import side matches against). That
narrower, honest scope is what §3 below actually proposes.

---

## 1. What's already there vs. what's missing

| Needed | Exists today | Gap |
|---|---|---|
| A stable identifier for "which export produced this file" | No — `ExportDefinition.Id` is a DB-internal int, `Name` is freely renamable, and `JsonExportFormatWriter` (Phase 14's JSON writer) emits only `{schema_version, extracted_at, records}` — no definition identity at all ([export-node.md](/dynamic-export/export-node.md), `DynamicExportService.BuildNestedJsonBytes`) | A new, stable, human-assigned tag |
| A place in the inbound file to carry that tag back | `ImportEnvelope` (Open Decision #14) already has a `definition` field, but it's an explicit *routing* selector ("stage this against ImportDefinition X"), not provenance — see the non-goal in §5 | A second, separate, advisory field |
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
ExportDefinition (Kind = "ci-confirmation-v1")
  → JSON output tagged { "exportProvenance": { "kind": "ci-confirmation-v1", "rootMatchField": "guid" } }
      → vendor round-trips the tag (if their ICD is told to) in their reply
          → ImportEnvelope { ..., "exportProvenance": { "kind": "ci-confirmation-v1" }, "records": [...] }
              → operator opens "New Import Definition", pastes/drops a sample file
                  → tag matches a known enabled ExportDefinition.Kind
                      → suggestion banner: "Prefill from Export Definition 'CI confirmation export'?"
                          → accept → RootTable/RootMatchColumn prefilled from the paired export's
                            root correlation-key node; any additional field names the sample
                            actually contains that also appear as SourceField/TargetKey pairs in
                            the export tree are offered as candidate scalar-field nodes — nothing
                            is enabled, allowlisted, or saved until the operator reviews it
```

This is a **UI-time authoring convenience**, not a runtime feature. It never runs during
`ImportWorker`'s inbound processing (§5 Non-Goals) and never changes what a saved
`ImportDefinition` does once it exists — a prefilled tree is edited, validated, and saved through
the exact same path as a hand-built one.

---

## 3. Design

### 3.1 `ExportDefinition.Kind` — a new, stable, operator-assigned tag

```
ExportDefinition
├── ... (unchanged)
└── Kind          : string?     (new — optional; a short, stable slug the operator sets once,
                                  e.g. "ci-confirmation-v1"; deliberately independent of Name
                                  (renamable) and Id (DB-internal, not portable across
                                  environments/restores) so the tag survives both)
```

`Kind` is free-text, not an enum — one more closed list this codebase would otherwise have to
maintain in lockstep with vendor ICD negotiations it doesn't control (same reasoning
[Export Definitions 2.0 §10](/pipeline/export-definitions-2.0.md#10-non-goals) gives for rejecting
a scripting/expression engine: don't build a taxonomy for something that's inherently open-ended).
Uniqueness is advisory, not enforced — two definitions sharing a `Kind` just means the suggestion
picks the most-recently-updated enabled one, never a hard save-time error.

### 3.2 Carrying it into the JSON output

`JsonExportFormatWriter` (the only Phase 14 JSON writer — CSV/Excel have no natural place for
structured metadata and are out of scope, §5) gains one optional top-level key when `Kind` is set:

```json
{
  "schema_version": "1",
  "extracted_at": "2026-09-06T00:00:00Z",
  "exportProvenance": { "kind": "ci-confirmation-v1", "exportDefinitionId": 42, "configVersion": 7 },
  "records": [ ... ]
}
```

`exportDefinitionId`/`configVersion` ride along for traceability (matching the existing
`ConfigVersion`-on-every-run convention, [Export Definitions 2.0 §8](/pipeline/export-definitions-2.0.md#8-non-functional-requirements))
but are never matched on — only `kind` is, since the id isn't portable and the version changes on
every edit. Omitted entirely when `Kind` is unset — zero shape change for every export that
doesn't opt in, and no `schemaVersion` bump needed (this is purely additive to a key space nothing
currently reads).

### 3.3 Reading it back on the import side

`ImportEnvelope` gains one more optional, informational field, parallel to `sourceSystem`/
`correlationId` (Open Decision #14) rather than folded into `definition`:

```
ImportEnvelope
├── schemaVersion, definition, generatedAt, sourceSystem, correlationId, records[]   (unchanged)
└── exportProvenance : { kind: string }?   (new, optional — purely advisory, see §5)
```

`ImportNodeWalker`/`ImportWorker` **never read this field** — it doesn't exist to them. It's
surfaced only by the frontend's `ImportDefinitionPreviewPanel.vue` (already the place an operator
pastes a sample inbound file) and the "New Import Definition" flow, both UI-only consumers.

### 3.4 The suggestion itself

A new pure function, `ImportMappingSuggestion.SuggestFrom(ExportDefinition, sampleRecordShape)`:

1. Look up the enabled `ExportDefinition` whose `Kind` matches the sample's `exportProvenance.kind`
   (most-recently-updated wins on a tie, §3.1). No match → no suggestion, silently — this must
   degrade to exactly today's blank-tree experience, never an error.
2. Walk the matched export's `RootNode.Children` for the one `scalar-field` node whose
   `SourceField` equals that export's own root table's correlation-key column (the same `Guid`
   Open Decision #4 already names) and prefill `ImportDefinition.RootTable`/`RootMatchColumn` from
   it. This is the one prefill that's always semantically sound, because both sides are
   contractually the same field already.
3. Best-effort only: for every other enabled root-level `scalar-field` node in the export tree,
   if the sample inbound record actually contains a JSON key equal to that node's `TargetKey`,
   offer it as a candidate `ImportNode` (`SourceKey = TargetKey`, `TargetColumn = SourceField`) —
   but **unchecked/disabled by default**, never auto-enabled, since matching a key name back to
   the same underlying column is a name-collision heuristic, not a proven contract (§0's
   correction). The operator still must enable each one, and each still goes through the existing
   `AllowedWritableColumns` schema-aware validator (Open Decision #9) before it can ever be saved
   as writable.
4. Nested object/array export children are not walked — v1's `AllowedWritableColumns` scope is
   root-only (Open Decision #5) and `OnMissingChild = insert` stays rejected at save time (Open
   Decision #15, unchanged by this proposal), so there is nothing a nested suggestion could
   usefully prefill yet.

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

**New:**
- `ExportDefinition.Kind` column + EF migration (nullable, no backfill needed — every existing row
  simply has no tag, exactly today's behavior).
- `exportProvenance` key in `JsonExportFormatWriter`'s output and in the `ImportEnvelope` parser
  (additive, optional, ignored by every writer/parser that predates it).
- `ImportMappingSuggestion.SuggestFrom` (`Connector.Core.DynamicImport`) — pure function, no I/O
  beyond the two already-loaded definition rows; easy to unit test in isolation the same way
  `ImportNodeWalker`'s diff logic is.
- A suggestion banner + "prefill" action in the New/Edit `ImportDefinition` UI.

---

## 5. Non-Goals (the guardrails this proposal must not cross)

* **Never a second routing mechanism.** `ImportEnvelope.definition` (Open Decision #14) stays the
  only field that decides which `ImportDefinition` a real inbound file is staged against.
  `exportProvenance` is read by the authoring UI only, never by `ImportWorker`/`ImportNodeWalker`
  at processing time — conflating the two would mean a file's *processing* target could shift
  based on a field nobody is required to keep truthful.
* **Never a trust or bypass mechanism.** A matching `kind` tag says "these two definitions were
  probably designed to pair," not "this specific file is safe to write." Every prefilled node
  still needs `AllowedWritableColumns` + schema-aware validation (Open Decision #9) before it can
  be saved as writable, and every staged run still needs four-eyes review (Open Decision #3)
  before it touches the ERP. This proposal only ever saves an operator typing at authoring time.
* **No CSV/Excel support.** Structured provenance metadata has no natural home in a flat row
  format; `ImportEnvelope` itself is already JSON-only, so this stays JSON-only symmetrically.
* **No change to the legacy single-mapping flow.** Scoped to `ExportDefinition`/`ImportDefinition`
  only, matching how Import Definitions itself was scoped (§2 of that doc).
* **No enum/taxonomy for `Kind`.** Free text, operator-owned, exactly as unconstrained as `Name` —
  see §3.1.

---

## 6. Open Decisions

1. **Where does the suggestion banner live?** `ImportDefinitionPreviewPanel.vue` (reuse the
   existing paste-a-sample flow) vs. a dedicated step in "New Import Definition". *Leaning:*
   reuse the preview panel — it already parses a sample file and is the one place both a real
   inbound file and a hypothetical "does my export/import pair make sense" check would look.
2. **Is `Kind` visible/editable from the Import Definition side at all**, e.g. an
   `ImportDefinition.PairedExportKind` field purely for the operator's own bookkeeping (a label,
   not a lookup key)? Would help someone auditing "why does this definition's tree look like
   that" months later, at the cost of one more optional field to keep meaningful.
3. **Multiple enabled `ExportDefinition`s sharing a `Kind`.** §3.1 picks most-recently-updated as a
   tiebreaker; worth confirming that's actually the least-surprising choice, versus listing all
   matches and letting the operator pick.
4. **Vendor ICD dependency.** The whole mechanism is inert until the vendor's ICD is told to echo
   `exportProvenance.kind` back — same posture as Import Definitions' own confirmation-field names
   (Open Decision #5): the *shape* can be built now, but it only starts firing once negotiated.
   Not a blocker to building this — the export side gets the tag regardless, and the import side's
   suggestion simply never triggers until a vendor file actually carries it.

---

## 7. Implementation status

Not started — proposal only, pending a decision to schedule it. If approved, expect it to follow
the same slice shape as Phases 14/17 (data model + migration → writer/parser plumbing →
suggestion function + tests → UI → docs), each independently shippable and each leaving the
system in a fully working state with the feature simply not yet visible.

## Related

- [Import Definitions](/pipeline/import-definitions.md) — Open Decision #14 (`ImportEnvelope`
  shape) and #5/#9 (`AllowedWritableColumns`), both load-bearing constraints on this proposal
- [Export Definitions 2.0](/pipeline/export-definitions-2.0.md) — `ExportDefinition`/`ExportNode`,
  which `Kind` would extend
- [ImportNode Tree](/dynamic-import/import-node.md) / [ExportNode Tree](/dynamic-export/export-node.md) —
  the shared `FieldMapping` shape the suggestion generator reuses verbatim
- [Open Points](/planning/open-points.md) — Open Point #6/#8, the vendor-ICD negotiations this
  proposal's Open Decision #4 depends on
