---
type: Business Process
title: Export Definition API — configure once, trigger from anywhere
description: CRUD, manual trigger, test, preview, and run history for saved, named export definitions (Phase 14 Slice 3) — the API surface an external program uses to run a saved export on demand.
resource: src/Connector.Api/Endpoints/ExportDefinitionEndpoints.cs
tags: [process, api, export-definitions, on-demand, phase-14]
timestamp: 2026-08-27T00:00:00Z
---

An [ExportDefinition](/pipeline/export-definitions-2.0.md) is a saved, named export configuration
(root table, field/relation tree, output format) that can be triggered independently of the legacy
single-mapping [On-Demand Run](/processes/on-demand-run.md). Configure it once through this API, then have any
authenticated external program trigger it later with a single call — no UI required.

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
`ConfigVersion`. `GET /api/export-definitions` lists saved definitions (summary — no tree);
`GET /api/export-definitions/{id}` returns one definition in full, including its `rootNode` tree.
`DELETE /api/export-definitions/{id}` removes it (run history rows are kept for traceability).
`POST /api/export-definitions/{id}/duplicate` copies a definition (starts disabled, manual-only —
see [ExportNode tree](/pipeline/export-definitions-2.0.md)). `PATCH /api/export-definitions/{id}/enable`
flips `isEnabled` (governs future scheduling once [Slice 4](/pipeline/export-definitions-2.0.md)
ships; it does not gate manual triggering below).

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

404 Not Found  — no definition with this id
500           — pipeline error (run is marked Failed; see Run History)
```

This is the one call an external system needs: authenticate, `POST .../run`, and the response body
*is* the export artifact in the definition's configured `outputFormat` — no polling, no separate
download step, no staging folder to read from. Every call — success or failure — writes exactly one
[run-history](#run-history) row, so a failed run is never silent even though the caller only sees an
HTTP error.

Unlike [On-Demand Run](/processes/on-demand-run.md)'s `POST /api/pipeline/run`, this does **not** create an
`ExportRun`, does not write to the staging folder, and is not subject to
[Four-Eyes Release](/processes/four-eyes-release.md) — those model the legacy CI-to-ServiceNow delivery
contract specifically, which generic export definitions are explicitly out of scope for (see
[Export Definitions 2.0 §10](/pipeline/export-definitions-2.0.md)). A definition's run is
synchronous request/response only.

# Test and preview

```
POST /api/export-definitions/{id}/test         # full pipeline, capped at 50 rows, tracked in history
Authorization: Bearer <token>
200 OK — { "runId": 43, "status": "Success", "recordCount": 50, "configVersion": 3,
           "startedAt": "...", "finishedAt": "...", "errorMessage": null, "isTestRun": true }
```

`test` runs the exact same query/format-writer path as `run` (so a passing test is a reliable
predictor of what `run` will do), capped to 50 rows and flagged `isTestRun` in its history row, but
returns the tracked run record as JSON rather than the artifact bytes — its purpose is to confirm
the configuration works, not to hand back data.

```
POST /api/export-definitions/{id}/preview
Authorization: Bearer <token>
200 OK — { "recordCount": 12, "records": [ { "name": "...", "addresses": [...] }, ... ] }
```

`preview` returns capped, tree-shaped JSON rows for on-screen inspection. It writes **no** run-history
row — use it while building a definition; use `test` to validate a saved one.

# Run History

```
GET /api/export-definitions/{id}/runs
Authorization: Bearer <token>
200 OK — [ { "id": 43, "configVersion": 3, "startedAt": "...", "finishedAt": "...",
             "status": "Success", "recordCount": 50, "errorMessage": null,
             "triggeredBy": "alice", "isTestRun": true }, ... ]
```

Every `run` and `test` call — never `preview` — writes exactly one `ExportDefinitionRunEntity` row
up front (`Status = Running`), updated to `Success` or `Failed` when the call completes. A crash mid
-query still leaves a `Failed` row with a specific `errorMessage`, matching this codebase's
"no silent partial success" convention (see [ExportRun](/domain/export-run.md) for the equivalent
guarantee on the legacy pipeline). Rows are returned newest-first, capped at 200.

# Validation

`POST`/`PUT` reject a definition whose `rootNode` tree fails any of:

* `rootNode.kind` must be `"root"`.
* Every identifier that reaches the SQL query builder — `rootTable`, and each node's `sourceField` /
  `relatedTable` / `joinKey` / `sourceJoinKey` — must match `^[A-Za-z_][A-Za-z0-9_]*$`.
* Nesting depth is capped (`DynamicExportService.MaxNestedDepth`, shared with the legacy nested-JSON
  path).
* No two enabled sibling nodes may share a `targetKey` at the same level.
* Every `scalar-field` node's `sourceField` is checked against the GDPR denylist (see
  [GDPR Compliance](/processes/gdpr-compliance.md)) — the same rule already enforced on the legacy
  `/api/export-mapping` save path, applied here at every nesting depth.
* `outputFormat` must be `csv`, `xlsx`, or `json`; `schedule`, if set, must be a 5-field cron string
  (not yet interpreted by a scheduler — see [Export Definitions 2.0](/pipeline/export-definitions-2.0.md)
  Slice 4).

A node's `filter` (an optional WHERE-clause fragment) is deliberately **not** identifier-validated —
it's free-form SQL by design, scoped to that node's own subquery, spliced in the same way
`ExportNode.Filter` already works in the query engine. Only authenticated users can save a
definition, the same trust boundary the legacy mapping-save endpoint already relies on.

# Authorization

Every endpoint above requires a valid JWT. Any authenticated user may configure or trigger an export
definition — no special role, matching [On-Demand Run](/processes/on-demand-run.md)'s existing model.

# Related

- [Export Definitions 2.0](/pipeline/export-definitions-2.0.md) — full data model and design spec this API implements (Slice 3)
- [DynamicExportService](/pipeline/dynamic-export-service.md) — query/format-writer engine this API drives
- [On-Demand Run](/processes/on-demand-run.md) — the legacy single-mapping equivalent, kept separate by design
- [GDPR Compliance](/processes/gdpr-compliance.md) — denylist enforcement referenced above
