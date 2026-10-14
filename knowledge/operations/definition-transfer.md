---
type: Business Process
title: Transfer Import/Export Definitions Between Connector Instances
description: Move saved mapping configurations between separate X5 Connector installations using the existing REST API, with validation and safe activation.
tags: [operations, configuration, import, export, transfer, deployment]
timestamp: 2026-10-14T00:00:00Z
---

# Transfer import/export definitions between connector instances

This procedure moves **saved job configurations** from connector instance A to instance B.
It does **not** move ERP records, export artifacts, manifests, credentials, run histories, or
audit logs. For moving *data* between two connectors via JSON files and a manifest, see
[Connector Alignment Trial](connector-alignment-trial.md).

## 1. Current support and limits

The existing authenticated REST API provides a full definition read and a create/update operation
for both job types:

| Job type | Read full configuration | Create on target | Update an existing target |
| --- | --- | --- | --- |
| Export | `GET /api/export-definitions/{id}` | `POST /api/export-definitions` | `PUT /api/export-definitions/{id}` |
| Import | `GET /api/import-definitions/{id}` | `POST /api/import-definitions` | `PUT /api/import-definitions/{id}` |

The list endpoints (`GET /api/export-definitions` and `GET /api/import-definitions`) return
summaries **without** the node tree. Always fetch each definition individually before copying it.

**Not implemented:** a UI action to download/upload definitions; a portable multi-definition
bundle; automatic cross-instance synchronization, matching, merging, or rollback. The
`/duplicate` actions duplicate definitions **within one instance only**. A data export
(`POST /api/export-definitions/{id}/run`) produces ERP data, **not** an exportable job definition.

The endpoints require separate JWT bearer tokens from each instance and the relevant
`ExportJobsView` / `ExportJobsCreate` or `ImportJobsView` / `ImportJobsCreate` permissions.
For updates on B, use the matching Edit permission. API keys are not supported for these
configuration endpoints. See [Authentication](/api/authentication.md).

### Prerequisites

1. A and B run compatible connector versions and their relevant source/target schemas have been
   reviewed; a definition does not carry or create its ERP connection.
2. The operator has access to both instances via HTTPS and has `curl` and `jq` installed.
3. Obtain a JWT for **each** instance (`POST /api/auth/login`). Keep tokens out of shell history,
   shared files, repositories, and logs.
4. Inventory definitions already present on B, their names, `integrationKey` / `contractVersion`
   pairs, and whether they are enabled. The API does **not** provide an automatic upsert or
   collision-resolution policy.

In the examples below, replace the URLs and numeric IDs. Supply `TOKEN_A` and `TOKEN_B` securely
as environment variables; neither value should appear in the saved JSON files:

```bash
A="https://connector-a.example.com"
B="https://connector-b.example.com"
# TOKEN_A and TOKEN_B are the respective JWT values.
```

## 2. Copy an export definition

Export the *configuration* of definition 12 from A. The `jq` projection removes source-local
IDs, configuration version, and audit metadata. It deliberately disables execution and clears
the cron schedule on B until the mapping has been verified:

```bash
curl --fail-with-body --silent --show-error \
  -H "Authorization: Bearer $TOKEN_A" \
  "$A/api/export-definitions/12" |
jq '{
  name,
  description,
  rootTable,
  rootNode,
  outputFormat,
  isEnabled: false,
  schedule: null,
  integrationKey,
  contractVersion,
  correlationKeySourceField,
  targetImportDefinition
}' > export-definition.json
```

