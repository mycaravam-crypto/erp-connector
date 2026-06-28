---
type: ERP Domain Type
title: ExportItem
description: GDPR-minimized CI containing only exportable fields; the type-system boundary for personal data exclusion.
resource: src/Connector.Core/Domain/ExportItem.cs
tags: [domain, minimized, gdpr, boundary]
timestamp: 2026-06-28T00:00:00Z
---

The result of [IDataMinimizer](/pipeline/data-minimizer.md) processing an
[ErpConfigurationItem](/domain/erp-configuration-item.md). Contains exclusively fields
that may be exported. The exclusion of personal data is enforced at the **type system level**:
`ExportItem` has no `TechnicianName` or `StorageLocation` fields.

# Schema

| Field                | Type      | Description                                              |
|----------------------|-----------|----------------------------------------------------------|
| `Guid`               | string    | Non-null (filter guarantees this). Coalesce key on ServiceNow. |
| `SerialNumber`       | string?   | May be absent — does not block the export.               |
| `PartNumber`         | string    | Article/part number.                                     |
| `ParentSerialNumber` | string?   | Null for BOM root elements.                              |
| `ModelReference`     | string    | Reference to the model article.                          |
| `CommissioningDate`  | DateOnly? | May be null if not recorded.                             |
| `MaintenanceState`   | string?   | Maintenance state, e.g. "Active", "InRepair".            |

# Data Flow

```
ErpConfigurationItem → DataMinimizer → ExportItem → ISchemaMapper → MappedExportRecord
```

# Compliance Note

`ExportItem` is the GDPR compliance boundary within the pipeline. Everything downstream
of this type is safe to persist and transmit. See [GDPR Compliance](/processes/gdpr-compliance.md).

The type system enforces the boundary: changing which fields are included requires
modifying both the `DataMinimizer` class and this record type, triggering a mandatory code review.
