---
type: Architecture
title: Data Source Configuration
description: >-
  DataSourceConfig's shape beyond PostgreSQL-only fields, its per-DataSourceType validation,
  password-handling guarantees, and the back-compat contract for stored configs.
tags: [architecture, data-source, postgres, servicenow, security, configuration]
timestamp: 2026-09-22T00:00:00Z
---

# Data Source Configuration

See [Data Source Abstraction](/architecture/data-source-abstraction.md) first for
`IDataSourceProvider`/`IDataSourceProviderResolver` and the overall seam this configuration feeds
into. This page covers `DataSourceConfig` itself: a shape general enough to describe a
non-relational data source, without breaking anything already stored.

## 1. Why the shape is general

`DataSourceConfig` carries a `Type` field, but isn't built entirely around PostgreSQL:
`Host`/`Port`/`Database` and the HTTP-API field `InstanceUrl` are all optional at the type level,
with the actually-required set enforced per `DataSourceType` at save time instead of baked into the
constructor — so a config for a source that isn't a relational database at all doesn't have to fake
values for fields it has no use for.

`PostgreSql`, `MariaDb` ([MariaDB Provider](/architecture/mariadb-provider.md)) and `ServiceNowTableApi`
([ServiceNow Table API Provider](/architecture/servicenow-provider.md)) have providers. `ServiceNowSqlApi`
is modeled only: a config can describe it, but `DataSourceProviderResolver.Resolve` rejects it with
`UnsupportedDataSourceException` because no `IDataSourceProvider` is registered for it.

## 2. The type

```csharp
namespace Connector.Core.DataSources;

public enum DataSourceType
{
    PostgreSql = 0,        // implemented
    MariaDb = 1,            // implemented (Arbeitsauftrag 7)
    ServiceNowTableApi = 2, // implemented (Arbeitsauftrag 9)
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
`Database`/`SslMode` — rather than being modeled as two separate subtypes. A single flat record is
kept over a `DataSourceConfig` class hierarchy (or separate `RelationalDataSourceConfig`/
`HttpApiDataSourceConfig` records) because:

- It's still exactly what gets deserialized from/serialized to one JSON blob in `AppSetting.Value` —
  a polymorphic shape there would need its own discriminated-union JSON handling for no present
  benefit.
- Every existing call site (`PostgreSqlDataSourceProvider`, `DynamicExportService.ConnectionFingerprint`,
  `ConnectionEndpoints`) already takes a single `DataSourceConfig` — splitting the type would ripple
  through all of them for a second provider that doesn't exist yet.
- YAGNI: the same judgment call [Data Source Abstraction §6](/architecture/data-source-abstraction.md#6-adding-a-second-provider)
  makes — a real second provider is real design work, and premature polymorphism here would mean
  guessing at a shape before that work happens.

## 3. Required fields per type

Not enforced by the type system (a `DataSourceConfig` is a plain data holder, not self-validating).
Each provider enforces its own rules in `IDataSourceProvider.ValidateConfig` (Arbeitsauftrag 14).
`POST /api/connection` resolves the provider for `Type` first and asks it before connecting:

| `Type` | Required | Rejected if |
|---|---|---|
| `PostgreSql`, `MariaDb` (`RelationalConnectionRules`) | `Host`, `Port`, `Database`, `Username`; `SslMode` unset or one of the shared names | Any of the four is null/blank, or `SslMode` is unknown |
| `ServiceNowTableApi` | `InstanceUrl` (absolute `https://`), `Username` | Either is missing, or the URL isn't HTTPS |
| Anything else (`ServiceNowSqlApi`, or a numeric value outside every defined member) | — | No provider is registered: `"data source type '{Type}' is not supported by this connector version yet."` |

"Invalid combinations" fail the provider's check, e.g. a `PostgreSql` config that carries
`InstanceUrl` instead of `Host`. `DataSourceProviderResolver.Resolve` rejects unknown or
unimplemented types for every caller, including those that never go through this endpoint
(`ExportWorker`, `ImportWorker`, etc.).

