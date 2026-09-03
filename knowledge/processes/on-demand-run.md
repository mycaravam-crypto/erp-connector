---
type: Business Process
title: On-Demand Pipeline Run
description: API-triggered full pipeline run and read-only preview — supplements the daily scheduled export without replacing it. Plus a synchronous, API-key-friendly way to trigger a named Step 3 preset from an external system.
resource: src/Connector.Api/Program.cs
tags: [process, pipeline, on-demand, preview, operational, api-key]
timestamp: 2026-09-03T00:00:00Z
---

Beyond the daily scheduled export ([ExportWorker](/pipeline/export-worker.md)), on-demand
endpoints let operators — or, for the preset-run endpoint, an external system — trigger or
preview an export at any time.

# Run Now

```
POST /api/pipeline/run
Authorization: Bearer <token>

200 OK — { "sequenceNo": 42, "recordCount": 5, "sha256Short": "abc123def456" }
500    — pipeline error (run is marked Failed)
```

Runs [DynamicExportService.BuildExportAsync](/pipeline/dynamic-export-service.md) immediately
against the active mapping, creates an [ExportRun](/domain/export-run.md) (`Status = Pending`),
and writes staging files exactly as the scheduled worker does. Must go through
[Four-Eyes Release](/processes/four-eyes-release.md) before physical transfer.

**Use cases:** force a re-export after an ERP data correction without waiting for 06:00 UTC;
validate the pipeline after a config change; smoke-test a fresh installation.

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

`{name}` is a preset saved from Step 3's "Save As…" (`PUT /api/export-mapping/presets/{name}`) —
the same mapper as Run Now, just saved under a name. Design once in Step 3, save as a preset, and
any authenticated caller — human or machine — triggers it by name.

Authenticate, `POST .../run/{name}`, and the response body *is* the artifact — no polling, no
staging folder, no Four-Eyes Release. Unlike `POST /api/pipeline/run`, this creates no `ExportRun`
and isn't subject to four-eyes: it models an external system pulling data on demand, not the
regulated CI-to-ServiceNow pipeline (same reasoning as
[Export Definition API](/processes/export-definition-api.md)'s `/run`, reusing Step 3's mapping
directly instead of a named-definition tree). Every call writes one audit log entry
(`export_preset_run`/`export_preset_run_failed`), so a failed run is never silent.

**Authorization**: a normal JWT, or an `X-Api-Key` header for a machine caller — see
[Authentication](/processes/authentication.md#api-keys-machine-to-machine). The only endpoint in
the app that currently accepts the API-key scheme.

# Export Preview

```
GET /api/pipeline/preview
Authorization: Bearer <token>

200 OK — { "recordCount": 5, "schemaVersion": "2.0", "records": [...] }
```

Runs the same query path as Run Now but writes nothing — no files, no `ExportRun` — entirely in
memory. Returns mapped records as JSON so the operator can check data before committing to a full
run: verify ERP scope, confirm field values after an ERP change, validate GUID population before
first production use.

# Sequencing

Run Now and the scheduled worker share one sequence-number counter (`MAX(SequenceNo) + 1`) —
every Run Now is a genuine entry, not a test one. Four-eyes must verify sequence continuity
regardless of trigger source. Preview and preset-run sit outside the counter entirely — neither
creates an `ExportRun`, so neither can create a sequence gap.

# Authorization

Run Now and Preview require a JWT; any authenticated user may trigger either, no special role.
Preset-run additionally accepts an API key (above).

# Related

- [ExportWorker](/pipeline/export-worker.md) — scheduled daily trigger, same pipeline
- [ExportRun](/domain/export-run.md) — Run Now creates one; Preview and preset-run don't
- [Four-Eyes Release](/processes/four-eyes-release.md) — required before Run Now output transfers
- [Authentication](/processes/authentication.md) — JWT required; API keys for preset-run
- [Export Definition API](/processes/export-definition-api.md) — the named/scheduled sibling to preset-run, for a mapping that needs its own schedule or deeper nesting
