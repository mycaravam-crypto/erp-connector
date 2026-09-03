---
type: Business Process
title: On-Demand Pipeline Run
description: API-triggered full pipeline run and read-only preview — supplements the daily scheduled export without replacing it. Plus a synchronous, API-key-friendly way to trigger a named Step 3 preset from an external system.
resource: src/Connector.Api/Program.cs
tags: [process, pipeline, on-demand, preview, operational, api-key]
timestamp: 2026-09-03T00:00:00Z
---

In addition to the daily scheduled export ([ExportWorker](/pipeline/export-worker.md)), on-demand
pipeline endpoints allow operators — or, for the preset-run endpoint below, an external system — to
trigger or preview an export at any time.

# Run Now

```
POST /api/pipeline/run
Authorization: Bearer <token>

200 OK — { "sequenceNo": 42, "recordCount": 5, "sha256Short": "abc123def456" }
500    — pipeline error (run is marked Failed)
```

Executes [DynamicExportService.BuildExportAsync](/pipeline/dynamic-export-service.md) immediately
against the active mapping — the same query+build path the scheduled worker and Preview use.

Creates an [ExportRun](/domain/export-run.md) record (`Status = Pending`) and writes the
staging files exactly as the scheduled worker does. The run must subsequently go through the
[Four-Eyes Release](/processes/four-eyes-release.md) process before physical transfer.

**Use cases:**
- Force a re-export after an ERP data correction, without waiting for 06:00 UTC.
- Validate the pipeline after a configuration change before the next scheduled run.
- Test a fresh connector installation end-to-end.

# Run a Saved Preset (external trigger)

```
POST /api/pipeline/run/{name}?format=json
Authorization: Bearer <token>        — OR —
X-Api-Key: <key>

200 OK
Content-Type: text/csv | application/json | application/vnd.openxmlformats-officedocument.spreadsheetml.sheet
Content-Disposition: attachment; filename="..."
X-Record-Count: 137

<file bytes>

404 Not Found — no preset saved under {name}
500           — pipeline error
```

`{name}` is a preset saved from Step 3's "Save As…" (`PUT /api/export-mapping/presets/{name}`) — the
same simple field/relation mapper used for the single active mapping above, just saved under a name
instead of (or in addition to) being the active one. There's no separate config surface to maintain:
design the export once in Step 3, save it as a preset, and any authenticated caller — human or machine —
can trigger it by name.

This is the one call an external system needs: authenticate, `POST .../run/{name}`, and the response
body *is* the export artifact — no polling, no staging folder to read from, no Four-Eyes Release. That's
a deliberate difference from `POST /api/pipeline/run` above: this endpoint doesn't create an `ExportRun`
and isn't subject to four-eyes, because it models a different contract (an external system pulling data
on demand) rather than the regulated CI-to-ServiceNow delivery pipeline — same reasoning
[Export Definition API](/processes/export-definition-api.md)'s `/run` already applies, though this
endpoint reuses Step 3's mapping directly rather than a separate named-definition data model. Every call
still writes one audit log entry (`export_preset_run` or `export_preset_run_failed`), so a triggered run
is never silent even though the caller only sees an HTTP error on failure.

**Authorization**: accepts either a normal user JWT, or an `X-Api-Key` header for a "dedicated API user"
that shouldn't need interactive login — see [Authentication](/processes/authentication.md#api-keys-machine-to-machine).
This is the only endpoint in the app that currently accepts the API-key scheme.

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

Run Now and the scheduled worker share the same sequence-number counter (`MAX(SequenceNo) + 1`); a Run
Now creates a genuine sequence-number entry in the export log — there is no concept of a "test" sequence
number. When releasing, the four-eyes authority must verify sequence continuity regardless of whether the
run was scheduled or on-demand. Preview and the preset-run endpoint above are both outside this counter
entirely — neither creates an `ExportRun`, so neither can create a sequence gap.

# Authorization

Run Now and Preview require a valid JWT; any authenticated user may trigger either, no special role
required in Iteration 1. The preset-run endpoint additionally accepts an API key (see above).

# Related

- [ExportWorker](/pipeline/export-worker.md) — scheduled daily trigger (same pipeline, same logic)
- [ExportRun](/domain/export-run.md) — Run Now creates an ExportRun; Preview and preset-run do not
- [Four-Eyes Release](/processes/four-eyes-release.md) — Run Now output must be released before transfer
- [Authentication](/processes/authentication.md) — JWT required; API keys for the preset-run endpoint
- [Export Definition API](/processes/export-definition-api.md) — the named/independently-scheduled sibling to preset-run, for when a mapping needs its own schedule or a tree of nested relations beyond what Step 3's simple mapper models
