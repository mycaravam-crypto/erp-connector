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

The result of [ISchemaMapper](/legacy/schema-mapper.md) transforming an
[ExportItem](/legacy/export-item.md) into the column shape defined by the
[Export Schema](/schema/export-schema.md) ICD contract (`Guid`, `SerialNumber`, `PartNumber`,
`ParentSerialNumber`, `ModelReference`, `CommissioningDateIso8601`, `MaintenanceState`) — every
identifier a `string` (never numeric, to stop Excel from silently corrupting leading zeros and
breaking the GUID correlation key), null dates rendered as `""` rather than `null` cells to keep
Excel formula ranges stable, and all other nullable fields defaulted to `""`.

That formatting discipline is the durable part; it's applied inline in
`DynamicExportService.ExecuteQueryAsync` today rather than by a dedicated mapper — see
[DynamicExportService](/pipeline/dynamic-export-service.md).
