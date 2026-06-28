---
type: Export Schema
title: Export Schema v2.0
description: ICD contract defining the column names, order, and filename format of CI export files.
resource: src/Connector.Core/Schema/ExportSchema.cs
tags: [schema, icd, versioned, excel]
timestamp: 2026-06-28T00:00:00Z
---

Single source of truth for all column names, ordering, and filename patterns used in the
Excel export. Implemented in `Connector.Core.Schema.ExportSchema`. Any column change
without a corresponding version bump is caught by a unit test at build time.

# Version

Current: **2.0**

| Version bump | When to use                                                             |
|--------------|-------------------------------------------------------------------------|
| **MAJOR**    | Breaking change — requires coordination with the vendor before deployment |
| **MINOR**    | Additive change (new optional column)                                   |

The schema version travels with every export via the `SchemaVersion` field of the
[ExportManifest](/domain/export-manifest.md).

# Schema

| Position | Column name            | C# constant                        |
|----------|------------------------|------------------------------------|
| 1        | `guid`                 | `ColumnNames.Guid`                 |
| 2        | `serial_number`        | `ColumnNames.SerialNumber`         |
| 3        | `part_number`          | `ColumnNames.PartNumber`           |
| 4        | `parent_serial_number` | `ColumnNames.ParentSerialNumber`   |
| 5        | `model_reference`      | `ColumnNames.ModelReference`       |
| 6        | `commissioning_date`   | `ColumnNames.CommissioningDate`    |
| 7        | `maintenance_state`    | `ColumnNames.MaintenanceState`     |

All columns use Excel format `49` (`@` / text) to prevent auto-coercion of serial
numbers or dates.

# Filename Format

```
export_{seqNo:D4}_{extractedAt:yyyyMMdd'T'HHmmss'Z'}.xlsx
```

Example: `export_0042_20260628T060000Z.xlsx`

Manifest companion: `export_0042_20260628T060000Z.manifest.json`

# Related

- [MappedExportRecord](/domain/mapped-export-record.md) — record type that maps onto these columns
- [ExportManifest](/domain/export-manifest.md) — carries the schema version in every export run
- [SchemaMapper](/pipeline/schema-mapper.md) — transforms ExportItem fields to match this schema
- [IPackager](/pipeline/packager.md) — serializes MappedExportRecord into the Excel file