Inspect `export-definition.json` before sending it. In particular, check `rootTable`, field
names, joins, optional SQL filters, the GDPR denylist on B, and
`targetImportDefinition` (which must name B's **actual** receiving import job when used).

Create a **new** export definition on B:

```bash
EXPORT_ID_B=$(
  curl --fail-with-body --silent --show-error \
    -X POST "$B/api/export-definitions" \
    -H "Authorization: Bearer $TOKEN_B" \
    -H "Content-Type: application/json" \
    --data-binary @export-definition.json |
  jq -r '.id'
)
echo "Created export definition on B: $EXPORT_ID_B"
```

B assigns a new ID, `configVersion = 1`, and new creation/audit metadata. The imported job is
disabled and has no schedule. After checking the target ERP connection and schema, run an
untracked preview or a tracked test (capped at 50 records):

```bash
curl --fail-with-body --silent --show-error \
  -X POST -H "Authorization: Bearer $TOKEN_B" \
  "$B/api/export-definitions/$EXPORT_ID_B/test"
```

Only then configure the intended schedule and enable the job using the target UI or API.

## 3. Copy an import definition

Import definitions are **write-side** configurations. In addition to the mapping tree, they
contain the root match column, the allowed writable columns, and the policy for unmatched rows.
Copy definition 7 from A, leaving it disabled on B:

```bash
curl --fail-with-body --silent --show-error \
  -H "Authorization: Bearer $TOKEN_A" \
  "$A/api/import-definitions/7" |
jq '{
  name,
  description,
  rootTable,
  rootMatchColumn,
  rootNode,
  allowedWritableColumns,
  unmatchedRootPolicy,
  isEnabled: false,
  integrationKey,
  contractVersion
}' > import-definition.json
```

Review `rootTable`, `rootMatchColumn`, every `targetColumn` in `rootNode`, the
`allowedWritableColumns` allowlist, and `unmatchedRootPolicy` before creating it:

```bash
IMPORT_ID_B=$(
  curl --fail-with-body --silent --show-error \
    -X POST "$B/api/import-definitions" \
    -H "Authorization: Bearer $TOKEN_B" \
    -H "Content-Type: application/json" \
    --data-binary @import-definition.json |
  jq -r '.id'
)
echo "Created import definition on B: $IMPORT_ID_B"
```

Unlike the export side, the import save endpoint validates writable columns and required
mappings against **B's live ERP schema**. A missing connection, missing column, forbidden
primary/identity/foreign-key target, GDPR-denied field, or incomplete insert mapping can cause
`400 Bad Request`. Do not bypass this validation by modifying the connector's internal SQLite
database. A successful create does not execute a write; use the target UI's preview with a
representative inbound sample, inspect the proposed diff, and enable only after review.
Actual imports still require the existing four-eyes release procedure.

## 4. Updating and reconciling target definitions

* `POST` always requests **creation**; it is not a portable "import-or-update" operation. First
  compare B's list and inspect any candidate by its own `GET .../{id}`.
* If the job already exists, use `PUT /api/{export|import}-definitions/{target-id}` with the
  respective **projected request JSON**, not the source ID or the unmodified GET response.
  `PUT` replaces the configuration and increments the target's `configVersion`.
* Internal IDs and `configVersion` are instance-local; do not use them as cross-instance
  identifiers. For connector-to-connector data flows, check that the paired `integrationKey`,
  `contractVersion`, and (for JSON exports) `targetImportDefinition` are compatible.
* Each enabled definition claiming an `integrationKey` / `contractVersion` pair must satisfy the
  application's uniqueness validation. Resolve conflicts before enabling the copied job.
* Do **not** blindly replace B-specific root tables, target columns, allowlists, schedules, or
  running jobs. Review the target diff and retain a backup of the previous configuration.
* This procedure concerns first-class import/export jobs, not legacy mapping presets or global
  application settings. Copying the internal database is not an equivalent safe transfer.

### Operational verification

| Check | Expected result |
| --- | --- |
| GET target definition | Mapping and transfer-relevant settings match the reviewed request |
| Export test on B | `Success` with the expected output shape (otherwise investigate the error) |
| Import save and preview on B | Target schema accepts the configuration; diff contains only intended writes |
| Newly created definition | `isEnabled = false`; export `schedule = null` |
| Audit | Create/update actions are attributed to the operator on B |
| Activation | Explicit operator decision after review; no unexpected scheduled run |

Treat configuration files as sensitive operational artifacts: optional filters and mappings may
contain business details even though the definition DTOs do not include stored connection
credentials. Restrict access, transfer over an approved channel, verify file integrity, and
remove temporary copies according to local retention policy.

## 5. Proposed native feature (NOT IMPLEMENTED)

A future iteration could provide a versioned, portable configuration bundle and a UI workflow.
This is a **proposal**, not a description of endpoints available today.

* `POST /api/definitions/export`: select multiple import/export job IDs and return a
  `formatVersion`-tagged JSON bundle without IDs, audit metadata, execution history, or secrets.
* `POST /api/definitions/import?dryRun=true`: validate the format and target schema; return
  create/update/skip conflicts, field-level differences, unresolved dependencies, and validation
  failures **without writing anything**.
* `POST /api/definitions/import?dryRun=false`: require explicit per-definition conflict decisions,
  execute an audited all-or-nothing configuration transaction, and create jobs disabled (export
  schedules cleared). Require separate permissions for read/validate/apply.
* Validate unknown bundle versions, tampering, duplicates, invalid mappings, disallowed fields,
  unavailable source tables, and collisions in integration key/contract version pairs. An integrity
  hash detects accidental corruption; if an untrusted transport is in scope, add a trusted
  signature to authenticate the bundle's origin.
* Provide test coverage for round-trip fidelity, partial failure/rollback, dry-run side effects,
  authorization, source/target schema differences, no-secret export, and safe default activation.

This enhancement can be delivered incrementally; the REST-API procedure above works **today**
without changes to the connector.

## References

- [Export Definition API](/api/export-definition-api.md)
- [Import Definitions](/pipeline/import-definitions.md)
- [Connector Alignment Trial](connector-alignment-trial.md)
- [Roles and Permissions](/security/roles-and-permissions.md)
