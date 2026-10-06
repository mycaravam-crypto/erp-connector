---
type: ERP Domain Type
title: ExportManifest
description: Integrity and sequence metadata that accompanies every export file.
resource: src/Connector.Core/Domain/ExportManifest.cs
tags: [domain, manifest, integrity, sequence]
timestamp: 2026-06-28T00:00:00Z
---

A manifest accompanies every export data file, letting the receiving gateway verify file
integrity and detect lost exports (sequence gaps) without a back-channel.

# Schema

| Field             | Type           | Description                                                         |
|-------------------|----------------|---------------------------------------------------------------------|
| `SequenceNumber`  | int?           | Monotonically increasing from 1. Gaps signal lost exports. Null for an export-definition run. |
| `SchemaVersion`   | string         | Schema version in `MAJOR.MINOR` format. Breaking changes bump MAJOR.|
| `ExtractedAt`     | DateTimeOffset | UTC timestamp of the ERP read.                                      |
| `RecordCount`     | int            | Number of records in the data file. Must match actual row count.    |
| `Sha256Checksum`  | string         | SHA-256 of the data file (hex, lowercase). Verified before USB release. |
| `Producer`        | object         | Which connector wrote the file: `Application` (always `x5-connector`), `Version`, `InstanceId`. |

`Producer.InstanceId` is a random GUID created on first start and kept in the `AppSetting` table
(`instance_id`), so it stays the same across restarts and upgrades. It lets a receiving connector
tell another connector's export apart from a vendor file, and two instances apart from each other.
A restored database backup keeps its id, so a cloned installation shares it. On the copy, an
authenticated user regenerates the id in Settings → Connector Instance
(`POST /api/settings/instance/regenerate`, audit action `instance_id_regenerated`). The old id is
kept in `retired_instance_ids`, so this installation's earlier exports are still rejected as its own;
a paired instance sees the new id as the producer of every later export.

# Distribution

The manifest is serialized as JSON alongside the data file (extension follows the run's
configured format — `.xlsx`/`.csv`/`.json`):

```
staging/export_0042_20260628T060000Z.xlsx
staging/export_0042_20260628T060000Z.manifest.json
```

See [Export Schema](/schema/export-schema.md) for the filename template.

# Sequence Gap Detection

A jump from #41 to #43 signals a missing export #42, detectable without contacting the sender.
[Four-Eyes Release](/operations/four-eyes-release.md) should verify sequence continuity before
clearing a run for physical transfer.
