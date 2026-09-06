---
type: Business Process
title: Open Points
description: Tracked decisions and clarifications outstanding from the Technical Concept that will drive future code changes.
tags: [process, open-points, roadmap, decisions, compliance]
timestamp: 2026-06-28T00:00:00Z
---

Eight open points were raised in the Technical Concept document (`TECHNICAL_CONCEPT.md §10`).
Points #1, #2, and #6 are resolved; #3–#5 and #7–#8 are pending stakeholder or legal decisions.

# Resolved

| # | Topic | Resolution |
|---|-------|------------|
| 1 | Correlation key | **Resolved.** `Guid` (PostgreSQL UUID) is the coalesce key. `SerialNumber` is no longer the correlation field and a missing serial does not block export. Schema version bumped to 2.0. |
| 2 | Missing serial number | **Resolved.** `SerialNumber` is nullable in [ExportItem](/legacy/export-item.md) and [MappedExportRecord](/legacy/mapped-export-record.md). Empty string in the output file; does not block the export. |
| 6 | Return-channel timing | **Resolved — the return channel is built and shipped (Phase 17, all 7 slices).** [Import Definitions](/pipeline/import-definitions.md) §6 answers every open decision this point raised: the root-match key is the existing `Guid` correlation key (#4); writable scope is confirmation/status fields on the root entity only, an explicit allowlist validated against the introspected schema (#5, #9); a single bad vendor row is quarantined without blocking the rest of the file (#6); the inbound side enforces the GDPR denylist defensively even though personal data isn't expected there (#7); no sequence/gap detection is built for v1, since the SHA-256 manifest alone covers file integrity and vendor confirmations don't arrive 1:1 per export run (#8). See [Dynamic Import](/dynamic-import/index.md) for how it actually runs. What was genuinely still pending — *when* the vendor delivers a return channel at all, and the concrete confirmation-field column names — is a vendor ICD negotiation, not a design or implementation gap; that negotiation is out of scope for this connector's own backlog and tracked with the vendor relationship, not here. |

# Pending

| # | Topic | Stakeholder | Code Impact |
|---|-------|-------------|-------------|
| 3 | Classification marking | Legal | Release API (`POST /api/exports/{seqNo}/release`) may need a data-classification field on the [ExportRun](/domain/export-run.md). |
| 4 | `storagelocation` entitlement | Data owner + Legal | If confirmed in scope: [IDataMinimizer](/legacy/data-minimizer.md) must pass the field through; [ExportItem](/legacy/export-item.md) gains a new field; [Export Schema](/schema/export-schema.md) bumps to MAJOR version; ICD re-negotiation with vendor. |
| 5 | Snapshot volume | ERP data steward | If the CI count exceeds ~500 k: add a pagination or delta parameter to `DynamicExportService`'s query path (`ConnectionEndpoints.IntrospectSchemaAsync` + the mapping-driven query). Currently a full-snapshot read with no server-side paging. |
| 7 | Retention periods | Legal + DPO | `RetentionDays` defaults to 30. The final value must be agreed with the DPO and set in production `appsettings.json`. See [Data Retention](/operations/data-retention.md). |
| 8 | Allocation chart import | ERP + vendor | Defines the scope predicate for the source-table mapping in `SchemaView.vue`/`DynamicExportService`. Today the predicate is whatever the operator configures at mapping-save time; production scope may also need to depend on allocation chart references. |

# How Decisions Flow Into Code

When a pending point is resolved, the typical sequence is:

1. The decision is recorded here (update Status from "Pending" to "Resolved" with the outcome).
2. The affected knowledge files are updated (e.g., [GDPR Compliance](/operations/gdpr-compliance.md) for #4, [Data Retention](/operations/data-retention.md) for #7).
3. A code change is planned and scheduled, referencing this open point by number.
4. The ROADMAP is updated with the new task.

# Related

- [Import Definitions](/pipeline/import-definitions.md) — the shipped resolution to #6
- [GDPR Compliance](/operations/gdpr-compliance.md) — #3 and #4 affect minimization policy
- [Data Retention](/operations/data-retention.md) — #7 drives the `RetentionDays` value
- [Dynamic Export Service](/pipeline/dynamic-export-service.md) — #5 and #8 affect the live query/mapping path (the historical `IErpReader` this originally referenced was removed in the [changelog](/changelog.md)'s Phase 13 cleanup)
- [Export Schema](/schema/export-schema.md) — #4 would require a schema version bump
