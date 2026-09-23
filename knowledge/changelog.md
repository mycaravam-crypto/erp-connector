---
type: Changelog
title: Connector — Changelog
description: Phase-by-phase record of what shipped, newest first, plus current in-progress status.
tags: [changelog, roadmap, history]
timestamp: 2026-09-03T00:00:00Z
---

Last updated: 2026-09-23

---

## Phase 32 — Multi-source architecture cleanup ✅

Arbeitsauftrag 14: a final architecture review. Driver packages and every per-source-type decision
now live only in `DataSources/PostgreSql|MariaDb|ServiceNow`. See
[Multi-Source Architecture](/architecture/multi-source-architecture.md): component picture,
providers, capability matrix, limits, security model, and how to add a provider.

| Item | Notes |
|---|---|
| `IDataSourceProvider.ValidateConfig`/`TargetHost`/`IsAlwaysEncrypted`, `RelationalConnectionRules` | Replace `ConnectionEndpoints.ValidateRequiredFields`/`IsValidSslMode`/`ValidateInstanceUrl` and the type switch in `TransportSecurity`. `POST /api/connection` resolves the provider first |
| `ISqlDataSourceProvider.OpenConnectionAsync`, `ImportConnection`, `DataSourceCapabilities.Imports` | The import walker, releaser, worker and endpoints run on the provider's `DbConnection` + dialect instead of Npgsql. Imports are enabled for PostgreSQL only |
| `ISqlDialect.FormatValue` | One column-to-string rule for provider rows and the import walker |
| `Connector.Api.csproj` | `Npgsql` reference removed |
| Tests | `DataSourceConfigValidationTests` (replaces the two `ConnectionEndpoints*ValidationTests`, plus the `Imports` capability check). Import tests updated to the new signatures |

---

## Phase 31 — Query performance metrics and load tests ✅

Arbeitsauftrag 13: the export pipeline was checked for N+1 queries and unneeded full reads, and now
measures what each run reads. See [Query Performance](/architecture/query-performance.md).

| Item | Notes |
|---|---|
| `MeteredDataSourceProvider`, `ExportQueryMetrics`, `ExportBuildResult.Metrics` | `QueryCount`, `RecordsRead`, `DurationMs` per build (`BuildExportNodeAsync`, `BuildExportAsync`) |
| `ExportWorker`, `ExportDefinitionWorker`, Export Definition run/test endpoints | Metrics logged per scheduled run and recorded in the audit detail of manual and test runs |
| `ExportLoadTests` | 1 000 / 10 000 / 100 000 records on PostgreSQL and MariaDB. Asserts query count `1 + ⌈n/10 000⌉`, rows read `3n`, output size. Reports duration and allocations. Plus cancellation of a 100 000-record export |
| Analysis | No query per root record in any path, no `SELECT *`, only needed fields, server-side filters, ServiceNow paginated; known limits documented |

---

## Phase 30 — Provider contract tests and capabilities ✅

Arbeitsauftrag 12: one shared test contract for every data source provider, with the ways providers
legitimately differ stated as explicit capabilities. See
[Data Source Abstraction §7](/architecture/data-source-abstraction.md).

| Item | Notes |
|---|---|
| `DataSourceCapabilities`, `IDataSourceProvider.Capabilities` | `NativeSql`, `CaseSensitiveTextMatch`, `DistinguishesEmptyFromNull`, `ConditionsOnLeftJoinedTables`. PostgreSQL/MariaDB: all; ServiceNow: none |
| `DynamicExportService` | Gets its SQL dialect only from a provider with `NativeSql` |
| `DataSourceProviderContractTests` | 17 shared tests: connection, schema, relation metadata, simple query, projection, filter, null handling, limit, join, unknown table/column, cancellation, secrets not in errors, plus four capability-dependent tests asserted both ways |
| `PostgreSqlProviderContractTests`, `MariaDbProviderContractTests`, `ServiceNowTableApiProviderContractTests` | One subclass per provider; ServiceNow runs on `FakeServiceNow.WithExportFixture()` (same rows as the SQL fixtures) |

---

## Phase 29 — Data source security hardening ✅

Arbeitsauftrag 11: a targeted security review of the multi-source layer, with the concrete gaps fixed.
See [Data Source Security](/security/data-source-security.md) for threats, controls, residual risks
and test coverage.

| Item | Notes |
|---|---|
| `SanitizingLogFormatter` | Every log line goes through it: exception text (incl. inner exceptions) and string properties scrubbed, destructured `Password` dropped |
| `ErrorSanitizer` | Also scrubs HTTP `Basic` credentials. New `Scrub` (any text) and `ForLogging` (exceptions) |
| `AuditService` | Scrubs every detail. `export_failed`/`export_preset_run_failed` no longer store the raw exception message |
| `TransportSecurity`, `POST /api/connection` | In production, PostgreSQL/MariaDB must use `Require`/`VerifyCA`/`VerifyFull` unless `DataSources:AllowUnencryptedConnections=true`. Startup warning for a stored connection that isn't always encrypted |
| Tests | `DataSourceSecurityTests` (injection in table/column/join field/filter/IN/export identifier and recursive GDPR, on PostgreSQL and MariaDB), `SanitizingLogFormatterTests`, `TransportSecurityTests`, more `ErrorSanitizerTests`, ServiceNow table-name injection |

---

## Phase 28 — ServiceNow Table API data source ✅

Arbeitsauftrag 9: ServiceNow can be connected, its schema read and queried through `SourceQuery`
over the REST Table API, behind the same `IDataSourceProvider` interface. See
[ServiceNow Table API Provider](/architecture/servicenow-provider.md).

| Item | Notes |
|---|---|
| `DataSources/ServiceNow/` | `ServiceNowTableApiProvider`, `ServiceNowClient`, `ServiceNowSchemaReader`, `ServiceNowQueryCompiler`; registered in `Program.cs` |
| Schema | `sys_db_object` + `sys_dictionary` with inheritance. `sys_id` is the PK. Reference fields are relations (`incident.assignment_group` → `sys_user_group.sys_id`) |
| Queries | Server-side encoded-query filter, `sysparm_fields` projection, offset pagination, joins in C# with batched `IN` reads (no N+1), limit |
| Resilience | Retry only on 429/502/503, bounded (3) and honoring `Retry-After`. No retry on 401/403/400. 30 s request timeout. Cancellation |
| Security | HTTPS only (save time and every request), SSRF check on the instance host, credentials only in the `Authorization` header, ACLs respected, no admin role, no writes |
| `POST /api/connection`, `GET /api/source-schema` | Instance URL validation. The schema error names the instance URL |
| Tests | `ServiceNowTableApiProviderTests` against the in-memory `FakeServiceNow` Table API (no real credentials in CI), plus instance-URL cases in `ConnectionEndpointsHttpTests` |

**Known limit:** exports still render SQL, so an export against a ServiceNow connection fails with
`UnsupportedDataSourceException`. Moving export trees onto `SourceQuery` is follow-up work.

---

## Phase 27 — Connection UI for several source types ✅

Arbeitsauftrag 8: the connection page asks for the source type first (PostgreSQL, MariaDB / MySQL,
ServiceNow) and shows that type's fields. See
[Data Source Configuration §6](/architecture/data-source-configuration.md).

