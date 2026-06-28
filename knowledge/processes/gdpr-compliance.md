---
type: Business Process
title: GDPR Data Minimization
description: Policy and enforcement mechanism for personal data exclusion throughout the export pipeline.
tags: [process, gdpr, compliance, privacy, data-minimization]
timestamp: 2026-06-28T00:00:00Z
---

The connector processes ERP records that contain personal data. GDPR Article 5(1)(c)
(data minimization principle) requires that only data necessary for the stated purpose
is processed and transferred.

# Purpose of Transfer

The export supports **warranty management and product improvement** by the hardware vendor.
Only fields directly necessary for CI identification and maintenance tracking are in scope.

# Personal Data Fields

| Field             | Classification  | Handling                                           |
|-------------------|-----------------|----------------------------------------------------|
| `TechnicianName`  | Personal data   | **Always excluded** — GDPR Art. 5(1)(c)            |
| `StorageLocation` | Potentially PII | Currently excluded — scope pending (Open Point #4) |

# Pipeline Enforcement

Personal data is stripped at [IDataMinimizer](/pipeline/data-minimizer.md) (Stage 3):

1. Minimization occurs **in memory**, before any file write.
2. [ErpConfigurationItem](/domain/erp-configuration-item.md) (which contains personal data) is
   discarded after minimization — it is never written to disk, intermediate files, or logs.
3. [ExportItem](/domain/export-item.md) (the minimizer's output) has no personal data fields
   at the type-system level. The exclusion cannot be accidentally bypassed without modifying the type.

# Audit Logging

[IExportFilter](/pipeline/export-filter.md) logs excluded CIs at `Warning` level using
only non-personal fields (`PartNumber`, `SerialNumber`). This supports audit traces without
writing personal data to log files.

# Open Points Affecting This Policy

| # | Topic               | Impact                                                            |
|---|---------------------|-------------------------------------------------------------------|
| 4 | `StorageLocation`   | If confirmed in scope: `DataMinimizer` and `ExportSchema` must be updated; legal review required. |
| 3 | Classification marking | Release API may need a data-classification field on the run record. |

# Related

- [IDataMinimizer](/pipeline/data-minimizer.md)
- [ErpConfigurationItem](/domain/erp-configuration-item.md)
- [ExportItem](/domain/export-item.md)
- [Export Schema](/schema/export-schema.md)
