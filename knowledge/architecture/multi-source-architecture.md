---
type: Architecture
title: Multi-Source Architecture
description: >-
  The final picture after Arbeitsauftrag 7–14: how PostgreSQL, MariaDB and the ServiceNow Table API sit
  behind one provider seam, what each supports (capability matrix), the known limits, the security
  model, and how to add another provider.
tags: [architecture, data-source, postgres, mariadb, servicenow, capabilities, security]
timestamp: 2026-09-23T00:00:00Z
---

# Multi-Source Architecture

## 1. Component picture

```text
                     Connector.Api                        (endpoints, workers' hosts; no driver packages)
   ConnectionEndpoints · ExportDefinitionEndpoints · PipelineEndpoints · Import*Endpoints
                                   │  IDataSourceProviderResolver.Resolve(config.Type)
                                   ▼
 Connector.Core.DataSources       IDataSourceProvider ──── Capabilities (DataSourceCapabilities)
 (no SQL, no drivers)             DataSourceConfig · SourceSchema · SourceQuery · QueryResult
                                   │
 Connector.Infrastructure          ├── DynamicExportService (export trees, legacy mappings)  ─┐
  (provider-neutral)               ├── ImportNodeWalker / ImportRunReleaser  (via ImportConnection)
                                   ├── MeteredDataSourceProvider · TransportSecurity           │ ISqlDialect
                                   └── ISqlDataSourceProvider / ISqlDialect / RelationalConnectionRules
                                   │
 DataSources/PostgreSql   PostgreSqlDataSourceProvider · PostgreSqlDialect · PostgreSqlQueryCompiler   (Npgsql)
 DataSources/MariaDb      MariaDbDataSourceProvider · MariaDbDialect · MariaDbQueryCompiler ·
                          MariaDbSchemaReader · MariaDbConnectionFactory                            (MySqlConnector)
 DataSources/ServiceNow   ServiceNowTableApiProvider · ServiceNowClient · ServiceNowSchemaReader ·
                          ServiceNowQueryCompiler                                                    (HttpClient)
```

Driver packages (`Npgsql`, `MySqlConnector`) and every per-type decision live only in the three
provider folders. Outside them, provider names appear in exactly two places, both intentionally:

- the `DataSourceType` enum (`Connector.Core`), which is the persisted discriminator;
- the DI registration in `Program.cs`, the composition root.

A review for Arbeitsauftrag 14 searched for `Npgsql|MySqlConnector|ServiceNow|PostgreSql|MariaDb` and
`DataSourceType.*` outside the provider folders. It found no `if (type == …)` in
`DynamicExportService`, the `ExportNode` engine, the writers, the schedulers, preview, run history or
API business logic. What it did find was moved into the providers:

