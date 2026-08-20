---
type: Pipeline Stage
title: IExportFilter — Stage 2
description: Scope filter that retains only CIs with a non-empty GUID (the required ServiceNow Coalesce key).
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

The second stage. Applies the scope entitlement filter: only CIs that can be correlated
to a ServiceNow asset may proceed. Excluded CIs are audit-logged with reason.

# Contract

```csharp
IReadOnlyList<ErpConfigurationItem> Filter(IReadOnlyList<ErpConfigurationItem> items);
```

Synchronous — no I/O dependency; filter rules are in-memory.

# Filter Rule (Iteration 1)

| Condition                    | Decision    |
|------------------------------|-------------|
| `Guid` is non-null, non-empty | **In scope** |
| `Guid` is null or whitespace  | **Excluded** — Coalesce key cannot be fulfilled on ServiceNow side |

A missing `SerialNumber` does **not** block the export.

# Audit Logging

Excluded CIs are logged at `Warning` level using only `PartNumber` and `SerialNumber` —
never personal data fields (`TechnicianName`). This supports audit traces without
exposing personal data in log files.

A summary is logged at `Information` level: total, included, excluded counts.

# Output

Returns a subset of [ErpConfigurationItem](/domain/erp-configuration-item.md) records,
passed to [IDataMinimizer](/pipeline/data-minimizer.md).
