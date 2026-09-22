---
type: Pipeline Design
title: Import Mapping Presets from Export Provenance
description: Tag an ExportDefinition/ImportDefinition with a shared, versioned IntegrationKey; if a vendor's inbound file round-trips it, offer to create a new ImportDefinition from the paired export's root-matching (and, best-effort, shared field names).
resource: src/Connector.Core/DynamicImport/ImportNode.cs
tags: [pipeline, dynamic-mapping]
timestamp: 2026-09-06T00:00:00Z
---

An `ExportDefinition` and the `ImportDefinition` that consumes the vendor's reply to it are, today,
two independently-authored definitions with nothing linking them. This mechanism tags both sides of
one business exchange with a shared, versioned identifier, and — if the vendor's ICD is told to
echo that identifier back — lets the "New Import Definition" flow offer to prefill a starting tree
from the paired export, instead of the operator building it from a blank editor. This is a **UI-time
authoring convenience**, not a runtime feature: it never runs during `ImportWorker`'s inbound
processing, and never changes what a saved `ImportDefinition` does once it exists.

The two trees don't share as many fields as "invert the export mapping" might imply.
`AllowedWritableColumns` restricts writes to confirmation/status fields on the root entity —
fields the vendor *adds*, which by definition have no `SourceField` on the export side to invert
from. What the two sides genuinely share is narrower and more valuable: the root correlation key
itself (the same `Guid` the connector already exports is the field the import side matches
against). That narrower, honest scope is what §2 below describes.

## 1. What's needed

| Needed | Why it's missing without this |
|---|---|
| A stable, versioned identifier for "which exchange does this belong to" | `ExportDefinition.Id` is a DB-internal int, `Name` is freely renamable, and the JSON writer emits only `{schema_version, extracted_at, records}` — no definition identity at all |
| An explicit, non-heuristic root correlation-key field on the export side | `ExportDefinition` has `RootTable` but nothing names *which* scalar-field node is the correlation key |
| A place in the inbound file to carry the exchange identity back | `ImportEnvelope`'s `definition` field is an explicit *routing* selector ("stage this against ImportDefinition X"), not provenance — see §5 |
| A way to turn a matched pair into a starting `ImportNode` tree | `ImportNode`/`ExportNode` already share `FieldMapping` verbatim and the same recursive shape — needs a generator function + UI affordance |

This is scoped to `ExportDefinition` ↔ `ImportDefinition` only — the legacy single-mapping flow has
no `ImportDefinition` counterpart at all.

## 2. Design

### 2.1 `IntegrationKey`/`ContractVersion`/`CorrelationKeySourceField`

```
ExportDefinition
├── ... (unchanged)
├── IntegrationKey            : string?  (optional; a short, stable slug identifying the business
│                                          exchange, e.g. "ci-confirmation"; independent of Name
│                                          (renamable) and Id (DB-internal, not portable across
│                                          environments/restores) so it survives both)
├── ContractVersion           : int?     (versions the exchange itself, separately from
│                                          ConfigVersion, which just versions this one mapping's
│                                          edits. "ci-confirmation" v1 → v2 is a deliberate,
│                                          reviewed change to what the exchange means; a mapping
│                                          tweak within v1 is not)
└── CorrelationKeySourceField : string?  (names which enabled root-level scalar-field node's
                                           SourceField is the correlation key, e.g. "guid". Purely
                                           advisory metadata — it does not change query building or
                                           output — but it turns the deterministic half of the
                                           suggestion in §2.4 from a tree walk into a stored fact)

ImportDefinition
├── ... (unchanged)
├── IntegrationKey  : string?  (same meaning as above)
└── ContractVersion : int?     (same meaning as above)
```

`IntegrationKey` is free-text, not an enum — one more closed list this codebase would otherwise
have to maintain in lockstep with vendor ICD negotiations it doesn't control.

**Uniqueness is enforced, not advisory:** the save-time validator rejects a second *enabled*
`ExportDefinition` sharing an `(IntegrationKey, ContractVersion)` pair already used by another
enabled one (the same rule applies independently among `ImportDefinition`s). `IntegrationKey` and
`ContractVersion` are set together or not at all — a version with no key, or vice versa, is
rejected at save time. This means matching is always "find the one enabled definition with this
pair," never a heuristic tiebreak.

### 2.2 Carrying it into the JSON output

`JsonExportFormatWriter` (the only JSON writer — CSV/Excel have no natural place for structured
metadata and are out of scope, §5) gains one optional top-level key when `IntegrationKey` is set:

```json
{
  "schema_version": "1",
  "extracted_at": "2026-09-06T00:00:00Z",
  "provenance": { "integrationKey": "ci-confirmation", "contractVersion": 1, "configVersion": 7 },
  "records": [ ... ]
}
```

Only `integrationKey`/`contractVersion` are ever matched on. `configVersion` rides along purely for
an operator-facing tooltip ("generated by config version 7") — an already-established per-run
traceability field, not a new identifier. No internal database id (`ExportDefinition.Id`, any run
id) is ever placed on the wire — this is an external interchange payload, and an internal primary
key has no meaning outside this one connector instance. Omitted entirely when `IntegrationKey` is
unset — zero shape change for every export that doesn't opt in.

### 2.3 Reading it back on the import side

`ImportEnvelope` gains one more optional, informational field, parallel to `sourceSystem`/
`correlationId` rather than folded into `definition`:

```
ImportEnvelope
├── schemaVersion, definition, generatedAt, sourceSystem, correlationId, records[]   (unchanged)
└── provenance : { integrationKey: string, contractVersion: int }?   (optional — purely advisory)
```

`ImportNodeWalker`/`ImportWorker` **never read this field** — it doesn't exist to them. It's
surfaced only by the frontend's `ImportDefinitionPreviewPanel.vue` and the "New Import Definition"
flow, both UI-only consumers.

### 2.4 The suggestion itself

A pure function, `ImportMappingSuggestion.SuggestFrom(ExportDefinition, sampleRecordShape)`:

1. Look up the enabled `ExportDefinition` whose `(IntegrationKey, ContractVersion)` matches the
   sample's `provenance` pair. Thanks to §2.1's uniqueness constraint this is a lookup, not a
   ranking — at most one result. No match → no suggestion, silently — this degrades to exactly
   today's blank-tree experience, never an error.
2. Read `RootTable`/`CorrelationKeySourceField` directly off the matched `ExportDefinition` and set
   `ImportDefinition.RootTable`/`RootMatchColumn` from them. Deterministic — no tree inspection.
   This is the one prefill that's always semantically sound, because both sides are contractually
   the same field already.
3. Best-effort only: for every other enabled root-level `scalar-field` node in the export tree, if
   the sample inbound record actually contains a JSON key equal to that node's `TargetKey`, offer
   it as a candidate `ImportNode` (`SourceKey = TargetKey`, `TargetColumn = SourceField`) — but
   **unchecked/disabled by default**, never auto-enabled, since matching a key name back to the
   same underlying column is a name-collision heuristic, not a proven contract. The operator still
   must enable each one, and each still goes through the existing `AllowedWritableColumns`
   schema-aware validator before it can ever be saved as writable.
4. Nested object/array export children are not walked — writable scope is root-only and
   `OnMissingChild = insert` stays rejected at save time, so there is nothing a nested suggestion
   could usefully prefill yet.

### 2.5 Considered and rejected: a shared `IntegrationContract` entity

An alternative: introduce a persisted `IntegrationContract` (key, version, root table, correlation
key) that both `ExportDefinition` and `ImportDefinition` reference by FK, rather than each carrying
its own copy of `IntegrationKey`/`ContractVersion`. Not adopted, for two reasons:

* **It's more structure than a UI-authoring convenience needs.** A new entity means a migration, an
  FK, and lifecycle questions this feature has no actual requirement to answer yet — what happens
  to the contract row when the last definition referencing it is deleted; whether a contract can
  have more than one enabled `ImportDefinition` at once (e.g. during a vendor migration from v1 to
  v2). Plain matching columns answer "does this export and this import agree they're the same
  exchange" exactly as well, at save-time-validation cost instead of schema-and-lifecycle cost.