| Item | Notes |
|---|---|
| `ConnectionView.vue`, `lib/connectionForm.ts`, `ConnectionTlsSelect.vue` | Source Type select. Per-type fields and default ports (5432/3306). ServiceNow Instance URL + Access Method. Per-type required-field messages. Stored configs (including ones without `type`) load correctly |
| `api/connection.ts` | `DataSourceType`, `type`/`instanceUrl`/`hasPassword` in the DTOs; POST sends the full config |
| `ConnectionEndpoints.WithStoredPasswordIfUnchanged` | An empty password keeps the stored one while type, target and username are unchanged |
| `POST /api/connection` | A type with no provider yet gets a readable 400 instead of the resolver's internal message |
| `DashboardView.vue` | Shows the ServiceNow instance URL when there is no host/database |
| Tests | `ConnectionView.test.ts` (+14), `connectionForm.test.ts`, `ConnectionEndpointsPasswordRetentionTests`, one more `ConnectionEndpointsHttpTests` case |

---

## Phase 26 — MariaDB data source ✅

Arbeitsauftrag 7: MariaDB is the second relational data source, over MySqlConnector. The same
`ExportDefinition` produces the same result against PostgreSQL and MariaDB. See
[MariaDB Provider](/architecture/mariadb-provider.md).

| Item | Notes |
|---|---|
| `DataSources/MariaDb/` | `MariaDbDataSourceProvider`, `MariaDbConnectionFactory`, `MariaDbSchemaReader`, `MariaDbQueryCompiler`, `MariaDbDialect`; registered in `Program.cs` |
| `ISqlDialect.BuildMatchesAny` | Now binds the key batch itself (PostgreSQL: one array parameter, same SQL as before; MariaDB: one parameter per key) |
| `PostgreSqlDataSourceProvider.BuildConnectionString` | Refuses non-PostgreSQL configs, so the still Npgsql-only import paths fail clearly against a MariaDB connection |
| `testdb/mariadb-init.sql`, `docker-compose.yml` `testdb-mariadb`, CI `mariadb:11.4` service | MariaDB fixture with the same `export_*` rows as the PostgreSQL one |
| Tests | `MariaDbDialectTests`, `MariaDbQueryCompilerTests`, `MariaDbDataSourceProviderTests`, `MariaDbExportParityTests` (flat, filter, join, nested, CSV, JSON, XLSX, invalid credentials, missing table, timeout on both backends) |

**Known difference:** an `ExportNode` scalar of a boolean column is `"1"`/`"0"` on MariaDB
(`BOOLEAN` = `TINYINT(1)`) and `"true"`/`"false"` on PostgreSQL; documented and pinned by a test.

**Verification:** `dotnet build -c Release` (warnings-as-errors) and `dotnet csharpier check .` clean;
full suite (576 tests: 74 `Connector.Core.Tests` + 502 `Connector.Integration.Tests`) green against a
real PostgreSQL 16 (`testdb/init.sql`) and MariaDB 10.11 (`testdb/mariadb-init.sql`).

---

## Phase 25 — Export trees assembled in C# ✅

Arbeitsauftrag 6: the database no longer builds nested export JSON. `ExportNode` trees and legacy
nested JSON groups are fetched as plain relational rows (one query per tree node) and assembled in C#.
Output is identical. See [Export Tree Assembly](/architecture/export-tree-assembly.md).

