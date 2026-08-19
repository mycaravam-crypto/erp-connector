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

The fifth stage. Serializes [MappedExportRecord](/domain/mapped-export-record.md) records
into an Excel `.xlsx` file and assembles an [ExportManifest](/domain/export-manifest.md)
with SHA-256 checksum and sequence number.

# Contract

```csharp
Task<ExportPackage> PackageAsync(
    IReadOnlyList<MappedExportRecord> records,
    int sequenceNumber,
    CancellationToken ct);
```

# Implementation: ExcelPackager

Current implementation: `Connector.Export.ExcelPackager` using the **ClosedXML** library.

Key constraint: all columns are set to Excel format `49` (`@` / text). This prevents Excel
from auto-converting serial numbers or dates to numeric/date types, complementing the
string-enforcement in [ISchemaMapper](/pipeline/schema-mapper.md).

The column order follows the `ExportSchema.Columns` list exactly.

# Output

Produces an [ExportPackage](/domain/export-package.md) containing:
- `DataFileBytes`: raw `.xlsx` bytes
- `DataFileName`: formatted per the [Export Schema](/schema/export-schema.md) filename template
- `Manifest`: [ExportManifest](/domain/export-manifest.md) with SHA-256 over the data bytes

# Iteration Note

Iteration 1 output format is `.xlsx`. The interface is format-agnostic — Iteration 2 can
swap to a different format (e.g. CSV, Parquet) by replacing only this implementation.
All other pipeline stages are unaffected.

# Output

Produces [ExportPackage](/domain/export-package.md) passed to [IExportSink](/pipeline/export-sink.md).
