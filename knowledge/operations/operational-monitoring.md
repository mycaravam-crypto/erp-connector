---
type: Business Process
title: Operational Monitoring
description: Health checks, stale-pending detection, and sequence gap detection that support the daily operator workflow.
tags: [process, monitoring, health, observability]
timestamp: 2026-06-29T00:00:00Z
---

Three lightweight mechanisms, added in Phase 6, make the daily export cycle safer and observable
without an external monitoring stack.

# Health Check

`GET /api/health` — no authentication required, suitable for uptime monitors.

Checks three things and returns a JSON summary:

| Check | What it tests |
|---|---|
| `erp_db` | Can EF Core open a connection to the ERP source database? |
| `log_db` | Can EF Core open a connection to the export-log SQLite? |
| `staging` | Does the staging directory exist and is it writable (probe file written + deleted)? |

Returns HTTP 200 `{"status":"healthy",…}` when all checks pass, 503 `{"status":"degraded",…}` otherwise.

# Stale Pending Indicator

`GET /api/exports` includes `isStale: true` on any `Pending` run whose `ExtractedAt` is more than
24 hours old. ExportView shows an amber callout banner when any stale run exists, plus an inline
"overdue" tag on the stale row — surfacing missed releases without a separate alerting system.

# Sequence Gap Detection

`GET /api/exports/{seqNo}` includes a `sequenceGapWarning` string (null when no gap), set when a
Pending run's sequence number isn't the immediate successor of the last Released run — e.g. last
released #41, Pending #43 → "Sequence gap detected: last released run is #41 … Investigate run
#42 before releasing." ExportDetail renders it as an orange banner above the release form, so
operators see it before submitting four-eyes approval.

# Related

- [Four-Eyes Release](four-eyes-release.md)
- [ExportWorker](/pipeline/export-worker.md)
- [ExportManifest](/domain/export-manifest.md)
