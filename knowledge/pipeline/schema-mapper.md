---
type: Pipeline Stage
title: ISchemaMapper — Stage 4
description: Transforms ExportItem fields to ICD-schema format — ISO 8601 dates, all identifiers as strings.
resource: src/Connector.Core/Interfaces/ISchemaMapper.cs
tags: [pipeline, stage-4, icd, schema-mapping, formatting]
timestamp: 2026-06-28T00:00:00Z
---

> **⚠ Superseded (2.0).** `ISchemaMapper`/`SchemaMapper` were never wired into the live
> application and have been removed from the codebase. Identifier-as-string and ISO-8601
> date formatting are now applied inline in `DynamicExportService.ExecuteQueryAsync`. See
> [DynamicExportService](/pipeline/dynamic-export-service.md). This page is kept only as a
> record of the original design intent.

The fourth stage of the original fixed pipeline. Mapped [ExportItem](/domain/export-item.md)
fields onto the [Export Schema](/schema/export-schema.md) ICD contract — coercing dates to
ISO 8601 and, critically, every identifier to `string`, never a numeric type. That rule is the
design intent worth keeping: Excel silently auto-converts numeric-looking values, corrupting
leading zeros and long serial numbers — the GUID correlation key would be irreparably broken.
It threw `InvalidCorrelationKeyException` on a null/empty `Guid`, so such a record could never
leave the system.

**What replaced it:** identifier-as-string and ISO-8601 date formatting are now applied inline in
`DynamicExportService.ExecuteQueryAsync` — see [DynamicExportService](/pipeline/dynamic-export-service.md).