* **It nudges toward a shape this codebase has twice deliberately avoided.** [Import
  Definitions §8](/pipeline/import-definitions.md#8-non-goals) rejects "a generic bidirectional
  sync engine," and [`ImportNode`'s own doc](/dynamic-import/import-node.md) explains why it
  doesn't merge into `ExportNode` despite being structurally identical: the two trees flow in
  opposite directions, and forcing them into one type would mean nullable fields that are
  meaningless half the time. A shared parent entity for `ExportDefinition`/`ImportDefinition` is
  the same move one level up.

If a real future requirement needs the contract to be a first-class, independently-managed thing
(e.g. a UI page listing "integrations" rather than exports and imports separately), this can be
revisited — nothing in the plain-column design forecloses adding a proper entity later and
migrating the columns onto it.

## 3. Reused vs. New

**Reused as-is:**
- `ImportNode`/`FieldMapping` shape — the suggestion produces ordinary `ImportNode` values, edited
  in the existing `ImportNodeTreeEditor.vue`, nothing new to render.
- `AllowedWritableColumns` schema-aware validator and the identifier-safety/GDPR-denylist checks —
  a suggested node is exactly as untrusted as a hand-typed one until it passes save-time
  validation.
- `ImportDefinitionPreviewPanel.vue`'s existing "paste a sample inbound file" affordance — the
  natural place to also surface the suggestion banner.
- `ConfigVersion` — reused for the wire payload's operator-facing traceability field instead of
  inventing a new one.

**New:**
- `ExportDefinition.IntegrationKey`/`ContractVersion`/`CorrelationKeySourceField` and
  `ImportDefinition.IntegrationKey`/`ContractVersion` columns (all nullable, no backfill needed).
- The `(IntegrationKey, ContractVersion)` uniqueness-among-enabled-rows check, added to each
  definition type's existing save-time validator.
- `provenance` key in `JsonExportFormatWriter`'s output and in the `ImportEnvelope` parser
  (additive, optional, ignored by every writer/parser that predates it).
- `ImportMappingSuggestion.SuggestFrom` (`Connector.Core.DynamicImport`) — pure function, no I/O
  beyond the two already-loaded definition rows.
- A suggestion banner + "Create from export" action in the New/Edit `ImportDefinition` UI (a
  dedicated step, `ImportMappingSuggestionPanel.vue`, rather than reusing
  `ImportDefinitionPreviewPanel.vue` — that panel drives `.../{id}/preview` against an
  already-saved definition's live ERP connection, which doesn't exist yet at this point in the
  flow), offered alongside "Start blank." The paired `IntegrationKey`/`ContractVersion` is shown
  read-only on `ImportDefinitionBasicFields.vue` once set, for an operator auditing why a
  definition's tree looks the way it does.
- A "reimport an edited export" affordance: `ImportDefinitionPreviewPanel.vue` recognizes an
  *exported* job file's shape (`schema_version` snake_case, distinct from a real `ImportEnvelope`)
  and rewraps it in place so Preview can run normally, and offers "Create Import Definition from
  this export" when the reshaped sample carries a `provenance.integrationKey`.

## 4. Non-Goals (guardrails this must not cross)

* **Never a second routing mechanism.** `ImportEnvelope.definition` stays the only field that
  decides which `ImportDefinition` a real inbound file is staged against. `provenance` is read by
  the authoring UI only, never by `ImportWorker`/`ImportNodeWalker` at processing time — conflating
  the two would mean a file's *processing* target could shift based on a field nobody is required
  to keep truthful.
* **Never a trust or bypass mechanism.** A matching `(integrationKey, contractVersion)` pair says
  "these two definitions were designed to pair," not "this specific file is safe to write." Every
  suggested node still needs `AllowedWritableColumns` + schema-aware validation before it can be
  saved as writable, and every staged run still needs four-eyes review before it touches the ERP.
  This only ever saves an operator typing at authoring time.
* **No CSV/Excel support.** Structured provenance metadata has no natural home in a flat row
  format; `ImportEnvelope` itself is already JSON-only, so this stays JSON-only symmetrically.
* **No change to the legacy single-mapping flow.** Scoped to `ExportDefinition`/`ImportDefinition`
  only.
* **No enum/taxonomy for `IntegrationKey`.** Free text, operator-owned, exactly as unconstrained as
  `Name`.
* **No shared `IntegrationContract` entity** — see §2.5.

## 5. Open question

The whole mechanism is inert until the vendor's ICD is told to echo
`provenance.integrationKey`/`contractVersion` back — an external negotiation, not something
resolved from inside this codebase. The export side carries the tag regardless of whether the
vendor round-trips it, and the import side's suggestion simply never triggers until a vendor file
actually carries it.

## Related

- [Import Definitions](/pipeline/import-definitions.md) — the `ImportEnvelope` shape and
  `AllowedWritableColumns`, both load-bearing constraints here, and its Non-Goals, which §2.5's
  rejection of a shared entity is anchored to
- [Export Definitions](/pipeline/export-definitions-2.0.md) — `ExportDefinition`/`ExportNode`,
  which `IntegrationKey`/`ContractVersion`/`CorrelationKeySourceField` extend
- [ImportNode Tree](/dynamic-import/import-node.md) / [ExportNode Tree](/dynamic-export/export-node.md) —
  the shared `FieldMapping` shape the suggestion generator reuses verbatim, and the "two small
  honest types" precedent §2.5 follows
- [Open Points](/planning/open-points.md) — the vendor-ICD negotiation §5 depends on
