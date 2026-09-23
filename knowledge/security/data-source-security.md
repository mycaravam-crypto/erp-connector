---
type: Security
title: Data Source Security
description: >-
  Security review of the multi-source data access layer (Arbeitsauftrag 11): threats, the controls in
  place for PostgreSQL, MariaDB and ServiceNow, residual risks, and which tests cover what.
tags: [security, data-source, postgres, mariadb, servicenow, gdpr, tls]
timestamp: 2026-09-23T00:00:00Z
---

# Data Source Security

Scope: everything between the connector and a source system. That covers connection configuration,
the three providers ([MariaDB](/architecture/mariadb-provider.md),
[ServiceNow](/architecture/servicenow-provider.md), PostgreSQL), the export query builders, and how
errors and logs describe them.

## 1. Threats

| # | Threat | Where it would enter |
|---|---|---|
| T1 | SQL injection through an identifier (table, column, join field) | `SourceQuery`, `ExportNode`/legacy mapping configs |
| T2 | SQL injection through a value (filter value, `IN` list) | `SourceQuery` conditions, tree key batches |
| T3 | Encoded-query injection (ServiceNow): a value adds its own `^OR…`/`^NQ…` condition | `SourceQuery` conditions, table names |
| T4 | Free SQL in a stored `ExportNode.Filter` | Export Definition save |
| T5 | Credential leak through logs, exceptions, API responses, audit events, health checks, debug output, connection strings | Every error path |
| T6 | Personal data (GDPR denylist) read from the source at all | Export trees at any depth |
| T7 | Plaintext transport of credentials and data | PostgreSQL/MariaDB TLS mode, ServiceNow URL |
| T8 | SSRF: the connection target pointed at cloud metadata or another internal service | `POST /api/connection` |
| T9 | Over-privileged or misbehaving access to ServiceNow (admin role, retry storms) | ServiceNow provider |

## 2. Controls

### SQL and encoded-query injection (T1–T4)

- **Identifiers come from the introspected schema.** `SourceQueryValidator` rejects any table or column
  (projection, condition, join field, join parent) the live schema didn't report, before any SQL is
  built. Each provider reads the schema fresh for every `ExecuteAsync`.
- **Identifiers are validated and quoted by the dialect.** Export configs are checked at save time
  (`SqlIdentifierRegex`: letters, digits and `_`). Every identifier is quoted by the provider's
  `ISqlDialect.QuoteIdentifier` (PostgreSQL `"…"`, MariaDB `` `…` ``), which doubles embedded quotes.
- **Values are always parameters.** Both SQL compilers bind every condition value (`@p0`…) and bind
  one parameter per `IN` value. The tree engine's key batches are bound too (a `text[]` in PostgreSQL,
  one parameter per key in MariaDB). `LIMIT` is an `int` rendered invariantly.
- **ServiceNow.** Field names come from the schema, and table names must be plain identifiers. A value
  containing `^` or a line break, or `,` inside `IN`, is rejected (`InvalidSourceQueryException`), not
  escaped. The encoded-query syntax lives only in `ServiceNowQueryCompiler`.
- **Sort fields.** Neither `SourceQuery` nor the export configs have an `ORDER BY`/sort field, so there
  is no sort-field input to inject through. The only ordering is ServiceNow's constant `ORDERBYsys_id`.
- **`ExportNode.Filter`** (free SQL by design) is screened at save time. The screen allows a safe
  character set and rejects comments, `;`, `$$`, statement keywords, and any function call except
  `AND`/`OR`/`NOT`/`IN` grouping (so `SLEEP(`, `pg_sleep(`, `LOAD_FILE(` fail too). It isn't a parser —
  see §3.

### Credential leakage (T5)

| Channel | Control |
|---|---|
| API responses | `GET /api/connection` has no password field (`hasPassword` only). Connection-test and preview errors go through `ErrorSanitizer.Detail`. An empty password on save keeps the stored one only for an unchanged target and account. |
| Exceptions | `ErrorSanitizer` scrubs `password=`/`pwd=` fragments and HTTP `Basic` credentials. ServiceNow errors are composed by the client and never include the `Authorization` header. |
| Logs | Every log line is formatted through `SanitizingLogFormatter`. It scrubs the exception text (including inner exceptions and stack), every string property, and drops destructured `Password` properties. It keeps the JSON output valid. |
| Audit events | `AuditService.LogAsync` scrubs every detail before storing it. The two call sites that put exception text into audit details (`export_failed`, `export_preset_run_failed`) use `ErrorSanitizer.Detail`. |
| Health checks | `/api/health` reports only `log_db`/`staging` booleans; nothing about the source connection. |
| Debug output | `DataSourceConfig.ToString()` omits the password and reports `HasPassword`. |
| Connection strings | Built with `NpgsqlConnectionStringBuilder`/`MySqlConnectionStringBuilder` typed properties (no key injection). They are never logged or returned. The config is encrypted at rest (Data Protection). |
| Run history | Export and import run errors are stored as `ErrorSanitizer.Detail(ex)`. |

