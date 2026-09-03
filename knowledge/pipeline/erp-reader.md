---
type: Pipeline Stage
title: IErpReader — Stage 1
description: Reads all maintainable Configuration Items from the ERP as a full read-only snapshot.
resource: src/Connector.Core/Interfaces/IErpReader.cs
tags: [pipeline, stage-1, erp, read-only, snapshot]
timestamp: 2026-06-28T00:00:00Z
---

> **⚠ Superseded (2.0).** `IErpReader`/`DemoErpReader` were never wired into the live
> application and have been removed from the codebase. The connector now reads the ERP
> source directly via Npgsql, driven by the runtime-configurable mapping — see
> [DynamicExportService](/pipeline/dynamic-export-service.md). This page is kept only as a
> record of the original design intent.

The first stage of the original fixed pipeline. Read all maintainable CIs from the ERP source
system as a full, read-only snapshot (no delta parameter — full-snapshot volume is tracked as
[Open Point #5](/processes/open-points.md)) and passed them to [IExportFilter](/pipeline/export-filter.md).

The read-only/idempotent-snapshot constraint was the actual design intent worth keeping: the
connector must never write to the ERP, and repeated reads in the same window must agree. That
constraint carries forward unchanged into `DynamicExportService`'s direct Npgsql queries, even
though `IErpReader` itself is gone.
