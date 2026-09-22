---
type: Architecture
title: Data Source Configuration
description: >-
  Arbeitsauftrag 3 — DataSourceConfig generalized beyond PostgreSQL-only fields, its per-DataSourceType
  validation, password-handling guarantees, and the back-compat/migration contract for existing stored
  configs.
tags: [architecture, data-source, postgres, servicenow, security, configuration]
timestamp: 2026-09-22T00:00:00Z
---

# Data Source Configuration

See [Data Source Abstraction](/architecture/data-source-abstraction.md) first for `IDataSourceProvider`/
`IDataSourceProviderResolver` and the overall seam this configuration feeds into. This page covers
Arbeitsauftrag 3 specifically: generalizing `DataSourceConfig` itself so it can describe a non-relational
data source, without breaking anything already stored.

## 1. Why this exists

Arbeitsauftrag 2's `DataSourceConfig` already carried a `Type` field, but its shape was still built
entirely around PostgreSQL: `Host`/`Port`/`Database` were required positional parameters, so any config —
even a hypothetical future one for a source that isn't a relational database at all — had to fake values
for fields it had no use for. Arbeitsauftrag 3 removes that constraint: `DataSourceConfig` is now an
init-only record whose relational fields (`Host`/`Port`/`Database`) and HTTP-API field (`InstanceUrl`) are
all optional at the type level, with the actually-required set enforced per `DataSourceType` at save time
instead of baked into the constructor.

This is **modeling only** — same scope boundary Arbeitsauftrag 2 drew for `MariaDb`/`ServiceNow`. No new
provider is implemented here; `ServiceNowTableApi`/`ServiceNowSqlApi` join `MariaDb` as `DataSourceType`
members a config can describe, that `DataSourceProviderResolver.Resolve` still rejects with
`UnsupportedDataSourceException` because no `IDataSourceProvider` is registered for any of them.

## 2. The type

```csharp
namespace Connector.Core.DataSources;

public enum DataSourceType
{
    PostgreSql = 0,        // implemented
    MariaDb = 1,            // not implemented — modeled only
    ServiceNowTableApi = 2, // not implemented — modeled only
    ServiceNowSqlApi = 3,   // not implemented — modeled only
}

public sealed record DataSourceConfig
{
    public DataSourceType Type { get; init; }         // defaults to PostgreSql (enum's 0 value)

    // Relational sources (PostgreSql/MariaDb) only:
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? Database { get; init; }

    // HTTP API sources (ServiceNowTableApi/ServiceNowSqlApi) only:
    public string? InstanceUrl { get; init; }

    public string Username { get; init; } = "";
    public string Password { get; init; } = "";

    // Relational sources only — see knowledge/architecture/data-source-abstraction.md's SR-03 note.
    public string? SslMode { get; init; }

    public bool HasPassword => !string.IsNullOrEmpty(Password);
}
```

`Host`/`Port`/`Database` and `InstanceUrl` are mutually relevant to disjoint sets of `DataSourceType`
values — a relational source never sets `InstanceUrl`, an HTTP-API source never sets `Host`/`Port`/
`Database`/`SslMode` — rather than being modeled as two separate subtypes. A single flat record was kept
over introducing a `DataSourceConfig` class hierarchy (or separate `RelationalDataSourceConfig`/
`HttpApiDataSourceConfig` records) because:

- It's still exactly what gets deserialized from/serialized to one JSON blob in `AppSetting.Value` — a
  polymorphic shape there would need its own discriminated-union JSON handling for no present benefit.
- Every existing call site (`PostgreSqlDataSourceProvider`, `DynamicExportService.ConnectionFingerprint`,
  `ConnectionEndpoints`) already takes a single `DataSourceConfig` — splitting the type would ripple through
  all of them for a second provider that doesn't exist yet.
- YAGNI: this is the same judgment call [Data Source Abstraction §6](/architecture/data-source-abstraction.md#6-extending-with-a-second-provider)
  already made — a real second provider is real design work, and premature polymorphism here would be
  guessing at a shape before that work happens.

## 3. Required fields per type

Not enforced by the type system (a `DataSourceConfig` is a plain data holder, not self-validating) — enforced
at save time by `ConnectionEndpoints.ValidateRequiredFields`, the single choke point `POST /api/connection`
routes every request through before ever calling a provider:

| `Type` | Required | Rejected if |
|---|---|---|
| `PostgreSql`, `MariaDb` | `Host`, `Port`, `Database`, `Username` | Any of the four is null/blank |
| `ServiceNowTableApi`, `ServiceNowSqlApi` | `InstanceUrl`, `Username` | Either is null/blank |
| Anything else (including a numeric value outside every defined member) | — | Always — `"Unknown data source type '{Type}'."` |

This single function covers both the "invalid combinations" case (e.g. a `PostgreSql` config missing `Host`,
or one that mixed in `InstanceUrl` instead) and the "unknown data source type" case from the same `switch`,
so the two can never drift into inconsistent error handling. `DataSourceProviderResolver.Resolve` enforces
the unknown/unimplemented-type case again, independently, for every other caller that reaches a provider
without going through this HTTP endpoint (`ExportWorker`, `ImportWorker`, etc.) — see
`DataSourceProviderResolverTests` (§6).

`POST /api/connection`'s host-reachability SSRF check ([Data Source Abstraction](/architecture/data-source-abstraction.md))
and `SslMode` validation only run for `PostgreSql`/`MariaDb` — an HTTP-API source's `InstanceUrl` isn't a bare
host, and isn't validated here at all yet (there is no live ServiceNow provider that would ever open a
connection to it).

