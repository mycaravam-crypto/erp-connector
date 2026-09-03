---
type: Pipeline Stage
title: IDataMinimizer — Stage 3
description: GDPR data minimization — strips personal data fields before any disk write.
resource: src/Connector.Core/Interfaces/IDataMinimizer.cs
tags: [pipeline, stage-3, gdpr, minimization, privacy]
timestamp: 2026-06-28T00:00:00Z
---

> **⚠ Superseded (2.0).** `IDataMinimizer`/`DataMinimizer` were never wired into the live
> application and have been removed from the codebase. GDPR minimization is still
> enforced, but as a runtime-editable denylist checked at mapping-save time and stripped
> at query time — not a fixed type from which the fields are simply absent. See
> [GDPR Compliance](/processes/gdpr-compliance.md) and
> [DynamicExportService](/pipeline/dynamic-export-service.md). This page is kept only as a
> record of the original design intent.

The third stage of the original fixed pipeline, and the GDPR compliance boundary. Transformed
each [ErpConfigurationItem](/domain/erp-configuration-item.md) into an
[ExportItem](/domain/export-item.md) in memory, before any disk write, by omitting personal and
non-entitled fields (`TechnicianName`, `StorageLocation` — see
[GDPR Compliance](/processes/gdpr-compliance.md) for the current field table and rationale).
Exclusion was enforced at the type-system level: `ExportItem` simply had no field to bypass.

**What replaced it:** GDPR minimization is still enforced with the same two fields excluded, but
via a runtime-editable denylist checked at mapping-save time and stripped at query time, not a
fixed type — see [DynamicExportService](/pipeline/dynamic-export-service.md).

The process for adding a newly-approved field still applies regardless of mechanism: legal/privacy
review, ICD coordination with the vendor, and a MAJOR version bump of the
[Export Schema](/schema/export-schema.md) if the field is new to the export file.

# Related

- [GDPR Compliance](/processes/gdpr-compliance.md)
- [ErpConfigurationItem](/domain/erp-configuration-item.md)
- [ExportItem](/domain/export-item.md)
