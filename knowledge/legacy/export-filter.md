---
type: Pipeline Stage
title: IExportFilter — Stage 2
description: Scope filter that retains only CIs with a non-empty GUID (the required vendor Coalesce key).
resource: src/Connector.Core/Interfaces/IExportFilter.cs
tags: [pipeline, stage-2, filter, scope, coalesce]
timestamp: 2026-06-28T00:00:00Z
---

> **⚠ Superseded (2.0).** `IExportFilter`/`ExportFilter` were never wired into the live
> application and have been removed from the codebase. The scope predicate now lives in
> the operator-authored SQL of the dynamic mapping (e.g. a `WHERE` clause or join
> selecting only in-scope rows) — see [DynamicExportService](/pipeline/dynamic-export-service.md).
> This page is kept only as a record of the original design intent (why a correlation-key
> filter is required at all).

The second stage of the original fixed pipeline. Applied the scope entitlement filter: only
CIs with a non-null, non-empty `Guid` proceeded, since that's the required vendor Coalesce
key — a missing `SerialNumber` never blocked export. Excluded CIs were audit-logged (counts and
non-personal identifiers only) without exposing personal data in log files.

The rule itself — a row without a correlation key must not be exported — is the durable design
intent. It now lives as an operator-authored `WHERE`/join predicate in the dynamic mapping's SQL
instead of a fixed filter stage; see [DynamicExportService](/pipeline/dynamic-export-service.md).
