---
type: Pipeline Stage
title: ISchemaMapper — Stage 4
description: Transforms ExportItem fields to ICD-schema format — ISO 8601 dates, all identifiers as strings.
resource: src/Connector.Core/Interfaces/ISchemaMapper.cs
tags: [pipeline, stage-4, icd, schema-mapping, formatting]
timestamp: 2026-06-28T00:00:00Z
---

The fourth stage. Transforms [ExportItem](/domain/export-item.md) fields into a
[MappedExportRecord](/domain/mapped-export-record.md) that conforms to the
[Export Schema](/schema/export-schema.md) ICD contract.

# Contract

```csharp
MappedExportRecord Map(ExportItem item);
```

Throws `InvalidCorrelationKeyException` if `Guid` is null or empty — such records must
not leave the system.

# Mapping Rules

| Source field (`ExportItem`)  | Target field (`MappedExportRecord`) | Transformation                              |
|------------------------------|--------------------------------------|---------------------------------------------|
| `Guid`                       | `Guid`                               | As-is (string; already non-null post-filter) |
| `SerialNumber`               | `SerialNumber`                       | `?? string.Empty` — never null in output    |
| `PartNumber`                 | `PartNumber`                         | As-is                                       |
| `ParentSerialNumber`         | `ParentSerialNumber`                 | Nullable; preserved as null for root CIs    |
| `ModelReference`             | `ModelReference`                     | As-is                                       |
| `CommissioningDate`          | `CommissioningDateIso8601`           | `DateOnly.ToString("yyyy-MM-dd")` or `""`   |
| `MaintenanceState`           | `MaintenanceState`                   | `?? string.Empty`                           |

# Why Identifiers Must be Strings

Excel auto-converts numeric-looking values to numbers, silently truncating leading zeros
and corrupting long serial numbers. The GUID (correlation key) would be irreparably broken.
Explicit `string` assignment here, combined with column format `@` (text) in
[IPackager](/pipeline/packager.md), prevents this at two independent layers.

# Output

Produces [MappedExportRecord](/domain/mapped-export-record.md) records passed to
[IPackager](/pipeline/packager.md).
