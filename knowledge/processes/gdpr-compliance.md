---
type: Business Process
title: GDPR Data Minimization
description: Policy and enforcement mechanism for personal data exclusion throughout the export pipeline.
tags: [process, gdpr, compliance, privacy, data-minimization]
timestamp: 2026-06-28T00:00:00Z
---

The connector processes ERP records containing personal data. GDPR Art. 5(1)(c) (data
minimization) requires that only data necessary for the stated purpose is processed and
transferred.

# Purpose of Transfer

The export supports **warranty management and product improvement** by the hardware vendor.
Only fields directly necessary for CI identification and maintenance tracking are in scope.

# Personal Data Fields

| Field             | Classification  | Handling                                           |
|-------------------|-----------------|-----------------------------------------------------|
| `TechnicianName`  | Personal data   | **Always excluded** — GDPR Art. 5(1)(c)            |
| `StorageLocation` | Potentially PII | Currently excluded — scope pending (Open Point #4) |

# Enforcement

Personal data is stripped in memory before any disk write, and never appears in intermediate
results, log files, or the exported file. The mechanism: a **runtime-editable denylist**, checked
at mapping-save time (a save is rejected if it maps a denylisted field) and re-stripped from
every query result as defence-in-depth — see
[DynamicExportService](/pipeline/dynamic-export-service.md).

Excluded rows/fields are audit-logged using only non-personal identifiers (`PartNumber`,
`SerialNumber`), never `TechnicianName` — traceable without writing personal data to log files.

*(The original design enforced this at the type level — `ExportItem` simply had no
`TechnicianName`/`StorageLocation` field. That fixed pipeline is gone; see
[IDataMinimizer](/pipeline/data-minimizer.md) for why the rule exists.)*

# Open Points Affecting This Policy

| # | Topic               | Impact                                                            |
|---|---------------------|--------------------------------------------------------------------|
| 4 | `StorageLocation`   | If confirmed in scope: the denylist and [Export Schema](/schema/export-schema.md) must be updated; legal review required. |
| 3 | Classification marking | Release API may need a data-classification field on the run record. |

# Related

- [DynamicExportService](/pipeline/dynamic-export-service.md) — runtime denylist enforcement
- [IDataMinimizer](/pipeline/data-minimizer.md) — original type-level design intent
- [Export Schema](/schema/export-schema.md)
