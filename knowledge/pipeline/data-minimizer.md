---
type: Pipeline Stage
title: IDataMinimizer — Stage 3
description: GDPR data minimization — strips personal data fields before any disk write.
resource: src/Connector.Core/Interfaces/IDataMinimizer.cs
tags: [pipeline, stage-3, gdpr, minimization, privacy]
timestamp: 2026-06-28T00:00:00Z
---

> **⚠ Superseded (2.0).** `IDataMinimizer`/`DataMinimizer` were never wired into the live
> application and have been removed from the codebase. GDPR minimization is still
> enforced, but as a runtime-editable denylist checked at mapping-save time and stripped
> at query time — not a fixed type from which the fields are simply absent. See
> [GDPR Compliance](/processes/gdpr-compliance.md) and
> [DynamicExportService](/pipeline/dynamic-export-service.md). This page is kept only as a
> record of the original design intent.

The third stage and the GDPR compliance boundary. Transforms each
[ErpConfigurationItem](/domain/erp-configuration-item.md) into an
[ExportItem](/domain/export-item.md) by omitting personal and non-entitled fields.

# Contract

```csharp
ExportItem Minimize(ErpConfigurationItem source);
```

# Excluded Fields

| Field             | Reason for exclusion                          |
|-------------------|-----------------------------------------------|
| `TechnicianName`  | Personal data — GDPR Art. 5(1)(c)             |
| `StorageLocation` | Scope inclusion pending review (Open Point #4)|

Minimization occurs in memory **before any disk write**. Neither field appears in
intermediate results, log files, or the exported file itself.

# Type-System Enforcement

The exclusion is enforced at the **type system level**: [ExportItem](/domain/export-item.md)
has no `TechnicianName` or `StorageLocation` fields. Bypassing the minimization rule
requires modifying both this class and the `ExportItem` record type, making the change
impossible to overlook in code review.

# Changing the Minimization Rule

Any addition of a new field to `ExportItem` requires:
1. Legal/privacy review confirming the field's inclusion is compliant.
2. ICD coordination with the vendor if the [Export Schema](/schema/export-schema.md) changes.
3. MAJOR version bump of the schema if the field is new to the export file.

# Output

Produces [ExportItem](/domain/export-item.md) records passed to [ISchemaMapper](/pipeline/schema-mapper.md).

# Related

- [GDPR Compliance](/processes/gdpr-compliance.md)
- [ErpConfigurationItem](/domain/erp-configuration-item.md)
- [ExportItem](/domain/export-item.md)
