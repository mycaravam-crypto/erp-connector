---
type: Architecture
title: ServiceNow Table API Provider
description: >-
  ServiceNowTableApiProvider — ServiceNow as a data source over its REST Table API (Arbeitsauftrag 9):
  components, schema mapping, query compilation, joins, retries/timeouts, security and known limits.
tags: [architecture, data-source, servicenow, rest, security]
timestamp: 2026-09-23T00:00:00Z
---

# ServiceNow Table API Provider

`DataSourceType.ServiceNowTableApi` is served by `ServiceNowTableApiProvider`, registered next to the SQL
providers in `Program.cs`. It implements `IDataSourceProvider` like them, so connection test, schema
reading and neutral `SourceQuery` execution work through the same interface; nothing outside
`Connector.Infrastructure/DataSources/ServiceNow` knows that ServiceNow is reached over REST.
`ServiceNowSqlApi` stays modeled only (no provider).

## 1. Components (`Connector.Infrastructure/DataSources/ServiceNow`)

| Type | Job |
|---|---|
| `ServiceNowTableApiProvider` | `TestConnectionAsync`, `ReadSchemaAsync`, `ExecuteAsync` (root read + joins in C#). `ExecuteNativeAsync` throws `UnsupportedDataSourceException` — there is no SQL. |
| `ServiceNowClient` | The only HTTP code: `GET /api/now/table/{table}` with Basic auth over HTTPS, pagination, per-request timeout, bounded retries, error mapping. |
| `ServiceNowSchemaReader` | Tables from `sys_db_object` (with `super_class` inheritance), fields from `sys_dictionary`, mapped to `SourceTable`/`SourceColumn`. |
| `ServiceNowQueryCompiler` | `SourceQuery` → Table API reads: encoded query (`sysparm_query`), projection (`sysparm_fields`), join steps, limit. |

## 2. Schema mapping

| ServiceNow | `SourceSchema` |
|---|---|
| `sys_db_object` row | `SourceTable` (label as description) |
| `sys_dictionary` rows of the table **and its ancestors** (`incident` gets `task`'s fields; a child's override wins) | `SourceColumn`s |
| `internal_type` | `Type` |
| `sys_id` | primary key, not nullable |
| `mandatory = true` | not nullable |
| `internal_type = reference` + `reference = <table>` | foreign key to `<table>.sys_id` — e.g. `incident.assignment_group` → `sys_user_group.sys_id` |

`ReadSchemaAsync` (connection test, schema page) reads every table the account can see. `ExecuteAsync`
reads only the tables of the query, plus their ancestors, so a query doesn't pay for the whole dictionary.

## 3. Queries

```text
SourceQuery ─ SourceQueryValidator ─ ServiceNowQueryCompiler ─→ root read ─→ join reads ─→ QueryResult
```

- **Filter.** Conditions are sent server-side as the encoded query of the table they belong to, joined with
  `^` and followed by `ORDERBYsys_id` (stable pagination): `=`, `!=`, `>`, `>=`, `<`, `<=`, `LIKE`
  (Contains), `STARTSWITH`, `ENDSWITH`, `ISEMPTY`/`ISNOTEMPTY` (null checks), `IN`. Values are formatted
  invariantly (dates `yyyy-MM-dd[ HH:mm:ss]`, `Guid` as 32 hex digits, booleans `true`/`false`).
- **Encoded-query safety.** Field names come from the validated schema (and table names must be plain
  identifiers). A value containing `^` or a line break — or `,` inside `IN` — is rejected with
  `InvalidSourceQueryException` before any request, so a value can never add a condition of its own.
  The encoded-query syntax exists only in the compiler, never in `Connector.Core` or stored configuration.
- **Projection.** `sysparm_fields` lists exactly the output columns plus join keys.
- **Pagination.** `sysparm_limit` pages of `ServiceNowClientOptions.PageSize` (1000) with `sysparm_offset`,
  until a short page or the limit.
- **Joins** run in C#. After the root read, each join reads the joined table with
  `column IN (distinct parent values)`, in batches of 100 values (one request per batch and page, never one
  per root record). Rows are matched by value, as inner or left equi-joins, in the query's join order.
  Conditions on an inner-joined table go into that table's read. Conditions on a **left**-joined table
  are rejected: filtering the joined read would change left-join semantics.
- **Limit** is sent as the root read's `sysparm_limit` unless an inner join could drop root rows; then
  the root is read fully and the joined result is cut to the limit.
- **Values.** Everything comes back as a string. An empty field (`""`, ServiceNow has no separate NULL)
  becomes `null`. `sysparm_display_value=false` gives raw values (sys_ids for references, UTC timestamps).

## 4. Resilience

| Case | Behavior |
|---|---|
| HTTP 429, 502, 503 | Retried up to `MaxRetries` (3) times. The wait is `Retry-After` if sent, otherwise 1 s, 2 s, 4 s…, capped at 30 s per wait. After the last retry: `DataSourceQueryException` with the status as `ErrorCode`. |
| HTTP 401 | Not retried. "ServiceNow rejected the credentials (HTTP 401)." |
| HTTP 403 | Not retried. "…the account lacks read access (ACL/role) to this table." |
| HTTP 400 (invalid query), other errors | Not retried. ServiceNow's `error.message` is appended. |
| Request timeout | `TimeoutException` after `RequestTimeout` (30 s) per request. |
| Cancellation | The caller's `CancellationToken` aborts the request and any retry wait (`OperationCanceledException`). |

## 5. Security

- **HTTPS only.** `ServiceNowClient.ParseInstanceUrl` refuses anything but an absolute `https://` URL,
  both at save time (`ConnectionEndpoints.ValidateInstanceUrl`) and before every request.
  `POST /api/connection` also runs the SSRF host check on the instance URL's host.
- **Credentials** travel only in the `Authorization` header. They are never part of a URL, a log or an
  error message. `DataSourceConfig.ToString()` omits the password, and connection-test failures go
  through `ErrorSanitizer`.
- **Least privilege.** No admin role is needed. The account needs read access to the tables it exports
  and to `sys_db_object`/`sys_dictionary` for the schema (e.g. the `personalize_dictionary` role). Every
  read goes through the Table API, so ServiceNow's ACLs apply: a table the account may not read fails
  with 403, and rows or fields ACLs hide are simply not returned.
- **No direct database (RaptorDB) access** and no write calls: the provider only issues `GET`s.

## 6. Known limits

- **Exports.** The export builders (`DynamicExportService`: legacy mappings and `ExportNode` trees) still
  render SQL through `ISqlDialect`, so an export against a ServiceNow connection fails with
  `UnsupportedDataSourceException` ("does not support SQL export queries"). ServiceNow can be connected,
  browsed (schema) and queried through `SourceQuery`. Moving the export trees onto `SourceQuery` is the
  follow-up described in [Source Query Model §5](/architecture/source-query-model.md).
- **Imports** are PostgreSQL-only, as for MariaDB.
- `LIKE`/`STARTSWITH`/`ENDSWITH` follow ServiceNow's matching, which is case-insensitive. The model asks
  for case-sensitive matching, so the result may contain more rows.
- A full schema read is one paginated pass over `sys_db_object` and `sys_dictionary`. On a large instance
  that is several thousand tables, so the connection test takes correspondingly long.

## 7. Tests

`ServiceNowTableApiProviderTests` run against `FakeServiceNow`, an in-memory Table API
(`HttpMessageHandler`) that implements the encoded-query subset, projection, pagination, Basic auth,
ACL denial and scripted failures. No real instance or credentials are used in CI. They cover:
authentication (success, 401 without retry or password echo, 403, plain-HTTP refusal), table metadata
with inherited fields, reference fields as relations, a simple query with empty-as-null, projection,
server-side filter, an injection-shaped value rejected before any read, pagination, limit, left/inner
join with one batched read, unknown table/column, no native SQL, retry on 429/502/503, giving up after
`MaxRetries`, no retry on 400, request timeout and cancellation. `ConnectionEndpointsHttpTests` cover
the HTTPS and SSRF checks on the instance URL.