## 4. Password handling

Three independent guarantees, all pre-existing security-review findings this generalization had to keep
holding as the shape changed:

1. **Never returned by a GET endpoint.** `GET /api/connection`'s response DTO, `ErpConnectionInfo`, has no
   `Password` field at all — only `HasPassword`, a bool. This was already true before Arbeitsauftrag 3 (the
   old DTO simply omitted `Password`); the explicit `HasPassword` field is new, so a caller can distinguish
   "no password set" from "password set, not shown" without the API ever emitting the value itself.
2. **Never logged.** `DataSourceConfig.ToString()` is overridden to omit `Password` entirely (reporting
   `HasPassword` instead) — a bare record's compiler-generated `ToString()` would otherwise print every
   public property, `Password` included, in plaintext into any log statement, exception message, or
   debugger view that happens to interpolate the config (`$"{config}"`, a `{Config}` structured-logging
   template, etc.). No call site does this today, but the override makes it impossible to introduce by
   accident later.
3. **Connection strings/exceptions never fully logged with a password.** Unchanged from Arbeitsauftrag
   2/SR-14: `ErrorSanitizer.Detail` scrubs any `password=`/`pwd=` fragment out of an exception's message
   before it's ever returned to a caller or (potentially) logged — the one place Npgsql has been observed to
   echo a connection string, credential included, back inside its own exception text.

At rest, `AppSetting.Value` (where `DataSourceConfig` is actually persisted, JSON-serialized, `Password`
included) is encrypted via `EncryptedStringConverter`/ASP.NET Core Data Protection — see that converter's
own doc comment. That encryption is a property of the storage column, not of `DataSourceConfig` itself, and
predates this change; it's unaffected by the shape generalization here.

## 5. Backward compatibility / migration

`AppSetting.Value` is a schemaless, encrypted JSON blob (`Connector.Infrastructure.AppSettingsStore`/
`EncryptedStringConverter`) — not a typed database column — so there is no EF Core migration involved in
evolving `DataSourceConfig`'s shape, and never has been. "Migration" for this record is entirely a JSON
back-compat property, verified directly in `DataSourceConfigTests`:

- **A config stored before `Type` existed at all** (Arbeitsauftrag 2's own back-compat case) — no `"Type"`
  key in the JSON — deserializes with `Type` defaulting to `DataSourceType.PostgreSql` (the enum's `0`
  value), exactly as it did before this change.
- **A config stored by Arbeitsauftrag 2** (`Type` present, but no `InstanceUrl`, and the record was still
  the old positional shape) deserializes unchanged: `InstanceUrl` defaults to `null`, every other field
  keeps its stored value. Converting the record from positional to init-only parameters doesn't change its
  JSON contract — System.Text.Json matches by property name either way — so this required no explicit
  converter or upgrade step.
- **A config with an explicit `Type`** (new or old shape) round-trips identically.
- **An out-of-range numeric `Type`** (e.g. `999`, from a future member this codebase doesn't know about
  yet) still deserializes rather than throwing — `System.Text.Json` doesn't validate enum values against
  defined members by default — leaving `ValidateRequiredFields`/`DataSourceProviderResolver.Resolve` as the
  actual, explicit rejection points rather than a deserialization crash.

No stored `AppSetting` row needs a one-time rewrite for any of this — every old shape already deserializes
correctly against the current type, and the next `POST /api/connection` (or any other `SetSettingAsync`
call against that key) naturally re-persists it in the current shape.

## 6. Tests

- `DataSourceConfigTests` (`Connector.Core.Tests`, no database/HTTP) — the back-compat/JSON cases from §5,
  `ToString()`/`HasPassword` password-leak coverage, and that an "invalid combination" or out-of-range
  `Type` still deserializes (rejection is a separate, later step, not a parse-time concern).
- `ConnectionEndpointsRequiredFieldValidationTests` (`Connector.Integration.Tests`, pure unit, no
  database) — `ValidateRequiredFields` for every `DataSourceType`: all-required-fields-present,
  each-field-individually-missing, a relational type carrying `InstanceUrl` instead of `Host`/an HTTP-API
  type carrying `Host` instead of `InstanceUrl` (the "invalid combinations" case read literally), and the
  unknown-type case.
- `ConnectionEndpointsHttpTests` (`Connector.Integration.Tests`, real HTTP pipeline via `ApiFactory`, no
  Postgres `testdb` needed) — `GET /api/connection`'s raw JSON response never contains the stored password
  or a `"password"` key, `HasPassword` reflects whether one is set, and `POST /api/connection` 400s for an
  unknown type or a type-inappropriate missing field.
- `DataSourceProviderResolverTests` — extended with `ServiceNowTableApi`/`ServiceNowSqlApi` (renamed from
  the old placeholder `ServiceNow` member) and an out-of-range numeric type, alongside the pre-existing
  `MariaDb`/no-providers-registered cases.
- Every pre-existing Postgres-backed test (`PostgreSqlDataSourceProviderTests`, the `DynamicExportService*`
  suites, etc.) still passes unchanged against a real PostgreSQL instance — updated only mechanically, from
  `DataSourceConfig`'s old positional-constructor calls to object-initializer syntax, with no behavior
  change.