| Was | Now |
|---|---|
| `ConnectionEndpoints.ValidateRequiredFields` (a switch over `DataSourceType`), `IsValidSslMode` (Npgsql's enum), `ValidateInstanceUrl` (ServiceNow) and the relational/HTTP host branch | `IDataSourceProvider.ValidateConfig` / `TargetHost` / `IsAlwaysEncrypted`, with `RelationalConnectionRules` shared by both SQL providers |
| `TransportSecurity.IsAlwaysEncrypted` (a type switch) | the providers' `IsAlwaysEncrypted` |
| `ImportNodeWalker`/`ImportRunReleaser`/`ImportWorker`/import endpoints opening `NpgsqlConnection`, using `NpgsqlCommand` and `PostgreSqlDialect.Instance` | `ImportConnection.OpenAsync` → the provider's `OpenConnectionAsync` (`DbConnection`) + its `ISqlDialect`, gated by the `Imports` capability |
| Npgsql-specific date formatting in the walker and the PostgreSQL row reader; `FormatDefault` in the MariaDB provider | `ISqlDialect.FormatValue` |
| `Npgsql` package reference in `Connector.Api` | removed |

## 2. Supported providers

| `DataSourceType` | Provider | Driver/transport | Details |
|---|---|---|---|
| `PostgreSql` | `PostgreSqlDataSourceProvider` | Npgsql | [Data Source Abstraction](/architecture/data-source-abstraction.md), [SQL Dialect](/architecture/sql-dialect.md) |
| `MariaDb` | `MariaDbDataSourceProvider` | MySqlConnector | [MariaDB Provider](/architecture/mariadb-provider.md) |
| `ServiceNowTableApi` | `ServiceNowTableApiProvider` | HTTPS REST (Table API) | [ServiceNow Table API Provider](/architecture/servicenow-provider.md) |
| `ServiceNowSqlApi` | — (modeled only) | — | Rejected with `UnsupportedDataSourceException` / a 400 on save |

## 3. Capability matrix

Common to all providers, and pinned by `DataSourceProviderContractTests` for each one:

- connection test, and schema with primary keys and relations;
- neutral `SourceQuery` with projection, filters (`=`, `<>`, `<`/`>`, `IN`, null checks, text match),
  limit and joins;
- explicit failure for an unknown table or column;
- cancellation, and errors that never contain a secret;
- config validation, target host and the transport rule.

| Capability (`DataSourceCapabilities`) | PostgreSQL | MariaDB | ServiceNow |
|---|---|---|---|
| `NativeSql`: SQL export builders (Export Definitions, legacy mappings) | ✅ | ✅ | ❌ |
| `Imports`: four-eyes import (walk + transactional release) | ✅ | ❌ (not yet verified) | ❌ |
| `CaseSensitiveTextMatch` | ✅ | ✅ | ❌ |
| `DistinguishesEmptyFromNull` | ✅ | ✅ | ❌ |
| `ConditionsOnLeftJoinedTables` | ✅ | ✅ | ❌ |
| Always encrypted | with `Require`/`VerifyCA`/`VerifyFull` | with `Require`/`VerifyCA`/`VerifyFull` | always (HTTPS) |

Callers check capabilities instead of types. `DynamicExportService` needs `NativeSql`, and
`ImportConnection` needs `Imports`.

## 4. Known limits

- **ServiceNow can't be exported yet.** The export builders render SQL, so an export on a ServiceNow
  connection fails with `UnsupportedDataSourceException`. Moving the export trees onto `SourceQuery`
  needs `ExportNode.Filter` replaced by structured conditions (a stored-data migration and a UI
  change; [Source Query Model §5](/architecture/source-query-model.md)).
- **`ExportNode.Filter` is free SQL** in the backend's own dialect, screened at save time
  ([Data Source Security](/security/data-source-security.md)).
- **Imports only on PostgreSQL** (capability), until MariaDB's release path is verified.
- **MariaDB booleans** in `ExportNode` scalars are `"1"`/`"0"` (`BOOLEAN` = `TINYINT(1)`).
- **Performance:**
  - Exports are built in memory, and join keys are compared as text.
  - ServiceNow uses offset pagination and 100-key join batches.
  - See [Query Performance](/architecture/query-performance.md).

## 5. Security model

The full review is in [Data Source Security](/security/data-source-security.md). In short:

- **Identifiers** come from the introspected schema, are validated and are quoted by the provider's
  dialect.
- **Values** are always bound parameters. For ServiceNow, a value containing an encoded-query
  separator is rejected.
- **Credentials** are encrypted at rest and never returned by GET. Log lines (`SanitizingLogFormatter`),
  audit details and errors (`ErrorSanitizer`) are scrubbed.
- **GDPR:** denied columns are excluded from every SELECT at every depth.
- **Transport:** each provider says whether a config is always encrypted. Production refuses anything
  else unless `DataSources:AllowUnencryptedConnections=true`.
- **SSRF:** the provider's `TargetHost` is resolved and checked before any connection attempt.
- **ServiceNow:** least privilege (no admin role), ACLs respected, bounded retries.

## 6. Adding another provider

A new relational source (say SQL Server) needs no change to the export or import domain logic:

1. **Folder** `Connector.Infrastructure/DataSources/<Name>/`, the only place its driver package is
   used.
2. **Provider** implementing `ISqlDataSourceProvider` (or `IDataSourceProvider` for a non-SQL source):
   - `Type`, and `Capabilities` (start from `DataSourceCapabilities.Sql` and switch off what isn't
     verified, as MariaDB does with `Imports`);
   - `ValidateConfig`/`TargetHost`/`IsAlwaysEncrypted` (reuse `RelationalConnectionRules`);
   - `TestConnectionAsync`/`ReadSchemaAsync` (schema reader);
   - `ExecuteAsync` (a `SourceQuery` compiler — validate with `SourceQueryValidator`, bind every value);
   - `ExecuteNativeAsync` and `OpenConnectionAsync`.
3. **Dialect** implementing `ISqlDialect`: quoting, parameters, limit, text cast, null-safe equals,
   batched key match, string aggregate, native text → JSON, `FormatValue`.
4. **Enum member** in `DataSourceType`, and **registration** in `Program.cs`.
5. **Tests:**
   - a `DataSourceProviderContractTests` subclass (three members) on the shared `export_*` fixture
     rows;
   - dialect and compiler unit tests, and `DataSourceConfigValidationTests` cases;
   - a CI service if it needs a real server.
6. **UI:** a source-type option and its fields in `src/lib/connectionForm.ts`.

## 7. Verification (Arbeitsauftrag 14)

`dotnet build -c Release` with warnings as errors (Roslyn, Sonar and Roslynator analyzers) and
`dotnet csharpier check .` are clean. `dotnet test` passes 709 tests (74 Core + 635 Integration)
against PostgreSQL 16 and MariaDB 10.11. The frontend passes `vue-tsc`, 498 vitest tests and
`fallow audit`.
