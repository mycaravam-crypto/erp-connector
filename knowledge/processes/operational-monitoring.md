---
type: Business Process
title: Operational Monitoring
description: Health checks, stale-pending detection, and sequence gap detection that support the daily operator workflow.
tags: [process, monitoring, health, observability]
timestamp: 2026-06-29T00:00:00Z
---

Three lightweight mechanisms were added in Phase 6 to make the daily export cycle safer and
more observable without requiring an external monitoring stack.

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

The `GET /api/exports` list includes `isStale: true` on any run whose status is `Pending`
and whose `ExtractedAt` timestamp is more than 24 hours old.

The ExportView renders:
- An amber callout banner at the top of the run list when any stale run exists.
- An inline "overdue" tag next to the status badge on the stale row.

This surfaces missed releases without requiring a separate alerting system.

# Sequence Gap Detection

`GET /api/exports/{seqNo}` includes a `sequenceGapWarning` string (null when no gap).

A gap is detected when a Pending run's sequence number is not the immediate successor of
the last Released run. Example: if the last released run is #41 and run #43 is Pending,
the warning reads: "Sequence gap detected: last released run is #41 … Investigate run #42 before releasing."

The ExportDetail view renders this as a left-bordered orange banner above the release form,
so operators see the warning before they submit the four-eyes approval.

# Related

- [Four-Eyes Release](four-eyes-release.md)
- [ExportWorker](/pipeline/export-worker.md)
- [ExportManifest](/domain/export-manifest.md)
