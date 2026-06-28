---
type: ERP Domain Type
title: ExportManifest
description: Integrity and sequence metadata that accompanies every export file.
resource: src/Connector.Core/Domain/ExportManifest.cs
tags: [domain, manifest, integrity, sequence]
timestamp: 2026-06-28T00:00:00Z
---

A manifest accompanies every export data file. It enables the receiving gateway to
verify file integrity and detect lost exports (sequence gaps) without a back-channel.

# Schema

| Field             | Type           | Description                                                         |
|-------------------|----------------|---------------------------------------------------------------------|
| `SequenceNumber`  | int            | Monotonically increasing from 1. Gaps signal lost exports.          |
| `SchemaVersion`   | string         | Schema version in `MAJOR.MINOR` format. Breaking changes bump MAJOR.|
| `ExtractedAt`     | DateTimeOffset | UTC timestamp of the ERP read.                                      |
| `RecordCount`     | int            | Number of records in the data file. Must match actual row count.    |
| `Sha256Checksum`  | string         | SHA-256 of the data file (hex, lowercase). Verified before USB release. |

# Distribution

The manifest is serialized as JSON alongside the data file:

```
staging/export_0042_20260628T060000Z.xlsx
staging/export_0042_20260628T060000Z.manifest.json
```

See [Export Schema](/schema/export-schema.md) for the filename template.

# Sequence Gap Detection

A jump from sequence #41 to #43 signals a missing export #42. The receiver can detect
this without contacting the sender. The [Four-Eyes Release](/processes/four-eyes-release.md)
authority should verify sequence continuity before clearing a run for physical transfer.
