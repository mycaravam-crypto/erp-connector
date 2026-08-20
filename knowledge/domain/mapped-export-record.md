---
type: ERP Domain Type
title: MappedExportRecord
description: Fully ICD-mapped export record with all fields formatted for the Excel output file.
resource: src/Connector.Core/Domain/MappedExportRecord.cs
tags: [domain, icd, export-ready, excel]
timestamp: 2026-06-28T00:00:00Z
---

> **⚠ Superseded (2.0).** This type was never wired into the live application and has been
> removed from the codebase — exported rows are `Dictionary<string,string>`/`JsonObject`
> built directly by `DynamicExportService`, not this fixed record. See
> [DynamicExportService](/pipeline/dynamic-export-service.md). This page is kept only as a
> record of the original design intent.

The result of [ISchemaMapper](/pipeline/schema-mapper.md) transforming an
[ExportItem](/domain/export-item.md). All fields are export-ready according to the
[Export Schema](/schema/export-schema.md) ICD contract.

# Schema

| Field                      | Type    | Description                                                      |
|----------------------------|---------|------------------------------------------------------------------|
| `Guid`                     | string  | PostgreSQL UUID as text. Coalesce field on ServiceNow side.      |
| `SerialNumber`             | string  | Manufacturer serial number as text (never numeric).              |
| `PartNumber`               | string  | Article/part number.                                             |
| `ParentSerialNumber`       | string? | Null for BOM root elements.                                      |
| `ModelReference`           | string  | Reference to the model article.                                  |
| `CommissioningDateIso8601` | string  | ISO 8601 date (`yyyy-MM-dd`) or empty string if not recorded.    |
| `MaintenanceState`         | string  | Maintenance state. Empty string if absent.                       |

# Critical Formatting Rules

All identifiers are **strings, never numeric**. Excel silently auto-converts
numeric-looking strings to numbers, corrupting leading zeros and long serial numbers.
The correlation key (GUID) would be irreparably broken. The `SchemaMapper` enforces this
via explicit `string` assignments; the `ExcelPackager` reinforces it with column format `@` (text).

Dates use ISO 8601 `yyyy-MM-dd`. Null dates become empty strings, not `null` cells,
to keep Excel formula ranges stable.

# Data Flow

```
ExportItem → SchemaMapper → MappedExportRecord → IPackager → ExportPackage
```
