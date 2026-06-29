---
type: Business Process
title: On-Demand Pipeline Run
description: API-triggered full pipeline run and read-only preview — supplements the daily scheduled export without replacing it.
resource: src/Connector.Api/Program.cs
tags: [process, pipeline, on-demand, preview, operational]
timestamp: 2026-06-28T00:00:00Z
---

In addition to the daily scheduled export ([ExportWorker](/pipeline/export-worker.md)), two
on-demand pipeline endpoints allow operators to trigger or preview an export at any time.

# Run Now

```
POST /api/pipeline/run
Authorization: Bearer <token>

200 OK — { "sequenceNo": 42, "recordCount": 5, "sha256Short": "abc123def456" }
500    — pipeline error (run is marked Failed)
```

Executes the full 6-stage pipeline immediately:

```
IErpReader → IExportFilter → IDataMinimizer → ISchemaMapper → IPackager → IExportSink
```

Creates an [ExportRun](/domain/export-run.md) record (`Status = Pending`) and writes the
staging files exactly as the scheduled worker does. The run must subsequently go through the
[Four-Eyes Release](/processes/four-eyes-release.md) process before physical transfer.

**Use cases:**
- Force a re-export after an ERP data correction, without waiting for 06:00 UTC.
- Validate the pipeline after a configuration change before the next scheduled run.
- Test a fresh connector installation end-to-end.

# Export Preview

```
GET /api/pipeline/preview
Authorization: Bearer <token>

200 OK — { "recordCount": 5, "schemaVersion": "2.0", "records": [...] }
```

Executes the pipeline through Stage 4 (SchemaMapper) only. **No files are written. No
`ExportRun` record is created.** The pipeline runs entirely in memory.

Returns the mapped records as JSON so the operator can verify data completeness and
field values before committing to a full run.

**Use cases:**
- Verify the ERP scope (which CIs are in scope, which are excluded, why).
- Confirm field values are mapped correctly after an ERP data change.
- Validate GUID population before the first production run.

# Sequencing

Both endpoints use the same sequence-number counter as the scheduled worker (`MAX(SequenceNo) + 1`).
A Run Now creates a genuine sequence-number entry in the export log — there is no concept of a
"test" sequence number. When releasing, the four-eyes authority must verify sequence continuity
regardless of whether the run was scheduled or on-demand.

# Authorization

Both endpoints require a valid JWT. Any authenticated user may trigger a Run Now or Preview.
No special role is required in Iteration 1.

# Related

- [ExportWorker](/pipeline/export-worker.md) — scheduled daily trigger (same pipeline, same logic)
- [ExportRun](/domain/export-run.md) — Run Now creates an ExportRun; Preview does not
- [Four-Eyes Release](/processes/four-eyes-release.md) — Run Now output must be released before transfer
- [Authentication](/processes/authentication.md) — JWT required
