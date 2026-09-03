---
type: ERP Domain Type
title: ExportItem
description: GDPR-minimized CI containing only exportable fields; the type-system boundary for personal data exclusion.
resource: src/Connector.Core/Domain/ExportItem.cs
tags: [domain, minimized, gdpr, boundary]
timestamp: 2026-06-28T00:00:00Z
---

> **⚠ Superseded (2.0).** This type was never wired into the live application and has been
> removed from the codebase. GDPR minimization is enforced by a runtime-editable denylist,
> not by field absence on a fixed type — see [GDPR Compliance](/operations/gdpr-compliance.md)
> and [DynamicExportService](/pipeline/dynamic-export-service.md). This page is kept only
> as a record of the original design intent.

The result of [IDataMinimizer](/legacy/data-minimizer.md) processing an
[ErpConfigurationItem](/legacy/erp-configuration-item.md): the GDPR compliance boundary within
the old pipeline. Everything downstream of this type was safe to persist and transmit — see
[GDPR Compliance](/operations/gdpr-compliance.md). Personal-data exclusion was enforced at the
type-system level: `ExportItem` simply had no `TechnicianName` or `StorageLocation` field, so
changing what's included required touching both `DataMinimizer` and this record, forcing review.

# Schema

| Field                | Type      | Description                                              |
|----------------------|-----------|-----------------------------------------------------------|
| `Guid`               | string    | Non-null (filter guarantees this). Coalesce key on ServiceNow. |
| `SerialNumber`       | string?   | May be absent — does not block the export.               |
| `PartNumber`         | string    | Article/part number.                                     |
| `ParentSerialNumber` | string?   | Null for BOM root elements.                              |
| `ModelReference`     | string    | Reference to the model article.                          |
| `CommissioningDate`  | DateOnly? | May be null if not recorded.                             |
| `MaintenanceState`   | string?   | Maintenance state, e.g. "Active", "InRepair".            |

This shape (minus GDPR-excluded fields) still describes what an export row looks like today; it's
just produced as an untyped SQL result set by `DynamicExportService`, not this record — see
[DynamicExportService](/pipeline/dynamic-export-service.md).
