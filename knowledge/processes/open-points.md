---
type: Business Process
title: Open Points
description: Tracked decisions and clarifications outstanding from the Technical Concept that will drive future code changes.
tags: [process, open-points, roadmap, decisions, compliance]
timestamp: 2026-06-28T00:00:00Z
---

Eight open points were raised in the Technical Concept document (`TECHNICAL_CONCEPT.md §10`).
Points #1 and #2 are resolved; #3–#8 are pending stakeholder or legal decisions.

# Resolved

| # | Topic | Resolution |
|---|-------|------------|
| 1 | Correlation key | **Resolved.** `Guid` (PostgreSQL UUID) is the coalesce key. `SerialNumber` is no longer the correlation field and a missing serial does not block export. Schema version bumped to 2.0. |
| 2 | Missing serial number | **Resolved.** `SerialNumber` is nullable in [ExportItem](/domain/export-item.md) and [MappedExportRecord](/domain/mapped-export-record.md). Empty string in the output file; does not block the export. |

# Pending

| # | Topic | Stakeholder | Code Impact |
|---|-------|-------------|-------------|
| 3 | Classification marking | Legal | Release API (`POST /api/exports/{seqNo}/release`) may need a data-classification field on the [ExportRun](/domain/export-run.md). |
| 4 | `storagelocation` entitlement | Data owner + Legal | If confirmed in scope: [IDataMinimizer](/pipeline/data-minimizer.md) must pass the field through; [ExportItem](/domain/export-item.md) gains a new field; [Export Schema](/schema/export-schema.md) bumps to MAJOR version; ICD re-negotiation with vendor. |
| 5 | Snapshot volume | ERP data steward | If the CI count exceeds ~500 k: add a pagination or delta parameter to `DynamicExportService`'s query path (`ConnectionEndpoints.IntrospectSchemaAsync` + the mapping-driven query). Currently a full-snapshot read with no server-side paging. |
| 6 | Return-channel timing | Vendor + sponsor | Iteration 2 scope. The vendor will eventually write back CI update confirmations; timing and format TBD. |
| 7 | Retention periods | Legal + DPO | `RetentionDays` defaults to 30. The final value must be agreed with the DPO and set in production `appsettings.json`. See [Data Retention](/processes/data-retention.md). |
| 8 | Allocation chart import | ERP + vendor | Defines the scope predicate for the source-table mapping in `SchemaView.vue`/`DynamicExportService`. Today the predicate is whatever the operator configures at mapping-save time; production scope may also need to depend on allocation chart references. |

# How Decisions Flow Into Code

When a pending point is resolved, the typical sequence is:

1. The decision is recorded here (update Status from "Pending" to "Resolved" with the outcome).
2. The affected knowledge files are updated (e.g., [GDPR Compliance](/processes/gdpr-compliance.md) for #4, [Data Retention](/processes/data-retention.md) for #7).
3. A code change is planned and scheduled, referencing this open point by number.
4. The ROADMAP is updated with the new task.

# Related

- [GDPR Compliance](/processes/gdpr-compliance.md) — #3 and #4 affect minimization policy
- [Data Retention](/processes/data-retention.md) — #7 drives the `RetentionDays` value
- [Dynamic Export Service](/pipeline/dynamic-export-service.md) — #5 and #8 affect the live query/mapping path (the historical `IErpReader` this originally referenced was removed in the [changelog](/changelog.md)'s Phase 13 cleanup)
- [Export Schema](/schema/export-schema.md) — #4 would require a schema version bump