`POST /api/connection` validates `SslMode` for `PostgreSql`/`MariaDb` (one vocabulary for both — the
MariaDB provider maps the names onto MySqlConnector's modes). The SSRF host check
([Data Source Abstraction](/architecture/data-source-abstraction.md)) runs for every type: against `Host`
for the relational ones, and against the `InstanceUrl`'s host for ServiceNow. That URL must also be an
absolute `https://` URL.

## 4. Password handling

Four independent guarantees:

1. **Never returned by a GET endpoint.** `GET /api/connection`'s response DTO, `ErpConnectionInfo`,
   has no `Password` field at all — only `HasPassword`, a bool, so a caller can distinguish "no
   password set" from "password set, not shown" without the API ever emitting the value itself.
2. **Never logged.** `DataSourceConfig.ToString()` is overridden to omit `Password` entirely
   (reporting `HasPassword` instead) — a bare record's compiler-generated `ToString()` would
   otherwise print every public property, `Password` included, in plaintext into any log statement,
   exception message, or debugger view that happens to interpolate the config (`$"{config}"`, a
   `{Config}` structured-logging template, etc.).
3. **Never silently replaced or re-sent.** The connection form (`ConnectionView`, Arbeitsauftrag 8) never
   fills the password field from a GET response. Leaving it empty means "keep the stored password":
   `ConnectionEndpoints.WithStoredPasswordIfUnchanged` carries the stored value over only if `Type`,
   `Host`, `Port`, `Database`, `InstanceUrl` and `Username` are all unchanged. Otherwise the request
   keeps its empty password, so a changed target never receives the stored credential.
4. **Connection strings/exceptions never fully logged with a password.** `ErrorSanitizer.Detail`
   scrubs any `password=`/`pwd=` fragment out of an exception's message before it's ever returned to
   a caller or logged — the one place Npgsql has been observed to echo a connection string,
   credential included, back inside its own exception text.

At rest, `AppSetting.Value` (where `DataSourceConfig` is persisted, JSON-serialized, `Password`
included) is encrypted via `EncryptedStringConverter`/ASP.NET Core Data Protection — see that
converter's own doc comment. That encryption is a property of the storage column, not of
`DataSourceConfig` itself.

Rows written before the converter existed are plaintext. At every startup,
`AppSettingEncryptionMigrator` encrypts any such row in place and logs the affected **keys** (never
values). It recognizes ciphertext by the `CfDJ8` prefix every Data Protection payload starts with,
so a ciphertext this key ring can't decrypt is left untouched rather than re-encrypted as if it were
plaintext. Once every row is encrypted it does nothing. After this runs, the converter's SR-11
warning ("read as plaintext instead of a Data Protection payload") only fires for a row written
around the converter, which is the case worth investigating.

## 5. Backward compatibility

`AppSetting.Value` is a schemaless, encrypted JSON blob (`Connector.Infrastructure.AppSettingsStore`/
`EncryptedStringConverter`) — not a typed database column — so evolving `DataSourceConfig`'s shape
never requires an EF Core migration. Compatibility is entirely a JSON property, verified directly in
`DataSourceConfigTests`:

- **A config stored before `Type` existed at all** — no `"Type"` key in the JSON — deserializes with
  `Type` defaulting to `DataSourceType.PostgreSql` (the enum's `0` value).
- **A config stored before `InstanceUrl` existed** deserializes unchanged: `InstanceUrl` defaults to
  `null`, every other field keeps its stored value. `System.Text.Json` matches by property name, so
  this needs no explicit converter or upgrade step.
- **A config with an explicit `Type`** (any shape) round-trips identically.
- **An out-of-range numeric `Type`** (e.g. `999`, from a future member this codebase doesn't know
  about yet) still deserializes rather than throwing — `System.Text.Json` doesn't validate enum
  values against defined members by default — leaving `DataSourceProviderResolver.Resolve` (and so
  `POST /api/connection`) as the actual, explicit rejection point rather than a
  deserialization crash.

No stored `AppSetting` row needs a one-time rewrite for any of this — every old shape already
deserializes correctly against the current type, and the next `POST /api/connection` (or any other
`SetSettingAsync` call against that key) naturally re-persists it in the current shape.

## 6. Connection form (frontend)

`ConnectionView` starts with a **Source Type** select — PostgreSQL, MariaDB / MySQL, ServiceNow — and
shows only that type's fields. The rules live in `src/lib/connectionForm.ts`:

| Source type | Fields | Sent as |
|---|---|---|
| PostgreSQL | Host, Port (default `5432`), Database, Username, Password, TLS/SSL Mode | `Type = PostgreSql` |
| MariaDB / MySQL | Host, Port (default `3306`), Database, Username, Password, TLS Mode (no `Allow`) | `Type = MariaDb` |
| ServiceNow | Instance URL (`https://` required), Access Method, Username, Password | `Type = ServiceNowTableApi` (Table API) or `ServiceNowSqlApi` (SQL API / Live Connect) |

Switching type moves the port to the new default unless the user entered their own. Fields that the
chosen type doesn't use are sent as `null`. Required fields are checked in the browser before any
request, per type, with one message per field. The provider's `ValidateConfig` on the server stays the
authority. A stored config without `Type` loads as PostgreSQL. With `hasPassword`, the password field
stays empty and its placeholder says it's unchanged. A type with no provider yet (currently the
ServiceNow SQL API) gets a readable 400 ("not supported by this connector version yet").

## 7. Tests

- `DataSourceConfigTests` (`Connector.Core.Tests`, no database/HTTP) — the back-compat/JSON cases
  from §5, `ToString()`/`HasPassword` password-leak coverage, and that an "invalid combination" or
  out-of-range `Type` still deserializes (rejection is a separate, later step, not a parse-time
  concern).
- `DataSourceConfigValidationTests` (`Connector.Integration.Tests`, pure unit, no database) — every
  provider's `ValidateConfig`/`TargetHost`/`IsAlwaysEncrypted`: required fields present or missing,
  invalid combinations, TLS-mode vocabulary, ServiceNow's HTTPS instance URL, and the `Imports`
  capability check.
- `ConnectionEndpointsHttpTests` (`Connector.Integration.Tests`, real HTTP pipeline via `ApiFactory`,
  no Postgres `testdb` needed) — `GET /api/connection`'s raw JSON response never contains the
  stored password or a `"password"` key, `HasPassword` reflects whether one is set, and
  `POST /api/connection` 400s for an unknown type or a type-inappropriate missing field.
- `DataSourceProviderResolverTests` — covers `ServiceNowTableApi`/`ServiceNowSqlApi`, `MariaDb`, an
  out-of-range numeric type, and no-providers-registered.
- `ConnectionEndpointsPasswordRetentionTests` — empty password kept only for an unchanged target/account.
- Frontend: `ConnectionView.test.ts` (provider switch, default ports, required fields, stored PostgreSQL/
  MariaDB/ServiceNow configs, password handling, API errors), `connectionForm.test.ts`, `connection-api.test.ts`.
- Every Postgres-backed test (`PostgreSqlDataSourceProviderTests`, the `DynamicExportService*`
  suites, etc.) passes unchanged against a real PostgreSQL instance.
