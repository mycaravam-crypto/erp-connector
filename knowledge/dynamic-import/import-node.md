---
type: Domain Type
title: ImportNode Tree
description: The recursive tree shape every ImportDefinition is built from — one node type for root/scalar-field/object/array, the write-side mirror of ExportNode, plus the schema-aware AllowedWritableColumns validator.
resource: src/Connector.Core/DynamicImport/ImportNode.cs
tags: [domain, dynamic-import, phase-17]
timestamp: 2026-09-06T00:00:00Z
---

An `ImportDefinition.RootNode` is one `ImportNode` tree — same recursive shape as
[ExportNode](/dynamic-export/export-node.md) (root/scalar-field/object/array, arbitrarily nested
via `Children`, reusing `FieldMapping` verbatim), walked in the opposite direction. See [Import
Definitions §4](/pipeline/import-definitions.md#4-data-model) for why it's a deliberately separate
type rather than a merge into `ExportNode`.

# Shape

```
ImportNode
├── SourceKey       string   — the JSON property/array name this node reads from the inbound record
├── Kind            string   — "root" | "scalar-field" | "object" | "array"
├── TargetColumn    string?  — set when Kind = scalar-field: the column this node writes on its own table
├── RelatedTable    string?  — set when Kind = object|array: the joined table
├── JoinKey         string?  — column on RelatedTable the join matches against
├── SourceJoinKey   string?  — column on *this* node's table the join matches against
├── OnMissingChild  string   — "insert" | "reject" — what happens when JoinKey doesn't resolve
├── Mapping         FieldMapping?  — set when Kind = scalar-field (reused verbatim from DynamicExport)
├── Children        ImportNode[]   — further fields/nested groups
└── Enabled         bool
```

`SourceKey` reads from JSON where `ExportNode.SourceField` reads from SQL; `TargetColumn` writes
to SQL where `ExportNode.TargetKey` writes to JSON. `OnMissingChild` has no export-side analogue —
a write-only policy for what happens when an object/array child's `JoinKey` doesn't resolve to an
existing row. **`OnMissingChild = "insert"` is only reachable for `array` children in principle**
— root rows are always match-only (`UnmatchedRootPolicy` has no "insert" option at all, see below)
— and even there, the Slice 5 save-time validator (`ImportDefinitionEndpoints.ValidateNode`)
rejects any node that sets it, enforcing v1's root-only confirmation-field scope (Open Decision
#15) at save time, not just by convention.

`ImportDefinition` also carries, outside the tree itself:

```
ImportDefinition
├── RootTable, RootMatchColumn      — table + column an inbound record's correlation key (the
│                                     same Guid exported today) must resolve against
├── RootNode           : ImportNode — the tree above
├── AllowedWritableColumns : string[]  — explicit allowlist (see below)
└── UnmatchedRootPolicy    : "reject" | "quarantine"  — deliberately no "auto-create"
```

A root row with no correlation-key match is excluded from the accepted set per
`UnmatchedRootPolicy` — never inserted. Only object/array *children* may ever be created, and only
when their own `OnMissingChild = "insert"` — a capability the v1 validator currently blocks
everywhere, per the paragraph above.

# Reading and writing a persisted tree

Every read of a stored `RootNode` goes through `ImportNodeJson.Deserialize`
(`Connector.Core.DynamicImport`), never a raw `JsonSerializer.Deserialize<ImportNode>` call — it
recursively backfills `Kind`/`OnMissingChild`/`Mapping`/`Children` the same way `ExportNodeJson`
does, so a tree saved before a property existed doesn't crash the first consumer that dereferences
it.

# AllowedWritableColumns — schema-aware validation (Open Decision #9)

The allowlist is checked twice, never trusted from just one:

1. **At save time** — `ImportDefinitionEndpoints.ValidateRequestAsync` walks the tree collecting
   every enabled scalar-field's `(table, TargetColumn)` pair, then checks each one against the
   live introspected ERP schema (`ConnectionEndpoints.IntrospectSchemaAsync`): the column must (a)
   be present in `AllowedWritableColumns`, (b) exist on its table, and must not be (c) the primary
   key, (d) an identity/computed column, or (e) an untracked foreign key. The root's own
   correlation-key field (matched against `RootMatchColumn`) is excluded from this check — it's
   read for matching, never written. The allowlist is also cross-checked against the [GDPR
   denylist](/operations/gdpr-compliance.md): a column that's GDPR-denied can never appear in
   `AllowedWritableColumns`, defense-in-depth even though personal data isn't expected on this
   side (Open Decision #7).
2. **At run time** — `ImportNodeWalker` re-checks writable columns against the same allowlist
   before building a diff, so a definition edited to add a bad column after the schema check ran
   (or one from before this validation existed) is never trusted silently.

Identifier-safety (`^[A-Za-z_][A-Za-z0-9_]*$`) and nesting-depth (`DynamicExportService
.MaxNestedDepth`) checks apply the same way `ExportNode`'s own validator applies them —
`SourceKey` is checked for control characters instead of the identifier regex, since it names a
JSON property, not a SQL identifier.

# Building a tree without hand-writing JSON

`ImportNodeTreeEditor.vue` is the frontend's recursive tree builder — structurally the same
component as `ExportNodeTreeEditor.vue`, built against `ImportNode` instead: picking a related
table for an object/array node prefills its FK-detected `JoinKey`/`SourceJoinKey`, and there is
deliberately no `OnMissingChild` picker in the UI at all (v1 only permits `"reject"`) and no
`Filter` input (import matches are always exact correlation-key lookups, not filtered subsets).
`ImportAllowedColumnsEditor.vue` is a separate, prominent list editor for the allowlist — flagging
any tree target column that isn't in it — kept visually distinct from the tree itself because it's
the feature's primary safety control (per import-definitions.md #57's acceptance criteria): an
operator should not be able to add a writable field to the tree without that field also appearing,
or being blocked, here.

# Related

- [Import Definitions](/pipeline/import-definitions.md) — the full spec this type implements, §4
- [ExportNode Tree](/dynamic-export/export-node.md) — the read-side sibling this mirrors
- [GDPR Compliance](/operations/gdpr-compliance.md) — the denylist `AllowedWritableColumns` is cross-checked against
- [ImportWorker](import-worker.md) — the run-time caller that re-checks the allowlist via `ImportNodeWalker`
