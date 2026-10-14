---
type: API
title: Selected-record export and import
description: Trigger targeted record exports and stage one or many imports through the existing definitions.
---

# Selected-record REST API

## Export

`POST /api/export-definitions/{id}/run-selected` uses the same JWT authorization and `ExportJobsDownload` permission as the existing `/run` endpoint.

```http
POST /api/export-definitions/12/run-selected
Authorization: Bearer <token>
Content-Type: application/json

{"recordIds":["4711","4712"]}
```

The saved definition **must have** `CorrelationKeySourceField` pointing to an enabled root scalar field. Configure this field as a unique key in the source schema. The API accepts 1–100 distinct non-blank IDs (max 256 characters each). IDs are bound as query parameters; the predicate is joined with **AND** to the saved root filter. No caller-supplied SQL, table or column is accepted. Invalid selection: 400. No records: 404. Successful response: the same file, run-history headers and audit event as `/run`. Existing `/run`, previews and scheduled jobs remain unchanged.

## Import

`POST /api/import-definitions/{id}/records` requires a JWT and `ImportJobsRun` permission.

```http
POST /api/import-definitions/12/records
Authorization: Bearer <token>
Content-Type: application/json

{"records":[{"guid":"a12b34c5","status":"Active"}]}
```

The endpoint validates 1–100 JSON objects (serialized envelope limit 1 MiB), adds `schemaVersion: "1"` and the saved import definition name, and delegates to **the same staging path** as `POST /api/import-definitions/{id}/runs`. It returns `201 Created` with the `ImportRun` summary (status `PendingReview`) and location `/api/import-runs/{runId}`. Exact repeat submissions use existing checksum deduplication (`409 Conflict`). No ERP write occurs until the separate four-eyes review endpoint approves the run. Root-match policies, writable-column allowlist, snapshot, audit and optimistic concurrency stay unchanged.

## Security and operations

JWT/RBAC, request limits, GDPR denylist, audit and import review remain mandatory. Selected IDs are not recorded in logs; only the request count is audited. Query values use the provider's parameter binding (`ISqlDialect.BuildMatchesAny`) to avoid SQL injection. Raw export filters are a separate legacy mechanism and must not be derived from API input.

**Rollout:** no database migration or change to existing integration contracts. When the selection API is not used, behavior is unchanged.
