---
type: Pipeline Stage
title: IExportSink — Stage 6
description: Writes the ExportPackage atomically to the staging path for four-eyes release.
resource: src/Connector.Core/Interfaces/IExportSink.cs
tags: [pipeline, stage-6, sink, staging, atomic]
timestamp: 2026-06-28T00:00:00Z
---

The final stage of the pipeline. Persists the [ExportPackage](/domain/export-package.md)
to the staging directory where the [Four-Eyes Release](/processes/four-eyes-release.md)
authority can inspect and approve it.

# Contract

```csharp
Task WriteAsync(ExportPackage package, CancellationToken ct);
```

Throws `ExportSinkException` if the staging path is not writable.

# Implementation: FileSystemExportSink

Current implementation: `Connector.Infrastructure.FileSystemExportSink`.

Staging path is configured via `ExportSink.StagingPath` in `appsettings.json`
(default in dev: `./staging`).

# Atomicity

1. Data bytes are written to `<filename>.tmp`.
2. `File.Move` renames `.tmp` to the final `.xlsx` name.
3. Manifest JSON is written to `<filename>.manifest.json`.

A half-written file is never visible as a complete export. The [Four-Eyes Release](/processes/four-eyes-release.md)
authority only sees a run after both the `.xlsx` and `.manifest.json` are fully present.

# Output Artifacts

```
staging/export_0042_20260628T060000Z.xlsx
staging/export_0042_20260628T060000Z.manifest.json
```

# Related

- [Four-Eyes Release](/processes/four-eyes-release.md)
- [ExportManifest](/domain/export-manifest.md)
- [ExportPackage](/domain/export-package.md)