### GDPR (T6)

The denylist is keyed on the source column (`SourceField`/`SourceName`), not the output name, and is
applied when the query plan is built. A denied column is left out of the `SELECT` of the root, of every
child node and of every nested level, so it never leaves the source. `StripGdprFieldsRecursive`
removes matching output keys again at every depth, as defense in depth. It is re-evaluated against the
current denylist on every run.

### Transport security (T7)

- **PostgreSQL/MariaDB:** `SslMode` `Require`/`VerifyCA`/`VerifyFull` makes TLS mandatory. In
  **production**, `POST /api/connection` refuses unset/`Prefer`/`Allow`/`Disable` (they can fall back
  to plaintext) unless `DataSources:AllowUnencryptedConnections=true` explicitly opts out
  (`appsettings.Production.json` ships `false`). Outside production, plaintext stays allowed by
  default for the local test databases. At startup, a stored connection that isn't always encrypted is
  logged as a warning.
- **ServiceNow:** HTTPS only. The instance URL is checked at save time and before every request;
  plain HTTP is never sent.

### SSRF (T8)

`POST /api/connection` resolves the target host — `Host` for SQL sources, the instance URL's host for
ServiceNow — and refuses link-local/metadata addresses (`169.254.0.0/16`, `fe80::/10`).

### ServiceNow authorization and behavior (T9)

- **Least privilege:** read access to the exported tables plus `sys_db_object`/`sys_dictionary` (e.g.
  `personalize_dictionary`). No admin role, no write calls, no direct database access.
- **ACLs:** every read goes through the Table API. A denied table is reported as HTTP 403 with a clear
  message and is not retried. ACL-hidden rows and fields are simply absent.
- **Bounded retries:** only 429/502/503, at most 3 retries, `Retry-After` capped at 30 s. Never on
  401/403/400. 30 s per-request timeout. Cancellation is honored.

## 3. Residual risks

| Risk | Why it remains | Mitigation |
|---|---|---|
| `ExportNode.Filter` is still free SQL | Replacing it needs structured conditions, a stored-data migration and a UI change ([Source Query Model §5](/architecture/source-query-model.md)) | Admin-only, screened at save time, runs with the connector's (ideally read-only) DB account |
| A connection saved before the production TLS rule keeps working unencrypted | Refusing it at runtime would stop existing exports without warning | Startup warning; re-saving applies the rule |
| TLS `Require` doesn't verify the server certificate | It's the operator's choice; `VerifyCA`/`VerifyFull` do | Documented in the UI help text |
| A denied column used as a **join key** is still selected | The join can't be computed without it | It's used only for grouping and never written to the output |
| Log sinks added via `Serilog` configuration (`ReadFrom.Configuration`) bypass `SanitizingLogFormatter` | Only the console sink is wired in code | None configured today; wrap any new sink the same way |
| ServiceNow `LIKE`/`STARTSWITH` match case-insensitively | ServiceNow's semantics | Documented; may return more rows, never other tables' data |
| Npgsql/MySqlConnector messages may contain host/database/user names | They're diagnostic, not secret | Only passwords and Basic credentials are scrubbed |

## 4. Test coverage

| Area | Tests |
|---|---|
| Injection: table, column, join field, filter value, `IN` values, export-tree identifier (PostgreSQL **and** MariaDB, real DBs) | `DataSourceSecurityTests` |
| Injection: ServiceNow values and table names | `ServiceNowTableApiProviderTests` (`…EncodedQuerySeparator…`, `…TableNameWithEncodedQuerySyntax…`) |
| Identifier quoting and parameter binding | `PostgreSqlDialectTests`, `MariaDbDialectTests`, `PostgreSqlQueryCompilerTests`, `MariaDbQueryCompilerTests` |
| Filter screening | `ExportFilterValidationTests` |
| Credential scrubbing (exceptions, Basic auth, logs, JSON validity) | `ErrorSanitizerTests`, `SanitizingLogFormatterTests` |
| Password never in GET / kept only for the same target | `ConnectionEndpointsHttpTests`, `ConnectionEndpointsPasswordRetentionTests` |
| Connection-string injection | `PostgreSqlDataSourceProviderTests`, `MariaDbDataSourceProviderTests` (`BuildConnectionString_*`) |
| No password in failed connection tests | `…InvalidCredentials…`/`…WrongPassword…` in the provider and parity tests |
| GDPR at root, child and nested level, never selected | `DataSourceSecurityTests.GdprDenylist_*` (both backends), plus the existing `DynamicExportService*` GDPR tests |
| TLS policy and production default | `TransportSecurityTests`; SslMode mapping in the provider tests |
| ServiceNow HTTPS, SSRF, 401/403, retries | `ServiceNowTableApiProviderTests`, `ConnectionEndpointsHttpTests`, `ConnectionEndpointsHostValidationTests` |
