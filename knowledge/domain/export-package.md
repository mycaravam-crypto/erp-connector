---
type: ERP Domain Type
title: ExportPackage
description: Complete export artifact composed of the data file bytes and an integrity manifest.
resource: src/Connector.Core/Domain/ExportPackage.cs
tags: [domain, package, artifact, atomic]
timestamp: 2026-06-28T00:00:00Z
---

The complete output produced by [DynamicExportService](/pipeline/dynamic-export-service.md)'s
`BuildExcelBytes`/`BuildCsvBytes`/`BuildJsonBytes`/`BuildNestedJsonBytes`: the data file bytes
plus an [ExportManifest](/domain/export-manifest.md). Both parts are written atomically to the
staging path — the export isn't ready until both files are fully present on disk.

# Schema

| Field           | Type           | Description                                                |
|-----------------|----------------|--------------------------------------------------------------|
| `Manifest`      | ExportManifest | Integrity and sequence metadata.                           |
| `DataFileBytes` | byte[]         | Content of the data file, in the run's configured format.  |
| `DataFileName`  | string         | Filename without path, e.g. `export_0042_20260628T060000Z.xlsx` (extension follows format). |

# Atomicity Guarantee

[FileSystemExportSink](/pipeline/export-sink.md) writes the data bytes to a `.tmp` file first,
then `File.Move`s it to the final name; the manifest JSON is written only after that move
completes. A partially-written package is never visible to
[Four-Eyes Release](/processes/four-eyes-release.md).

# Data Flow

```
DynamicExportService.BuildExportAsync() → ExportPackage → IExportSink.WriteAsync() → staging/
```
