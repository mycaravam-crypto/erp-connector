---
type: Pipeline Stage
title: IPackager — Stage 5
description: Packages mapped records into an Excel file and computes the SHA-256 manifest.
resource: src/Connector.Core/Interfaces/IPackager.cs
tags: [pipeline, stage-5, packaging, xlsx, sha256]
timestamp: 2026-06-28T00:00:00Z
---

> **⚠ Superseded (2.0).** `IPackager`/`ExcelPackager` were never wired into the live
> application and have been removed from the codebase. Packaging is now
> `DynamicExportService.BuildExcelBytes`/`BuildCsvBytes`/`BuildJsonBytes`/`BuildNestedJsonBytes`,
> covering all three export formats plus the nested-JSON shape (not just Excel). See
> [DynamicExportService](/pipeline/dynamic-export-service.md). This page is kept only as a
> record of the original design intent.

The fifth stage of the original fixed pipeline. Serialized
[MappedExportRecord](/domain/mapped-export-record.md) records into an Excel `.xlsx` file (via
ClosedXML, all columns forced to text format `@` to stop Excel auto-converting serial numbers or
dates) and assembled the [ExportManifest](/domain/export-manifest.md) with SHA-256 checksum and
sequence number, producing an [ExportPackage](/domain/export-package.md).

**What replaced it:** `DynamicExportService.BuildExcelBytes`/`BuildCsvBytes`/`BuildJsonBytes`/
`BuildNestedJsonBytes` cover all three export formats plus the nested-JSON shape, not just Excel
— see [DynamicExportService](/pipeline/dynamic-export-service.md). The manifest/SHA-256 assembly
and [IExportSink](/pipeline/export-sink.md) handoff are unchanged.
