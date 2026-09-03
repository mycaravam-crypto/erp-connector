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

The raw CI record produced by [IErpReader](/pipeline/erp-reader.md): every field from the ERP
source, including personal data. It never left the pipeline in memory, and was never written to
disk, logs, or any persistent storage — [IDataMinimizer](/pipeline/data-minimizer.md) consumed it
and produced an [ExportItem](/domain/export-item.md) in its place.

Of its fields, two carried compliance weight and are worth remembering even though the type is
gone: `TechnicianName` (personal data, always stripped — GDPR Art. 5(1)(c)) and
`StorageLocation` (potential PII, scope pending — see [Open Points](/processes/open-points.md) #4).
The rest (`Guid`, `SerialNumber`, `PartNumber`, `ParentSerialNumber`, `ModelReference`,
`CommissioningDate`, `MaintenanceState`) reappear, minimized, on
[ExportItem](/domain/export-item.md) and are documented there.