| Item | Notes |
|---|---|
| `DynamicExportService.TreeQuery.cs` | New tree query engine: compiled `TreePlan`, one query per node (child levels batched via `= ANY(@keys)`, chunked at 10 000 keys), grouping by join-key text, assembly in member order |
| `ExecuteExportNodeQueryAsync`, `ExecuteNestedJsonQueryAsync` | Same signatures and callers. Now compile to a `TreePlan` instead of a `json_build_object`/`json_agg` query. Cardinality errors are raised in C# with the same messages |
| `NativeSqlQuery.ReturnNativeText`, `QueryResultColumn.DataType` | The provider can return PostgreSQL's exact text rendering plus each column's type (Npgsql `AllResultTypesAreUnknown`) |
| `ISqlDialect` | `BuildJsonObject`/`BuildJsonArrayAggregate` removed; `BuildMatchesAny` and `ConvertNativeTextToJson` (`PostgreSqlJsonValues`, PostgreSQL's `to_json` rules) added |
| `testdb/init.sql` | Dedicated `export_customer`/`export_order`/`export_order_line`/`export_line_tag` tables for the regression tests |
| Tests | New `ExportTreeRegressionTests` (root, 1:1, 1:n, two levels, empty collection, NULL fields, multiple roots, query count / no JSON SQL, legacy typed values), native-text conversion cases in `PostgreSqlDialectTests`. All pre-existing export tests unchanged |

**Verification:** a local differential harness compared the old SQL-built output with the new output
for 28 trees against a scratch database with every relevant column type. All 28 were identical. The
new regression tests were also run against the old implementation, where every structural test
passed. `dotnet build -c Release` (warnings-as-errors) and `dotnet csharpier check .` are clean. The
full suite (519 tests: 74 `Connector.Core.Tests` + 445 `Connector.Integration.Tests`) is green
against a real PostgreSQL 16 loaded from `testdb/init.sql`.

---

## Phase 24 — PostgreSQL dialect encapsulated ✅

Arbeitsauftrag 5: moved every PostgreSQL-specific SQL fragment out of the generic query builders into
a minimal `ISqlDialect` with one implementation, `PostgreSqlDialect`. See
[SQL Dialect](/architecture/sql-dialect.md), whose §4 lists the PostgreSQL-specific references that
remain and explains why each one stays.

| Item | Notes |
|---|---|
| `ISqlDialect` | `QuoteIdentifier`/`BuildParameterName`/`BuildLimit`, extended only where existing code needs it: `QuoteStringLiteral`, `CastToText`, `BuildNullSafeEquals`, `BuildJsonObject`, `BuildJsonArrayAggregate`, `BuildStringAggregate` |
| `PostgreSqlDialect` | New; now holds the quoting, `@pN`, `LIMIT`, `::text`, `IS NOT DISTINCT FROM`, `json_build_object`/`json_agg`/`string_agg` that were inline |
| `ISqlDataSourceProvider` | `IDataSourceProvider` + `Dialect`; `PostgreSqlDataSourceProvider` implements it, `DynamicExportService` gets its dialect from it |
| `DynamicExportService` | `QI`/`SqlLit` removed; legacy flat/nested-JSON and `ExportNode` builders render through the dialect. The "array" relation strategy is now `string_agg(…, ',')` instead of `array_to_string(array_agg(…), ',')`, with identical output |
| `ImportNodeWalker`, `ImportRunReleaser` | Render their SELECT/UPDATE through `PostgreSqlDialect` (they still run on Npgsql connections) |
| Layout | `PostgreSqlDataSourceProvider`/`PostgreSqlQueryCompiler`/`PostgreSqlDialect` moved to `Connector.Infrastructure/DataSources/PostgreSql` (namespace `Connector.Infrastructure.DataSources.PostgreSql`), `DataSourceProviderResolver`/`ISqlDialect` to `DataSources` |
| Tests | New `PostgreSqlDialectTests`; all existing Postgres-backed export/import tests unchanged and green; one compiler assertion updated for parameter names (`p0` → `@p0`) |

**Verification:** `dotnet build -c Release` (warnings-as-errors) clean, `dotnet csharpier check .`
clean, full test suite (491 tests: 74 `Connector.Core.Tests` + 417 `Connector.Integration.Tests`)
green against a real PostgreSQL 16 loaded from `testdb/init.sql`.

---

## Phase 23 — Database-neutral query model ✅

Arbeitsauftrag 4: introduced a database-neutral `SourceQuery`/`QueryResult` model so query intent no
longer has to be Postgres SQL text, plus its PostgreSQL compilation. See
[Source Query Model](/architecture/source-query-model.md) for the full design.

| Item | Notes |
|---|---|
| `SourceQuery`/`QueryColumn`/`QueryJoin`/`QueryCondition`/`QueryOperator` | New neutral model in `Connector.Core.DataSources` — no field can hold a SQL fragment; every filter operand is a value |
| `QueryResult` | Now `Columns` + `Rows` (values aligned with columns); `ToDictionaries()` returns the previous name-keyed shape |
| `SourceQueryValidator` | Dialect-free validation against `SourceSchema`: unknown table/column, join scoping, operand rules → `InvalidSourceQueryException` |
| `PostgreSqlQueryCompiler` | `SourceQuery` → quoted-identifier SQL with every value a bound parameter (`@p0`…), string values bound as `unknown` so Postgres infers the column type |
| `IDataSourceProvider` | `ExecuteAsync` now takes the neutral `SourceQuery` (validated against the live schema); the old raw-SQL path is `ExecuteNativeAsync(NativeSqlQuery)` — `NativeSqlQuery` is the old `SourceQuery`, renamed |
| `DynamicExportService` | Moved to `ExecuteNativeAsync` with identical SQL — export output unchanged. Migrating `ExportNode` (incl. its free-SQL `Filter`) onto `SourceQuery` is follow-up work |
| Tests | New `SourceQueryValidatorTests`, `PostgreSqlQueryCompilerTests`, and real-Postgres `ExecuteAsync` tests in `PostgreSqlDataSourceProviderTests`; the existing raw-SQL tests moved to `ExecuteNativeAsync` |

**Verification:** `dotnet build -c Release` (warnings-as-errors) clean, `dotnet csharpier check .`
clean, full test suite (480 tests: 74 `Connector.Core.Tests` + 406 `Connector.Integration.Tests`)
green **against a real PostgreSQL 16 loaded from `testdb/init.sql`** — including every pre-existing
Postgres-backed export test, the evidence that PostgreSQL behavior is unchanged.

---

## Phase 22 — Generalized data source configuration ✅

Arbeitsauftrag 3: generalized `DataSourceConfig` (Phase 21's shape) beyond its PostgreSQL-only
Host/Port/Database fields so it can describe a non-relational source, without breaking any config
already stored. See [Data Source Configuration](/architecture/data-source-configuration.md) for the
full design.

| Item | Notes |
|---|---|
| `DataSourceConfig` | Converted from a positional record with required `Host`/`Port`/`Database` to an init-only record with nullable `Host`/`Port`/`Database`, a new optional `InstanceUrl` (HTTP-API sources), and a `HasPassword` computed property. `ToString()` overridden to never print `Password`. |
| `DataSourceType.ServiceNow` → `ServiceNowTableApi`/`ServiceNowSqlApi` | Split the single placeholder member into ServiceNow's two actual API styles — both still deliberately unimplemented, resolving either throws `UnsupportedDataSourceException` same as `MariaDb` |
| `ConnectionEndpoints.ValidateRequiredFields` | New per-`DataSourceType` required-field check (`POST /api/connection`) — relational types need Host/Port/Database/Username, HTTP-API types need InstanceUrl/Username, anything else (including an out-of-range numeric value) is rejected as an unknown type |
| `ErpConnectionInfo` (`GET /api/connection`) | Gained `Type`/`InstanceUrl`/`HasPassword`; still never returns `Password` itself — `HasPassword` is the most any API response may say about it |
| Back-compat | No EF migration — `AppSetting.Value` is a schemaless encrypted JSON blob, so a config saved before `Type`, or before `InstanceUrl`, keeps deserializing correctly (`Type` still defaults to `PostgreSql`) |
| Tests | New `DataSourceConfigTests` (JSON back-compat, password-leak, invalid-shape-still-deserializes, out-of-range-type-still-deserializes), `ConnectionEndpointsRequiredFieldValidationTests` (invalid combinations, unknown type), `ConnectionEndpointsHttpTests` (`GET /api/connection` never leaks the password, `POST /api/connection` 400s on bad input); `DataSourceProviderResolverTests` extended for the renamed ServiceNow members plus an out-of-range type; every pre-existing `DataSourceConfig` construction in the test suite updated from positional to object-initializer syntax, no behavior change |

**Verification:** `dotnet build -c Release` (warnings-as-errors) clean, `dotnet csharpier check .`
clean, full test suite (443 tests: 58 `Connector.Core.Tests` + 385 `Connector.Integration.Tests`)
green — the Postgres-backed subset no-ops in this environment per the project's standing "no-op if
`testdb` isn't running" convention, same as every prior Postgres-dependent test here.

---

## Phase 21 — Generic data source abstraction ✅

Arbeitsauftrag 2: introduced `IDataSourceProvider`/`IDataSourceProviderResolver`
(`Connector.Core.DataSources`) so the application no longer depends directly on PostgreSQL
connection types — with PostgreSQL behavior unchanged. See
[Data Source Abstraction](/architecture/data-source-abstraction.md) for the full design.

| Item | Notes |
|---|---|
| `IDataSourceProvider`/`IDataSourceProviderResolver` | New interfaces in `Connector.Core` (no `Npgsql` reference, matches Arbeitsauftrag 2's exact signatures) |
| `PostgreSqlDataSourceProvider` | The one registered implementation; owns `BuildConnectionString`, `information_schema` introspection, and generic SQL execution — all moved here from `DynamicExportService`/`ConnectionEndpoints` |
| `ErpConnectionConfig` → `DataSourceConfig` | Renamed (`Connector.Core.DynamicExport` → `Connector.Core.DataSources`), gained a `Type` field defaulting to `PostgreSql` for backward-compatible JSON deserialization of already-stored connection settings |
| `SourceSchemaDto`/`SourceTableDto`/`SourceColumnDto` → `SourceSchema`/`SourceTable`/`SourceColumn` | Moved from `Connector.Api/Dtos.cs` into `Connector.Core.DataSources` — the interface's own schema type, not a parallel API-only shape |
| `DynamicExportService` | No longer accepts or opens an `NpgsqlConnection` — every query method takes `(IDataSourceProvider, DataSourceConfig)` and builds the same SQL text as before |
| `MariaDb`/`ServiceNow` | Added to `DataSourceType` as documented, deliberately unimplemented placeholders — resolving either throws `UnsupportedDataSourceException` |
| Out of scope, unchanged | `ImportNodeWalker`/`ImportRunReleaser` still use `NpgsqlConnection` directly — `ImportRunReleaser`'s four-eyes commit transaction doesn't fit the given single-query `ExecuteAsync` shape; see the architecture doc §5 |

**Verification:** `dotnet build -c Release` (warnings-as-errors) clean, `dotnet csharpier check .`
clean, full test suite (407 tests, including 12 new provider/resolver tests) green against a real
PostgreSQL 16 instance matching CI's service container — 0 failed, 0 skipped. Grep-confirmed zero
`NpgsqlConnection`/`NpgsqlCommand`/`NpgsqlDataReader` references in `DynamicExportService*.cs`.

---

## Phase 20 — Manual import run trigger ✅

Closed the gap `ImportDefinitionRunControls.vue` used to call out explicitly: an import definition
had nothing to run on demand, only Phase 17 Slice 4's `inbound/` folder watcher and Preview's
untracked dry run. An operator can now select or paste a sample file on the Import Job page and
stage it as a real `PendingReview` run without dropping a file + manifest on disk.

| Item | Notes |
|---|---|
| `POST /api/import-definitions/{id}/runs` | Reuses `ImportNodeWalker.WalkAsync`/`ImportPlanBuilder.Build` — the same walk-then-build pass `.../preview` and `ImportWorker` already share — then persists an `ImportRunEntity` at `PendingReview` with `TriggeredBy` set to the operator's username. Checksum computed from the posted content itself (no manifest file); `(ImportDefinitionId, Sha256Checksum)` still dedupes a re-submit, returning 409 with the existing run's id/status |
| `ImportDefinitionPreviewPanel.vue` gained a file picker + **Run** button | Sits next to the existing **Preview** dry run — same textarea, same sample; Preview writes nothing, Run stages it for real and hands off straight into the existing review/release dialog |
| Docs reconciled | [Import Definitions §3](/pipeline/import-definitions.md#3-comparison-with-the-export-side)'s Trigger row and [ImportWorker](/dynamic-import/import-worker.md) now name both triggers; the stale "nothing to run on demand" comment in `ImportDefinitionRunControls.vue` is gone |

**Verification:** `npm test` (frontend, including a new manual-run case), `vue-tsc --build` clean;
backend endpoint smoke-tested against `testdb` — staged, diffed, deduped on re-submit (409), and
rejected cleanly with no ERP write.

---

## Phase 19 — Connector Setup / Operations UI terminology cleanup ✅

Finished separating the UI's three usage contexts called for in the Connector Setup/Managed
Export/independent-Jobs UX split: Connector Setup (Connect, Source Schema, CMDB Export Mapping),
the regulated Managed Export (unchanged Four-Eyes Approval and release process), and independent
Export Jobs/Import Jobs. Presentation-layer only — `ExportDefinition`/`ImportDefinition`,
`/api/export-definitions`, `/api/import-definitions` and the rest of the domain model/API are
untouched.

| Item | Notes |
|---|---|
| Removed the last "Step 3" badge | `SchemaView.vue`'s CMDB Export Mapping screen no longer looks like an in-progress wizard step; `PreviewTable.vue`'s error copy and `ActiveMappingSummary.vue`'s CTA links dropped their "Step 1"/"Step 3" references too |
| Dropped the permanent legacy-migration blurb | `ExportDefinitionsView.vue` no longer permanently tells every visitor that jobs were "migrated automatically from the legacy mapping screen" — that's a one-time system event, not a durable UI concept |
| "Import Definitions" renamed to "Import Jobs" | Nav pill, dashboard card/link, list page title/eyebrow/empty-state, and the "New Import Definition"/"Import definition not found" edit-view copy — matches the "Export Jobs" naming already in place |
| Docs reconciled | [On-Demand Pipeline Run](/api/on-demand-run.md) and [Export Definitions 2.0](/pipeline/export-definitions-2.0.md)'s "Step 3" references updated to "CMDB Export Mapping" — they pointed at a screen landmark that no longer exists. This changelog's own past Step 3/4 mentions are left as a historical record of what the UI looked like at the time |

**Verification:** full frontend suite (`npm test`, 406/406 passing), `vue-tsc --build` clean,
`fallow audit` clean on every changed file.

---

## Phase 18 — Import mapping presets from export provenance ✅

A UI-time authoring convenience, not a runtime feature: tag a JSON `ExportDefinition`'s output with
a stable, versioned `IntegrationKey`/`ContractVersion`; if a vendor's inbound reply round-trips the
same pair, offer to create a new `ImportDefinition` from the paired export — reading
`RootTable`/`RootMatchColumn` directly off the export (deterministic, no tree walk) and offering
any other name-matched fields as disabled, human-reviewed candidates. `ImportEnvelope.definition`
stays the only routing mechanism; every suggested field still goes through the existing
`AllowedWritableColumns` schema-aware validator and four-eyes review before anything is written to
the ERP. See [Import Mapping Presets from Export Provenance](/pipeline/import-mapping-presets.md)
for the full spec (all Design Review Amendments and Open Decisions) and [ExportNode
Tree](/dynamic-export/export-node.md)/[ImportNode Tree](/dynamic-import/import-node.md) for the new
fields as they run today. Tracking issue
[#73](https://github.com/mycaravam-crypto/erp-connector/issues/73).

| Slice | Item | Notes |
|---|---|---|
| 1 | Data model + migration | New nullable `ExportDefinition.IntegrationKey`/`ContractVersion`/`CorrelationKeySourceField` and `ImportDefinition.IntegrationKey`/`ContractVersion` columns, EF migration, no backfill. Save-time validator on each definition type: the pair must be set together or not at all, and at most one *enabled* definition of a given type may ever claim a given pair — enforced in `ValidateRequestAsync` (create/update) and the `.../enable` endpoint, since either path can turn a definition enabled. Shipped via #79, closes #74. |
| 2 | Export-side wiring | `JsonExportFormatWriter` emits an optional top-level `provenance: { integrationKey, contractVersion, configVersion }` key when `ExportDefinitionEntity.IntegrationKey` is set — omitted entirely otherwise, byte-identical to before. New `ExportProvenance` record threaded through `IExportFormatWriter.Write`/`DynamicExportService.BuildExportNodeAsync`/`BuildNestedJsonBytes`; CSV/Excel writers accept and ignore it rather than forking the interface. No internal database id placed on the wire. Shipped via #80, closes #75. |
| 3 | Import-side wiring | `ImportNodeWalker.ParseRecords` already ignored every envelope key besides `schemaVersion`/`records`, so an inbound file's optional `provenance` block needed no parser change to be "accepted" — added a regression test proving the walk is byte-identical with or without one. New pure `ImportMappingSuggestion.SuggestFrom` (`Connector.Core.DynamicImport`): exact-match lookup on `(integrationKey, contractVersion)`, deterministic root-field prefill, best-effort disabled candidates for other matching field names, nested children never walked. No I/O; unit-tested in complete isolation. No UI or API endpoint yet — deliberately reviewable before any frontend surfaced it. Shipped via #81, closes #76. |
| 4 | Frontend | New `POST /api/import-definitions/suggest-from-export` endpoint parses a pasted sample `ImportEnvelope`'s `provenance` block and delegates to Slice 3's pure function against every enabled, tagged export — degrading to `null` for anything short of an exact match, never an error. New `ImportMappingSuggestionPanel.vue` offers "Create from export" vs. "Start blank" as a dedicated step in the New Import Definition flow (Open Decision #1, overriding the doc's original lean toward reusing the preview panel — that panel needs an already-saved definition's live ERP connection, which doesn't exist yet at this point). Accepting prefills the root table/match column/tree with the deterministic correlation field pre-enabled and every best-effort candidate's `SourceKey` pre-filled but still unchecked. Paired `IntegrationKey`/`ContractVersion` shown read-only once set (Open Decision #2). Also added the export-side "Integration tagging" UI (`ExportDefinitionBasicFields.vue`) the proposal doc's own §4 never called out — without it there was no way for an operator to set `IntegrationKey`/`ContractVersion`/`CorrelationKeySourceField` on an export at all, since Slice 2 was backend-only. Shipped via #82, closes #77. |
| 5 | Docs | This entry; [Import Mapping Presets](/pipeline/import-mapping-presets.md)'s status flip to shipped and implementation-status/Open-Decisions close-out; [`knowledge/pipeline/index.md`](/pipeline/index.md)'s "Proposed" entry replaced with this Phase 18 section; [ExportNode Tree](/dynamic-export/export-node.md)/[ImportNode Tree](/dynamic-import/import-node.md) updated with the new fields; [Import Definitions](/pipeline/import-definitions.md)'s Related section note updated to shipped. Closes #78. |

**Verification:** per-slice, not one end-to-end pass — each slice's own tests (`ImportMappingSuggestionTests`
for the Slice 3 pure function; `ImportMappingSuggestionEndpointTests`/`IntegrationKeyValidationTests`
for the Slice 1/4 save-time guardrails and endpoint, both against the in-memory Sqlite `LocalDb`
fixture, no Postgres testdb required) plus `dotnet build`/`dotnet test`/`dotnet csharpier check`
and `npm run type-check`/`npm test` clean at each point in the sequence. No dotnet SDK was
preinstalled in the Slice 4 session; the .NET 10 SDK (`dotnet-sdk-10.0` via `apt`, roll-forward to
run the net9.0 test host) was used to build and run the full backend suite locally rather than
skipping verification.

---

## Phase 17 — Inbound JSON import ✅

The reverse leg of the pipeline: vendor-supplied JSON written back into the live ERP database
under the same air-gap and four-eyes controls as the export path, resolving [Open Point
#6](/planning/open-points.md) ("Return-channel timing"). Mirrors [Export Definitions
2.0](/pipeline/export-definitions-2.0.md) in reverse — see [Import
Definitions](/pipeline/import-definitions.md) for the full spec (all fifteen Open Decisions) and
[Dynamic Import](/dynamic-import/index.md) for how the shipped result actually runs. Tracking
issue [#51](https://github.com/mycaravam-crypto/erp-connector/issues/51).

| Slice | Item | Notes |
|---|---|---|
| 1 | Data model + migration | `ImportNode`/`FieldMapping` reuse, `ImportDefinitionEntity`/`ImportRunEntity`, EF migration — no behavior yet, just the shape. Shipped via #60, closes #52. |
| 1b | Schema amendments from design review | An external design review of the shipped Slice 1 model, done before Slice 2 began, added Open Decisions #9–15 — most importantly that a staged run didn't freeze the definition it was staged against (#10), nothing guarded against the ERP row changing while a run sat in review (#12), and the same vendor file could be re-imported with no idempotency check (#13). `DefinitionSnapshotJson`, the richer run statistics, and the `(ImportDefinitionId, Sha256Checksum)` uniqueness constraint amended `ImportDefinitionEntity`/`ImportRunEntity` via a new EF migration; everything from Slice 2 onward is written against the amended shape. Shipped via #64, closes #61. |
| 2 | `ImportNodeWalker` — parse, match, diff | Parses the `ImportEnvelope` (#14, `schemaVersion` checked before anything else) against a saved tree, resolves root/child matches, and produces a per-row field-level diff (`ImportWalkResult`). **No writes** — deliberately ordered before the commit path so the compliance-sensitive part is de-risked with a zero-write-capability deliverable first. Shipped via #63, closes #53. |
| 3 | Four-eyes commit path | `ImportPlanBuilder` reshapes Slice 2's diff into the persisted `ImportPlan`/`ImportPlanOperation` list Open Decision #11 calls for. `ImportRunReleaser.ReleaseAsync` applies it: one conditional `UPDATE` per row, guarded by every one of that row's expected-old-values in a single `WHERE` clause (#12) — zero affected rows marks that row Conflicted, excluded, not overwritten, without failing the run (#6); an unrelated failure rolls back the whole transaction and marks the run Failed. `RejectAsync` declines a run without touching the ERP. The Operator/Approver-distinctness check became `FourEyesReview.ValidateApprover`, shared by the export release endpoint (refactored to call it) and the new `POST /api/import-runs/{id}/release`+`/reject` endpoints. Also added `ImportRowStatus.Invalid`, distinct from `Rejected`. Shipped via #66, closes #54. |
| 4 | `ImportWorker` (inbound folder watcher) | Polls `inbound/` on a timer, sibling of `ExportWorker`/`ExportDefinitionWorker`. SHA-256 manifest check (no sequence check, #8); routes to the target `ImportDefinition` via its own `ImportEnvelope`'s `definition` field; idempotency check against `(ImportDefinitionId, Sha256Checksum)` (#13), reporting already-staged/already-released/rejected-duplicate distinctly and never staging a second run, with the unique-constraint violation as a race-safe fallback; quarantines to `inbound/rejected/` for a bad manifest, unparseable JSON, or no matching enabled definition, always audit-logged. On success, stages an `ImportRunEntity` at `PendingReview` with `DefinitionSnapshotJson` frozen (#10) and moves the file to `inbound/processed/` (never deleted). Shipped via #68, closes #55. |
| 5 | API endpoints | `ImportDefinitionEndpoints.cs` — CRUD with the schema-aware `AllowedWritableColumns` validator (#9: must exist on its table, and must not be a primary key, identity/computed column, or untracked foreign key) and the `OnMissingChild = insert` rejection (#15), preview (parse + plan, no write), run history. Shipped via #67, closes #56. |
| 6 | Frontend | `ImportNodeTreeEditor.vue` mirrors `ExportNodeTreeEditor.vue` for `ImportNode` (no Filter, no `OnMissingChild` picker). `ImportAllowedColumnsEditor.vue` is a prominent, separately-editable allowlist editor — the feature's main safety control. `ImportDefinitionsView.vue`/`ImportDefinitionEditView.vue` mirror the export side's list/edit views; `ImportDefinitionPreviewPanel.vue` previews a pasted sample `ImportEnvelope`. `ImportRunReviewDialog.vue` is the four-eyes review surface — full matched/changed/unchanged/rejected/conflicted/invalid breakdown (#11) plus a field-level diff, reusing `ReleaseDialog`'s Operator/Approver pattern with a Reject option (`ImportPlanDiffTable.vue`/`ImportRunCountSummary.vue`/`ImportRunOutcome.vue` split out to manage complexity and dedupe with the preview panel). Added `GET /api/import-runs/{id}` — no existing endpoint exposed a single run's `PlanJson` for the review dialog to use. Also closed out #55 (Slice 4), which had shipped via #68 but was never itself closed. Shipped via #69, closes #57. |
| 7 | Docs | This entry; [Import Definitions](/pipeline/import-definitions.md)'s status flip to shipped and implementation-status checklist closed out; new [`knowledge/dynamic-import/`](/dynamic-import/index.md) bundle (`index.md`, `import-node.md`, `import-worker.md`, `run-history.md`); [Open Point #6](/planning/open-points.md) moved from Pending to Resolved; [`knowledge/pipeline/index.md`](/pipeline/index.md)'s Phase 17 section updated. Closes #58. |

**Verification:** per-slice, not one end-to-end pass — each slice's own real-Postgres integration
tests (the schema-aware validator's specific rejection reasons, the idempotency race, conditional-
write conflict detection) plus `dotnet build`/`dotnet test`/`dotnet csharpier check` clean at that
point in the sequence; Slice 6's `npm run type-check && npm run test` (371 tests) and `npx fallow
audit --base origin/main` clean (Export/Import duplication accepted by design, not suppressed —
see [Code Health
Backlog](/planning/code-health-backlog.md#phase-17-slice-6-additions-new-files--resolved-via-threshold-override))
plus a Playwright smoke pass covering the full golden path (list → edit → tree-build → preview →
run history → review → release with a distinct approver). No dotnet SDK/Docker daemon was
available in the Slice 6 session, so the backend `GET /api/import-runs/{id}` addition there was
verified by manual review rather than a live run, unlike [Export Definitions
2.0](/pipeline/export-definitions-2.0.md)'s equivalent pass, which did have a live .NET toolchain
and Postgres available in one session.

---

## Phase 16 — Nested JSON mapping UX ✅

Two follow-up passes over the Phase 12 nested-JSON export UI — no new backend capability, just
making the existing `NestedGroupEditor.vue`/`SchemaView.vue` flow the JSON-first default it was
always meant to become. Separate from, and does not touch, the Phase 14 `ExportNode`/
`ExportDefinition` tree-builder system, which already models forward/reverse FKs and nested JSON
generically.

| Item | Notes |
|---|---|
| Step 3 reframed around nested JSON, not relations | "Nested JSON Structure" now renders directly after column mapping, always expanded; "Related Table Joins" demoted to a collapsed advanced section (opens by default only if a mapping already has relations configured). Visibility decoupled from the `previewFormat` toggle so switching the preview format no longer hides or discards nested-group/envelope config. Step 3 copy rewritten for the JSON-first mental model. Closes #41–#44. |
| "Convert to Nested Group" action | Per-relation action builds the equivalent nested group and optionally removes the source relation — replaces the old dead-end `relationsDroppedForJson` warning with a real migration path. Closes #45. |
| Structural preview + inline validation | `NestedGroupEditor.vue` gained a JSON-shape preview and inline validation (missing join config, duplicate export keys); the resulting template-complexity growth was kept under CI's `fallow` threshold by extracting `IssuesAlert.vue`. Closes #47. |
| Design-token cleanup | `RelationsSection.vue`/`RelationCard.vue` raw slate/white/sky Tailwind classes replaced with design tokens. Closes #46. |
| FK-direction suggestions | `findSuggestedRelations` (`lib/suggestedRelations.ts`) only surfaced reverse FKs (another table's column pointing back at the selected one), so a table's own FK column — the common case for a 1:1 nested "object" lookup, e.g. `item.manufacturer_id → manufacturer.id` — never appeared as a suggestion in the `ExportNode` tree builder. Forward FKs now suggest `object`, reverse FKs keep suggesting `array`; each suggestion carries its kind so `ExportNodeTreeEditor.vue` adds the right node type instead of always defaulting to `array`. Split into `findForwardRelations`/`findReverseRelations` to stay under the complexity gate. The legacy `SchemaView.vue` mapping only models 1:N joins, so it filters suggestions to `kind === 'array'`, unchanged behavior. |

**Verification:** both PRs' CI green (`fallow audit`/`type-check`/`test` on the frontend); new
`suggestedRelations.test.ts` and additions to `SchemaView.test.ts` cover the new behavior.

---

## Phase 15 — UI redesign: dark theme and design system ✅

Reworks `connector-ui`'s visual layer end-to-end — tokens, primitives, icons, then every view —
behind a system-preference-aware dark mode, plus two small follow-up fixes discovered once it
shipped. Zero backend change; existing views and functionality unchanged, presentation only. See
#26 for the overall tracking issue.

| Slice | Item | Notes |
|---|---|---|
| 1 | Theme foundation | Tailwind v4 `@theme` tokens (surfaces, borders, text, brand, semantic status colors, focus ring, elevation, motion) with distinct light/dark palettes — dark mode is its own surface hierarchy, not an inversion of light values. Theme resolves from system preference by default; a manual choice overrides and persists to `localStorage`; an inline pre-mount script in `index.html` applies the resolved class before first paint to avoid a flash of the wrong theme. Theme toggle (Light/Auto/Dark) added to the `App.vue` header. Closes #27. |
| 2 | Shared UI primitives | New `components/ui/`: `Button` (primary/secondary/ghost/danger, hover/focus/active/disabled/loading), `Input`/`Select`/`TextField` (shared `FieldShell` label+help/error scaffold, `useId()`-generated ids for label/aria-describedby wiring), `Card`, `Modal` (backdrop, focus trap, Escape-to-close, focus returns to trigger on close), `Alert` (success/warning/danger/info, accent color confined to icon/border so message-text contrast holds regardless of variant). `ReleaseDialog` migrated onto Button+Modal+Input as first consumer — now a real dialog behind a "Release Run" trigger instead of always inline. Closes #28. |
| 3 | Icon system | `lucide-vue-next` (per-icon, tree-shaken imports) plus an `Icon.vue` wrapper fixing size (16/20/24px), 2px stroke width, `aria-hidden` by default — icons stay decorative, status/actions keep their text label alongside. Replaced text-glyph affordances app-wide: `StatusBadge` status icons, the stepper's "→", Modal's "✕", success/error result indicators, copy-to-clipboard glyphs, connection/expand-collapse markers, remove-item "×" buttons. Closes #29. |
| 4 | Golden-path migration | Migrated the primary 4-step workflow (Connect → Source Schema → Export Schema → Export, plus `ExportDetail`) and every embedded child component onto Slice 1's tokens and Slice 2's primitives — raw `slate-*`/`red-*`/`green-*`/`amber-*`/`indigo-*` utility classes replaced with semantic tokens throughout this path. `App.vue` shell gained dedicated `--color-nav-*` tokens for its fixed dark rail, a completed-step checkmark on the stepper, visible keyboard focus on nav controls, and its own `<nav aria-label="Secondary">` landmark separated from the primary stepper. Verified manually in headless Chromium, both themes, no console errors. Closes #30. |
| 5 | Secondary views migration | Same treatment for every remaining view — Settings, Audit Log, ICD Schema, Export Definitions (list + edit), Login, NotFound — so no view is left on pre-redesign styling. `Input` gained `min`/`max` pass-through and a `string \| number` model type to back `v-model.number` fields (Settings' retention-days) without losing type safety. Verified manually in headless Chromium, dark mode, no console errors. Closes #31. |
| 6 | Data-table design pass | Dedicated pass over the dense operational tables (`ExportRunsTable`, `ColumnMappingTable`, `PreviewTable`, `ExportDefinitionRunsTable`, `SourceColumnsTable`, `FieldPickerTable`) beyond Slices 4–5's mechanical color swap: bordered `overflow-x-auto` containers so wide content scrolls instead of breaking layout, border-based row separators instead of zebra striping (several tables already carry a semantic row tint that striping would fight with), standardized header typography, right-aligned tabular-nums for numeric columns, a real empty state inside the bordered container, Refresh buttons moved onto the Button primitive. Verified manually in headless Chromium, dark mode. Closes #32. |
| — | Top bar decluttered | Flat row of secondary links + username + sign-out replaced with a single "username ▾" trigger revealing ICD Schema/Export Definitions/Settings/Audit Log/Sign out in one panel — the bar itself now shows only branding, workflow steps, and the theme toggle. |
| — | Export-runs date-formatting bug fixed | Backend serializes `ExtractedAt` via `DateTimeOffset.ToString("O")`, which produces a `+00:00` offset rather than a literal `Z`; the frontend's `formatDate` only checked for a trailing `Z` before appending one, turning `+00:00` into the invalid `...+00:00Z`. Fixed to recognize numeric UTC offsets too. |

**Verification:** each slice confirmed manually in headless Chromium in both themes (no console
errors) in addition to the standing `npm run type-check`/`npm run test` gate.

---

## Phase 14 — Generic export definitions ✅

Generalizes [DynamicExportService](/pipeline/dynamic-export-service.md)'s one configurable
mapping into any number of independently named, scheduled, tree-based export definitions — see
[Export Definitions 2.0](/pipeline/export-definitions-2.0.md) for the full spec and
[Dynamic Export](/dynamic-export/index.md) for how the shipped result actually runs. The legacy
single-mapping flow (`/export-schema`, `POST /api/pipeline/run`) is untouched and stays a fully
supported, separate workflow — this was never a cutover (§11 decision #2).

| Slice | Item | Notes |
|---|---|---|
| 1 | Data model + migration converter | `ExportNode`/`FieldMapping`, `ExportDefinitionEntity`/`ExportDefinitionRunEntity`, EF migration, idempotent one-time converter from legacy config blobs |
| 2 | Query/format-writer engine | `DynamicExportService`'s `ExportNode` tree walker; `IExportFormatWriter`/CSV/Excel/JSON writers |
| 3 | API endpoints | `ExportDefinitionEndpoints.cs` — CRUD, duplicate, enable, preview, run, test, run history |
| 4 | Scheduler | New `ExportDefinitionWorker` (sibling of `ExportWorker`, not a replacement) polls enabled definitions every minute against a new purpose-built `CronSchedule` matcher; every trigger (manual, test, or scheduled) now shares one `ExportDefinitionRunner` and treats a zero-record result as `Failed`, closing a Slice 3 gap |
| 5 | Frontend | `api/exportDefinitions.ts` client; new `ExportNodeTreeEditor.vue` tree builder (add/remove fields and related entities, inline `FieldMapping` transform editing, per-node `Filter`) alongside — not replacing — the legacy `NestedGroupEditor.vue`; expanded list view (enable toggle, schedule, last-run status, duplicate/test/delete) and edit view (create-or-edit, root-table picker, preview panel, execution history) |
| 6 | Docs | This entry; new [`knowledge/dynamic-export/`](/dynamic-export/index.md) bundle; [Export Definitions 2.0](/pipeline/export-definitions-2.0.md) implementation-status checklist closed out |

**Verification:** full end-to-end pass, not just unit tests — `dotnet build`/`dotnet test`/
`dotnet csharpier check` clean (128 backend tests total, 20 of them against a real Postgres
`testdb`), `npm run type-check && npm run test` clean (231/231) and `npx fallow audit` clean
(scoped `.fallowrc.json` threshold overrides for the new tree-builder's inherently large
components — see [Code Health Backlog](/planning/code-health-backlog.md)), a real browser session
driving the actual tree-builder UI against a running backend, and the scheduler confirmed firing
a run unattended against a live app. See [Export Definitions 2.0](/pipeline/export-definitions-2.0.md)
for the design this shipped.

---

## Phase 13 — 2.0: simplification after the exploration phase ✅

Phases 1–12 partly served as discovery — figuring out what the connector actually needed (fixed
schema vs. runtime-configurable mapping, single-table vs. joins, flat vs. nested export) while a
working system was already in production. That produced real accidental complexity alongside the
real requirements. This phase cuts it back down now that the shape of the requirement is known,
without touching the compliance-critical paths (four-eyes release, GDPR, audit log, sequence
integrity) that were requirements from day one, not discovery artifacts.

| Item | Notes |
|---|---|
| Removed the dead fixed pipeline | `Connector.Export`, `ErpConfigurationItem`/`ExportItem`/`MappedExportRecord`, `ISchemaMapper`/`IDataMinimizer`/`IExportFilter`/`IPackager`/`IErpReader` — never registered in DI, no live traffic since dynamic mapping (Phase 9–10) replaced them. See [DynamicExportService](/pipeline/dynamic-export-service.md) |
| Removed the demo-ERP browsing feature | `ErpDatabaseView`, `BomTree`/`BomTreeRow`/`CiDetailPanel`, `GET /api/erp/records`, the seeded SQLite `DemoErpDbContext` — an exploration/demo view with no production requirement; superseded by `SourceSchemaView` + the Postgres `testdb` fixture |
| Removed inert schema-mapping endpoints | `PATCH /api/schema/columns`/`/api/schema/mappings` persisted state nothing read; `GET /api/schema` is now a pure static read of the ICD reference |
| ICD contract decoupled explicitly | `IcdSchemaView`/`GET /api/schema` documented as read-only reference, independent of the live dynamic mapping — resolves the earlier "two competing schema models" ambiguity |
| Unified export execution path | `DynamicExportService.BuildExportAsync` + `UsesNestedJson` are now the single decision point for Run Now, `ExportWorker`, and Preview — closes the Phase 12 gap where nested-JSON only worked from Run Now. Scheduler settings gained a persisted `Format` field |
| SchemaView format-toggle bug fixed | The Step 3 "which JSON options to show" toggle silently shared a `localStorage` key with the Step 4 export-format picker, so peeking at nested-group config could silently change what Run Now exported. Decoupled; Step 3 now auto-selects when a loaded mapping has nested groups |
| API module boundaries cleaned up | `api/erp.ts` split into `api/mapping.ts` and `api/icdSchema.ts`; GDPR denylist endpoints moved from `api/audit.ts` to `api/scheduler.ts` |
| Docs reconciled with the running code | `knowledge/pipeline/*` and `knowledge/domain/*` pages for the deleted fixed pipeline marked superseded, pointing to [DynamicExportService](/pipeline/dynamic-export-service.md) |

---

## Phase 12 — Nested JSON export ✅

| Item | Notes |
|---|---|
| Nested JSON structure | `ExportMappingNestedGroup`/`ExportMappingNestedField` — JSON-only, additive to `ExportMappingConfig`; `object` (N:1) or `array` (1:N), nestable via `Children` |
| SQL generation | `ExecuteNestedJsonQueryAsync`/`BuildNestedGroupExpr` build one query with native `json_build_object`/`json_agg`; a zero-match array COALESCEs to `[]`, not `null` |
| JSON envelope wrapper | `ExportJsonWrapperConfig` — optional root key, items key, dynamic-timestamp metadata block; unset reproduces the legacy flat envelope |
| `NestedGroupEditor.vue` | Recursive, self-referencing component in `SchemaView`'s "Nested JSON Structure" section (JSON format only) |
| Save-time validation | `ValidateNestedGroups` checks depth cap (16), required fields, identifier safety, GDPR denylist, duplicate export keys at every depth. Does **not** check `JoinKey`/`SourceJoinKey` type-compatibility — a bad pairing only surfaces as a raw Postgres error at export time |
| Wired into Run Now only | `POST /api/pipeline/run?format=json` branches to nested when set. Preview and the nightly worker (Excel-only) still used the flat path — a gap closed in Phase 13 |
| Local Postgres test fixture | `docker-compose --profile test up -d testdb` seeds `manufacturer`/`manufacturer_address` (array-of-objects + empty-array case); backs `connection.spec.ts` and the integration tests below |
| Tests | `DynamicExportServiceNestedJsonPostgresTests.cs` — 7 real-Postgres integration tests |

---

## Phase 11 — Legacy mapping data regression fix ✅

| Item | Notes |
|---|---|
| Fixed crash on pre-Phase-10 mapping data | `export_mapping`/`export_presets` saved before relations gained `Fields`/`Delimiter`/`FlattenStrategy` deserialized those as `null`, crashing `SchemaView.vue`'s load path (misreported as "Could not reach the API") and, latently, Preview/Run Now/the scheduled worker |
| `ExportMappingJson` normalization helper | `DeserializeConfig`/`DeserializePresets` backfill `Fields → []`, `Delimiter → ", "`, `FlattenStrategy → "string_join"`; all 6 backend read sites route through it |
| Defense-in-depth guards | `DynamicExportService` null-coalesces `Fields`/`Delimiter` at point of use; `SchemaView.vue`/`ExportView.vue` guard the same way |

---

## Phase 10 — Export mapping usability ✅

| Item | Notes |
|---|---|
| Foreign-key auto-detection | `IntrospectSchemaAsync` detects FK constraints; `SourceColumnDto` carries `ForeignKeyTable`/`ForeignKeyColumn`; `SchemaView.vue` shows one-click "Suggested Relations" |
| Multi-field relations | `ExportMappingRelation.Fields` replaces the single source/target pair — a 1:N join now pulls any number of independently renamed columns |
| GDPR denylist gap closed | Save-time validation now scans relation fields too, not just primary columns |

---

## Phase 9 — Production hardening ✅

| Item | Notes |
|---|---|
| EF Core migrations | Replaced startup DDL; `MigrateAsync()` + bootstrap for pre-migration databases |
| Program.cs split | 9 endpoint modules; `Dtos.cs`; Program.cs down to ~170 lines |
| Serilog | Structured JSON in production; readable console in dev; bootstrap logger |
| Docker | Multi-stage build (node → sdk → aspnet); non-root user; named volumes; docker-compose |
| Security headers | CSP, X-Frame-Options, X-Content-Type-Options, Referrer-Policy, Permissions-Policy; HSTS in production |
| AuditService | Scoped, non-fatal writes; wired to all state-changing endpoints and ExportWorker |
| 404 catch-all | `NotFoundView.vue`; Vue Router catch-all route |
| Playwright E2E | `login.spec.ts`, `navigation.spec.ts`, `audit.spec.ts`; Vitest exclude configured |

---

## Phase 8 — UX hardening, compliance depth & gap recovery ✅

| Item | Notes |
|---|---|
| Preview count clarity | Header shows `50+` at cap; truncation note says "preview cap, not export total" |
| DeliveryNotes max-length | API rejects Notes > 2,000 chars; UI textarea has live counter |
| SettingsView range hint | Retention days shows "1–3,650 days"; validated server-side |
| Excel date columns | `BuildExcelBytes` auto-detects ISO dates, writes as Excel DateTime `yyyy-mm-dd` |
| Route guards | `source-schema`/`export-schema` redirect to `/connect?notice=needs-connection` |
| ERP pagination cap | `GET /api/erp/records` returns `{records, total}`, default cap 500 |
| GDPR denylist as runtime config | `GET`/`PATCH /api/gdpr-denied-fields`; stored in `AppSetting`; tag-pill editor |
| Audit log | `AuditLog` table; non-fatal writes; 8 endpoints wired; `GET /api/audit`; `AuditView.vue` |
| Skipped run status | `ExportRunStatus.Skipped`; `POST /api/exports/{seqNo}/skip`; gap detection treats it as resolved |

---

## Phase 7 — Requirements gap closure ✅

| Item | Notes |
|---|---|
| Zero-count abort | Scheduled worker + on-demand handler mark run `Failed` on 0 records |
| ISO 8601 date coercion | `date`/`timestamp`/`timestamptz` columns formatted `yyyy-MM-dd` |
| GDPR field denylist | Enforced at mapping-save (400 on violation) and stripped in query results |
| ICD Schema view | `IcdSchemaView.vue` at `/icd-schema` — read-only ICD reference |
| ERP Database CI browser | `ErpDatabaseView.vue` at `/erp-database` — BOM tree, scope filter, per-row detail |

---

## Phase 6 — Operational enhancements ✅

| Item | Notes |
|---|---|
| Health check | `GET /api/health` — ERP DB, log DB, staging writability; no auth |
| Stale pending indicator | `IsStale` on `ExportRunSummary`; UI callout when Pending > 24 h |
| Sequence gap detection | `GET /api/exports/{seqNo}` returns `SequenceGapWarning` |
| Delivery acknowledgement | `POST /api/exports/{seqNo}/deliver`; closes custody chain |
| Schema column persistence | `AppSetting` table; `PATCH /api/schema/columns` |
| Connection config backend | `GET`+`POST /api/connection`; Npgsql live schema introspection |

---

## Phase 5 — Tests ✅

56 .NET tests (unit + integration), 187 Vitest tests, all passing. Playwright E2E wired (requires
both servers running).

---

## Phase 4 — API & frontend ✅

| Item | Notes |
|---|---|
| ASP.NET Minimal API | `GET /api/exports`, `GET /api/exports/{seqNo}`, `POST /api/exports/{seqNo}/release` |
| Vue 3 UI scaffolding | Vite + Vue 3 + TypeScript + Tailwind; proxy to :5189 |
| Four-step workflow | Connect → Source Schema → Export Schema → Export |
| ConnectionView | Postgres host/port/db/user/password form; persisted to `localStorage` |
| SourceSchemaView | Expandable table/column browser; calls `/api/source-schema` |
| Export Schema column toggles | Checkboxes + format picker (xlsx/csv/json) |
| ExportView | Format picker, Run Export, preview table, run history |
| Multi-format export | `POST /api/pipeline/run?format=xlsx\|csv\|json` |
| ERP Database view | BOM tree; flat list with search + sort; per-row detail panel |

---

## Phase 3 — Infrastructure, I/O & orchestration ✅

| Item | Notes |
|---|---|
| ExcelPackager | `guid` as first column; ClosedXML |
| SQLite Export Log | `ExportRun` table with all required fields |
| ExportWorker | `BackgroundService` with `PeriodicTimer`; `Failed` status on exception |
| Data retention cleanup | Daily purge of staging files + Released/Failed rows; configurable `RetentionDays` |

---

## Phase 2 — Pipeline implementation ✅

| Item | Notes |
|---|---|
| ExportFilter | Blocks on missing GUID; missing serial number allowed |
| DataMinimizer | Removes personal-data fields at type level; preserves GUID |
| SchemaMapper | Throws `InvalidCorrelationKeyException` on empty GUID; maps all ICD columns |

---

## Phase 1 — Solution setup & domain contracts ✅

5 projects with strict dependency rules; domain models `ErpConfigurationItem`, `ExportItem`,
`MappedExportRecord`; 6 pipeline interfaces with XML documentation.

---

## Open points (future iterations)

Tracked in [Open Points](/planning/open-points.md) — stakeholder ownership, code impact, and
resolution workflow for each pending item (classification marking, `storagelocation` entitlement,
snapshot volume, return-channel timing, retention periods, allocation chart import).
