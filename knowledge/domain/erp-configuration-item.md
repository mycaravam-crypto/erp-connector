---
type: ERP Domain Type
title: ErpConfigurationItem
description: Raw Configuration Item record as read from the ERP; may contain personal data and excluded fields.
resource: src/Connector.Core/Domain/ErpConfigurationItem.cs
tags: [domain, erp, raw-data, pii]
timestamp: 2026-06-28T00:00:00Z
---

> **⚠ Superseded (2.0).** This type was never wired into the live application and has been
> removed from the codebase — the dynamic mapping reads rows as untyped SQL result sets,
> not this fixed shape. See [DynamicExportService](/pipeline/dynamic-export-service.md).
> This page is kept only as a record of the original design intent.

A raw CI record produced by [IErpReader](/pipeline/erp-reader.md). Contains all fields
from the ERP source including personal data. This type **never leaves the pipeline** — it
is discarded in memory after [DataMinimizer](/pipeline/data-minimizer.md) processes it.

# Schema

| Field                | Type       | Description                                                              |
|----------------------|------------|--------------------------------------------------------------------------|
| `Guid`               | string?    | Internal PostgreSQL UUID. Coalesce key on ServiceNow side.               |
| `SerialNumber`       | string?    | Manufacturer serial number. Identification attribute.                    |
| `PartNumber`         | string?    | Article/part number of the model.                                        |
| `ParentSerialNumber` | string?    | Serial number of the parent CI in the BOM hierarchy. Null for root elements. |
| `ModelReference`     | string?    | Reference to the model article (master data).                            |
| `CommissioningDate`  | DateOnly?  | Installation or commissioning date. Can be null if not yet recorded.     |
| `MaintenanceState`   | string?    | Maintenance-relevant state from the ERP.                                 |
| `TechnicianName`     | string?    | **Personal data — stripped by DataMinimizer (GDPR Art. 5(1)(c)).**      |
| `StorageLocation`    | string?    | Storage location — inclusion in export scope pending (Open Point #4).   |

# Data Flow

```
IErpReader → ErpConfigurationItem[] → IExportFilter → IDataMinimizer → ExportItem
```

After [IDataMinimizer](/pipeline/data-minimizer.md) produces an [ExportItem](/domain/export-item.md),
the `ErpConfigurationItem` is garbage-collected. It is never written to disk, logs,
or any persistent storage.

# Constraints

- [IErpReader](/pipeline/erp-reader.md) implementations must be **read-only**; write access to the ERP is prohibited.
- Multiple calls in the same time window must return the same data (idempotent).
- Full snapshot in Iteration 1 — no delta parameters (Open Point #5: volume to be assessed).
