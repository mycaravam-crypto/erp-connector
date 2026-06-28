---
type: ERP Domain Type
title: ExportPackage
description: Complete export artifact composed of the data file bytes and an integrity manifest.
resource: src/Connector.Core/Domain/ExportPackage.cs
tags: [domain, package, artifact, atomic]
timestamp: 2026-06-28T00:00:00Z
---

The complete output of [IPackager](/pipeline/packager.md). Contains both the data file
(Excel bytes) and the [ExportManifest](/domain/export-manifest.md). Both parts are written
atomically to the staging path — the export is not considered ready until both files are
fully present on disk.

# Schema

| Field           | Type           | Description                                                |
|-----------------|----------------|------------------------------------------------------------|
| `Manifest`      | ExportManifest | Integrity and sequence metadata.                           |
| `DataFileBytes` | byte[]         | Content of the data file (Iteration 1: `.xlsx`).           |
| `DataFileName`  | string         | Filename without path, e.g. `export_0042_20260628T060000Z.xlsx`. |

# Atomicity Guarantee

[FileSystemExportSink](/pipeline/export-sink.md) writes the data bytes to a `.tmp` file
first, then uses `File.Move` to the final name. The manifest JSON is written only after
the data file move completes. A partially-written package is never visible to the
[Four-Eyes Release](/processes/four-eyes-release.md) authority.

# Data Flow

```
IPackager.PackageAsync() → ExportPackage → IExportSink.WriteAsync() → staging/
```
