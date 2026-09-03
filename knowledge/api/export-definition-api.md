---
type: Business Process
title: Export Definition API — configure once, trigger from anywhere
description: CRUD, manual trigger, test, preview, and run history for saved, named export definitions (Phase 14 Slice 3) — the API surface an external program uses to run a saved export on demand.
resource: src/Connector.Api/Endpoints/ExportDefinitionEndpoints.cs
tags: [process, api, export-definitions, on-demand, phase-14]
timestamp: 2026-08-27T00:00:00Z
---

An [ExportDefinition](/pipeline/export-definitions-2.0.md) is a saved, named export configuration
(root table, field/relation tree, output format), triggerable independently of the legacy
single-mapping [On-Demand Run](/api/on-demand-run.md). Configure it once via this API, then
have any authenticated external program trigger it later with a single call — no UI required.

# Configuring an export

```
POST /api/export-definitions
Authorization: Bearer <token>
Content-Type: application/json

{
  "name": "Weekly Manufacturer Extract",
  "description": "Manufacturers with their addresses, CSV",
  "rootTable": "manufacturer",
  "outputFormat": "csv",
  "isEnabled": true,
  "schedule": null,
  "rootNode": {
    "targetKey": "root",
    "kind": "root",
    "children": [
      { "targetKey": "name", "kind": "scalar-field", "sourceField": "name", "enabled": true },
      {
        "targetKey": "addresses", "kind": "array", "enabled": true,
        "relatedTable": "manufacturer_address", "joinKey": "manufacturer_id", "sourceJoinKey": "id",
        "children": [
          { "targetKey": "city", "kind": "scalar-field", "sourceField": "city", "enabled": true }
        ]
      }
    ]
  }
}

201 Created — full ExportDefinitionDto, including the assigned "id"
400 Bad Request — validation error (see Validation below)
```

`PUT /api/export-definitions/{id}` re-validates and replaces the definition, bumping
`ConfigVersion`. `GET /api/export-definitions` lists definitions (summary, no tree);
`GET .../{id}` returns one in full including its `rootNode` tree. `DELETE .../{id}` removes it
(run history stays for traceability). `.../duplicate` copies a definition, starting disabled and
manual-only. `PATCH .../enable` flips `isEnabled` — governs [scheduled
runs](/dynamic-export/scheduler.md); doesn't gate manual triggering, which works regardless of
`isEnabled`.

# Triggering an export from an external program

```
POST /api/export-definitions/{id}/run
Authorization: Bearer <token>

200 OK
Content-Type: text/csv | application/json | application/vnd.openxmlformats-officedocument.spreadsheetml.sheet
Content-Disposition: attachment; filename="..."
X-Export-Run-Id: 42
X-Record-Count: 137
X-Config-Version: 3

<file bytes>

404 Not Found — no definition with this id
500           — pipeline error (run is marked Failed; see Run History)
```

Authenticate, `POST .../run`, and the response body *is* the artifact in the configured
`outputFormat` — no polling, no staging folder. Every call writes exactly one
[run-history](#run-history) row, success or failure, so a failed run is never silent even though
the caller only sees an HTTP error.

Unlike [On-Demand Run](/api/on-demand-run.md)'s `POST /api/pipeline/run`, this creates no
`ExportRun`, writes nothing to the staging folder, and isn't subject to
[Four-Eyes Release](/operations/four-eyes-release.md) — those model the CI-to-ServiceNow delivery
contract specifically, out of scope for generic definitions (see
[Export Definitions 2.0 §10](/pipeline/export-definitions-2.0.md)). A run is synchronous
request/response only.

# Test and preview

```
POST /api/export-definitions/{id}/test         # full pipeline, capped at 50 rows, tracked in history
Authorization: Bearer <token>
200 OK — { "runId": 43, "status": "Success", "recordCount": 50, "configVersion": 3,
           "startedAt": "...", "finishedAt": "...", "errorMessage": null, "isTestRun": true }
```

`test` runs the exact same query/format-writer path as `run` (a passing test reliably predicts
what `run` will do), capped to 50 rows and flagged `isTestRun`, but returns the tracked run
record as JSON instead of artifact bytes.

```
POST /api/export-definitions/{id}/preview
Authorization: Bearer <token>
200 OK — { "recordCount": 12, "records": [ { "name": "...", "addresses": [...] }, ... ] }
```

`preview` returns capped, tree-shaped JSON for on-screen inspection and writes **no** run-history
row — use it while building a definition; use `test` to validate a saved one.

# Run History

```
GET /api/export-definitions/{id}/runs
Authorization: Bearer <token>
200 OK — [ { "id": 43, "configVersion": 3, "startedAt": "...", "finishedAt": "...",
             "status": "Success", "recordCount": 50, "errorMessage": null,
             "triggeredBy": "alice", "isTestRun": true }, ... ]
```

Every `run`/`test` call — never `preview` — writes one `ExportDefinitionRunEntity` row up front
(`Status = Running`), updated to `Success`/`Failed` on completion; a mid-query crash still leaves
a `Failed` row with a specific `errorMessage` (same "no silent partial success" convention as
[ExportRun](/domain/export-run.md)). Rows are newest-first, capped at 200.

# Validation

`POST`/`PUT` reject a `rootNode` tree that fails any of:

* `rootNode.kind` must be `"root"`.
* Every identifier reaching the SQL builder — `rootTable`, `sourceField`/`relatedTable`/`joinKey`/
  `sourceJoinKey` on each node — must match `^[A-Za-z_][A-Za-z0-9_]*$`.
* Nesting depth is capped (`DynamicExportService.MaxNestedDepth`, shared with the legacy nested-JSON path).
* No two enabled sibling nodes share a `targetKey` at the same level.
* Every scalar-field `sourceField` is checked against the [GDPR denylist](/operations/gdpr-compliance.md), at every depth.
* `outputFormat` is `csv`/`xlsx`/`json`; `schedule`, if set, must be a 5-field cron string — read
  and interpreted every minute by [the scheduler](/dynamic-export/scheduler.md) once `isEnabled`
  is also true.

A node's `filter` (an optional WHERE-clause fragment, scoped to that node's subquery) is
deliberately **not** identifier-validated — it's free-form SQL by design. Only authenticated
users can save a definition, the same trust boundary as the legacy mapping-save endpoint.

# Authorization

Every endpoint requires a valid JWT. Any authenticated user may configure or trigger a
definition — no special role, matching [On-Demand Run](/api/on-demand-run.md).

# Related

- [Export Definitions 2.0](/pipeline/export-definitions-2.0.md) — full data model and spec
- [Dynamic Export](/dynamic-export/index.md) — the `ExportNode` tree, scheduler, and run-history entity this API is a surface over
- [DynamicExportService](/pipeline/dynamic-export-service.md) — query/format-writer engine
- [On-Demand Run](/api/on-demand-run.md) — the legacy single-mapping equivalent
- [GDPR Compliance](/operations/gdpr-compliance.md) — denylist enforcement referenced above
