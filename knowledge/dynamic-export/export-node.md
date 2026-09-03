---
type: Domain Type
title: ExportNode Tree
description: The recursive tree shape every ExportDefinition is built from — one node type for root/scalar-field/object/array, replacing the legacy Fields/Relations/NestedGroups split.
resource: src/Connector.Core/DynamicExport/ExportNode.cs
tags: [domain, dynamic-export, phase-14]
timestamp: 2026-09-03T00:00:00Z
---

An `ExportDefinition.RootNode` is one `ExportNode` tree — the single shape a definition's fields,
joins, filters, and value transforms are all expressed in. It generalizes the legacy
`ExportMappingConfig`'s three parallel shapes (`Fields`/`Relations`/`NestedGroups`, see
[DynamicExportService](/pipeline/dynamic-export-service.md)) into one recursive type, walked by
one query builder (`DynamicExportService.BuildExportNodeAsync`) and honored by every format
writer (CSV/Excel/JSON) — see [Export Definitions 2.0 §4](/pipeline/export-definitions-2.0.md#4-data-model)
for the full rationale.

# Shape

```
ExportNode
├── TargetKey       string   — export-visible name: column header or JSON key
├── Kind            string   — "root" | "scalar-field" | "object" | "array"
├── SourceField     string?  — set when Kind = scalar-field: the column on this node's own table
├── RelatedTable    string?  — set when Kind = object|array: the joined table
├── JoinKey         string?  — column on RelatedTable the join matches against
├── SourceJoinKey   string?  — column on *this* node's table the join matches against
├── Filter          string?  — optional WHERE-clause fragment, scoped to this node's own subquery
├── Mapping         FieldMapping?  — set when Kind = scalar-field
├── Children        ExportNode[]   — further fields/nested groups (root/object/array only)
└── Enabled         bool
```

A `root` node's own fields (`SourceField`/`RelatedTable`/etc.) are unused — it exists only to hold
`Children`, one per top-level export key. `Kind` extends the legacy
`ExportMappingNestedGroup.Kind` (`"object"|"array"`) with `scalar-field` and `root`: a flat
CSV/Excel row is just the case where every node is a `scalar-field` at depth 1, so no separate
"flat" shape is needed — a fourth level of CSV nesting needs zero new types, only a smarter writer.

# FieldMapping — value shaping on a scalar-field node

```
FieldMapping
├── DefaultValue    string?  — substituted when the source value is NULL
├── Transform       string   — "none" | "uppercase" | "lowercase" | "trim" | "dateFormat" | "constant"
├── TransformArg    string?  — the date-format string (dateFormat) or the literal value (constant)
└── DataType        string   — "string" | "number" | "boolean" | "date" — coercion target
```

Rename is `TargetKey` differing from `SourceField`; exclusion is `Enabled = false` — both need no
`FieldMapping` at all. `Transform`/`DataType` are small closed enums (`FieldTransform`/
`FieldDataType` in `ExportNode.cs`), not a scripting engine — see
[Export Definitions 2.0 §10](/pipeline/export-definitions-2.0.md#10-non-goals).

**Deviation from the original DataType design:** a scalar column is always read from Postgres as
`::text`, never cast per `DataType` in SQL — a bad value in one row would otherwise fail the
*entire* query. `DataType` coercion happens in C# after the row is read
(`DynamicExportService.CoerceToDataType`), so one malformed field degrades to a best-effort string
instead of aborting the whole export.

# Reading and writing a persisted tree

Every read of a stored `RootNode` goes through `ExportNodeJson.Deserialize`, never a raw
`JsonSerializer.Deserialize<ExportNode>` call — it recursively backfills missing
`Kind`/`Children`/`Mapping` properties the same way `ExportMappingJson` does for the legacy
config, so a tree saved before a schema addition doesn't crash the first consumer that
dereferences the new field.

# Validation

`ExportDefinitionEndpoints`'s save-time validator (not the query engine — see
[Export Definition API](/api/export-definition-api.md#validation)) recursively checks, at every
node and every depth: `TargetKey` is non-empty and control-character-free; every identifier
reaching the SQL builder (`SourceField`/`RelatedTable`/`JoinKey`/`SourceJoinKey`) matches
`^[A-Za-z_][A-Za-z0-9_]*$`; nesting doesn't exceed `DynamicExportService.MaxNestedDepth`; no two
enabled siblings share a `TargetKey`; and every `SourceField` clears the
[GDPR denylist](/operations/gdpr-compliance.md). `Filter` is deliberately **not**
identifier-validated — it's a free-form WHERE-clause fragment by design, not an identifier.

# Building a tree without hand-writing JSON

The frontend tree builder (`ExportNodeTreeEditor.vue`) is the one place a definition's tree is
normally constructed: picking a related table for an `object`/`array` node auto-populates its
`Children` with one disabled `scalar-field` node per column of that table (same
"pick columns via checkbox" UX the legacy `NestedGroupEditor.vue`/`FieldPickerTable.vue` give the
single-mapping flow), so building a multi-level export needs no manually-typed `SourceField`.

# Related

- [Export Definitions 2.0](/pipeline/export-definitions-2.0.md) — the full spec this type implements
- [Export Definition API](/api/export-definition-api.md) — the CRUD/validation HTTP surface
- [DynamicExportService](/pipeline/dynamic-export-service.md) — the query engine that walks this tree
- [GDPR Compliance](/operations/gdpr-compliance.md) — the denylist enforced on every `SourceField`
