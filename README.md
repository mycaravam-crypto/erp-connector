# X5 Connector

[![CI](https://github.com/mycaravam-crypto/erp-connector/actions/workflows/ci.yml/badge.svg)](https://github.com/mycaravam-crypto/erp-connector/actions/workflows/ci.yml)
[![License: Apache 2.0](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)
![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4)
![Vue 3](https://img.shields.io/badge/Vue-3-42b883)

**X5 Connector** is a self-hosted data bridge between an ERP system and an external vendor. It reads
configuration items from a source system (read-only), removes personal data, and produces
checksummed export packages (Excel, CSV or JSON) that must be approved by two different people
before they leave the organisation. Vendor-supplied files can be imported back the same way:
staged, diffed, reviewed under the four-eyes principle, and only then written to the source.

The source schema is never hard-coded. Tables, columns, joins and the output shape are all
configured at runtime in the web UI.

---

## Contents

- [Features](#features)
- [How it works](#how-it-works)
- [Tech stack](#tech-stack)
- [Getting started](#getting-started)
- [Production deployment](#production-deployment)
- [Configuration](#configuration)
- [Using the application](#using-the-application)
- [API overview](#api-overview)
- [Development](#development)
- [Project structure](#project-structure)
- [Documentation](#documentation)
- [License](#license)

---

## Features

**Data sources**
- PostgreSQL, MariaDB/MySQL and ServiceNow (REST Table API) through one common provider interface
- Live schema discovery, including foreign keys that are offered as join suggestions
- Configurable transport security (TLS) for database connections; unencrypted connections are refused in production unless explicitly allowed
- Stored connection secrets are encrypted at rest using ASP.NET Core Data Protection

**Exports**
- **Export mapping:** pick a source table, choose and rename columns, and add 1:N joins, all in the UI
- **Export definitions:** any number of named exports, each built from a tree of fields, objects and arrays, so the output can be a flat table or deeply nested JSON
- Output as `xlsx`, `csv` or `json`, each with a SHA-256 manifest
- Per-definition cron schedules, plus a daily scheduled export and on-demand runs
- Named mapping presets that external systems can trigger through an API key

**Imports**
- **Import definitions** describe how vendor files map back onto source tables, with an explicit allowlist of writable columns
- A background worker watches an inbound folder, checks each file's manifest and skips files it has already processed
- Every import is staged as a reviewable plan (a diff of inserts and updates) and only committed after four-eyes approval, using optimistic concurrency

**Governance and compliance**
- **GDPR data minimisation:** fields on a configurable denylist are removed at query time and cannot be added to a mapping
- **Four-eyes release:** the operator and the approver must be two different authenticated users, and this is enforced on the server
- **Audit log:** every change of state (logins, releases, deliveries, skips, mapping and settings changes) is recorded and can be browsed in the UI
- **Sequence integrity:** gaps in the run sequence are detected and unresolved runs are flagged before a release
- **Delivery tracking:** records the physical handover and the number of records the vendor imported, which closes the chain of custody
- **Retention:** old export files and log records are cleaned up after a configurable period

**Operations**
- One Docker image (multi-stage build, non-root user, health check) that serves both the API and the UI
- Structured logging with Serilog (JSON in production) and sanitised error output
- Rate-limited login and approval endpoints
- White-label branding (app name, logo, favicon, login background)
- Light and dark theme

---

## How it works

```
 Source system (read-only)
 PostgreSQL · MariaDB/MySQL · ServiceNow
        │
        │  runtime-configured mapping / export definition
        ▼
 ┌──────────────────────┐
 │ Query                │  flat SQL, or nested JSON built in the database
 ├──────────────────────┤
 │ GDPR filter          │  denylisted personal fields removed at query time
 ├──────────────────────┤
 │ Package              │  xlsx / csv / json + SHA-256 manifest
 └──────────────────────┘
        │
        ▼
 Staging folder ──► four-eyes release ──► physical transfer ──► delivery acknowledgement
```

Every run is recorded in an internal SQLite database. A new run stays **Pending** until two
different registered users have confirmed it. The connector's responsibility ends at the staging
folder. How the files reach the vendor is controlled by the operator.

Imports go the other way: inbound folder → manifest and idempotency checks → staged plan →
four-eyes review → commit to the source.

---

## Tech stack

| Layer | Technology |
|---|---|
| Backend | .NET 9, ASP.NET Core Minimal API, EF Core 9 (SQLite), Serilog, ClosedXML |
| Source connectors | Npgsql (PostgreSQL), MySqlConnector (MariaDB/MySQL), ServiceNow Table API |
| Frontend | Vue 3, TypeScript, Vite, Tailwind CSS, Vue Router |
| Auth | JWT bearer tokens (BCrypt-hashed users) and hashed API keys for machine-to-machine access |
| Testing | xUnit, Vitest, Playwright |
| Quality | Roslyn analyzers, SonarAnalyzer, Roslynator, CSharpier, fallow |
| Delivery | Docker, docker-compose, GitHub Actions |

---

## Getting started

### Prerequisites

| Tool | Version | Needed for |
|---|---|---|
| [Docker](https://docs.docker.com/get-docker/) with Compose | recent | Quickest way to run everything |
| [.NET SDK](https://dotnet.microsoft.com/download/dotnet/9) | 9.0 | Building and running the API locally |
| [Node.js](https://nodejs.org/) | 22.18+ or 24.12+ | Building and running the UI locally |

### Option A: development stack in Docker (recommended)

This option needs no local .NET or Node installation. It starts the API (`dotnet watch`), the UI
(Vite with hot reload) and a seeded PostgreSQL test database:

```bash
git clone https://github.com/mycaravam-crypto/erp-connector.git
cd erp-connector
docker compose -f docker-compose.dev.yml up
```

| Service | URL |
|---|---|
| Web UI | http://localhost:5173 |
| API | http://localhost:5189 |
| Test database (PostgreSQL) | `localhost:5432` |

Sign in with one of the development accounts: **`alice` / `alice123`** or **`bob` / `bob123`**.
You need both accounts to try the four-eyes release.

On the **Connect** screen, use these values for the bundled test database:

| Field | Value |
|---|---|
| Host | `testdb` (the Compose service name; `localhost` does not work from inside the API container) |
| Port | `5432` |
| Database | `erp_testdb` |
| User / Password | `erp_test` / `erp_test_pw` |

Source changes under `src/` are picked up live. Press `Ctrl-C` to stop all services.

### Option B: run locally without Docker

```bash
# 1. (optional) start a seeded test database
docker compose --profile test up -d testdb

# 2. API on http://localhost:5189
dotnet run --project src/Connector.Api --launch-profile http

# 3. UI on http://localhost:5173 (in a second terminal)
cd src/connector-ui
npm install
npm run dev
```

The Vite dev server forwards all `/api/*` requests to `http://localhost:5189`, so no CORS setup is
needed. In development mode the API seeds the `alice` and `bob` accounts and the API key
`dev-local-api-key`.

---

## Production deployment

The production image builds the UI and serves it from the API, so a single container provides
everything.

### 1. Create credentials

Generate a JWT signing secret and a BCrypt hash for each operator:

```bash
openssl rand -base64 48                        # JWT secret (at least 32 characters)
htpasswd -bnBC 11 "" 'the-password' | tr -d ':\n'   # BCrypt hash for one user
```

When the API runs in development mode, it can also generate a hash for you:
`POST /api/auth/hash` with `{"password":"…"}`.

### 2. Create a `.env` file next to `docker-compose.yml`

```dotenv
AUTH_JWT_SECRET=<generated secret>
AUTH_USER0_NAME=alice
AUTH_USER0_HASH=<bcrypt hash>
AUTH_USER1_NAME=bob
AUTH_USER1_HASH=<bcrypt hash>
```

At least two users are needed so that runs can be released under the four-eyes principle.

### 3. Start the container

```bash
docker compose up -d --build
```

The application is then available at **http://localhost:8090** (container port `8080`). Its
state is stored in named volumes:

| Volume | Mount | Contents |
|---|---|---|
| `connector-db` | `/data/db` | SQLite database (run log, audit log, settings) |
| `connector-dpkeys` | `/data/dpkeys` | Data Protection key ring for encrypted settings. **Back it up separately.** If you lose it, the stored connection secrets cannot be decrypted. |
| `connector-staging` | `/data/staging` | Generated export packages |
| `connector-inbound` | `/data/inbound` | Vendor files waiting to be imported |

The container runs as a non-root user with all Linux capabilities dropped and
`no-new-privileges` set. Health check: `GET /api/health`.

### TLS

The container serves plain HTTP. **Do not expose it to untrusted networks directly.** Put a
TLS-terminating reverse proxy (nginx, Caddy, Traefik or a cloud load balancer) in front of it and
make the container reachable only from that proxy. Also set `AllowedHosts` and `AllowedOrigins`
to your real hostname.

### Database migrations

The internal SQLite schema is managed with EF Core migrations and is applied automatically at
startup. No manual step is needed when you upgrade.

---

## Configuration

Every setting can be supplied in `appsettings.json` or as an environment variable, with `__` as
the separator (for example `ExportWorker__RetentionDays=90`). The complete annotated reference is
[`appsettings.Production.json`](src/Connector.Api/appsettings.Production.json).

| Key | Default | Description |
|---|---|---|
| `Auth:JwtSecret` | – | **Required.** Secret of at least 32 characters used to sign JWTs |
| `Auth:JwtExpiryHours` | `8` | How long a session token stays valid |
| `Auth:Users` | – | List of `{ Username, PasswordHash }` (BCrypt) |
| `Auth:ApiKeys` | – | List of `{ Name, KeyHash }`, where `KeyHash` is the SHA-256 hex of the raw key |
| `AllowedOrigins` | `[]` | Origins allowed to call the API from a browser |
| `AllowedHosts` | `*` | Allowed host names; restrict this in production |
| `ConnectionStrings:ExportLog` | `/data/db/export_log.db` | Internal SQLite database |
| `DataProtection:KeysDirectory` | `/data/dpkeys` | Location of the encryption key ring |
| `DataSources:AllowUnencryptedConnections` | `false` | Allow source connections without TLS in production |
| `ExportSink:StagingPath` | `/data/staging` | Folder where export packages are written |
| `ExportWorker:ScheduledTimeUtc` | `06:00` | Time of the daily scheduled export (UTC, `HH:mm`) |
| `ExportWorker:RetentionDays` | `30` | How many days export files and log records are kept |
| `ImportSink:InboundPath` | `/data/inbound` | Folder that is watched for vendor import files |
| `ImportWorker:PollInterval` | `00:00:30` | How often the inbound folder is checked |

The scheduler time, retention period, default export format, GDPR denylist and branding can also
be changed at runtime on the **Settings** page. Those values override the configuration file.

To create an API key, generate a random value and store only its hash:

```bash
openssl rand -hex 32                        # the raw key, which you give to the calling system
printf '%s' '<raw key>' | sha256sum         # the hash, which goes into Auth:ApiKeys
```

---

## Using the application

A typical first-time setup follows the workflow in the navigation:

1. **Connect:** choose the source type (PostgreSQL, MariaDB/MySQL or ServiceNow), enter the connection details, then test and save them.
2. **Source schema:** browse the live tables and columns.
3. **Export schema:** select columns, rename them and add joins. Save the result as a named preset if you want to reuse it.
4. **Exports:** preview the data, start a run, and follow the run history.
5. **Release:** a second user opens the run and approves it. After the physical handover, the delivery is recorded on the same page.

For several independent or nested exports, use **Export Definitions**. Each definition has its
own output tree, format and schedule. **Import Definitions** configure the way back from the
vendor. The **Audit** page shows every action, and **Settings** covers the scheduler, the GDPR
denylist and branding.

---

## API overview

All endpoints are under `/api`. Except for `health`, `version`, `branding` (GET) and
`auth/login`, they require `Authorization: Bearer <token>`. Obtain a token with:

```bash
curl -X POST http://localhost:8090/api/auth/login \
     -H 'Content-Type: application/json' \
     -d '{"username":"alice","password":"…"}'
```

| Area | Endpoints |
|---|---|
| Auth | `POST auth/login`, `POST auth/revoke-my-sessions` |
| System | `GET health`, `GET version`, `GET audit` |
| Connection | `GET/POST connection`, `GET source-schema` |
| Export mapping | `GET/PUT export-mapping`, `GET export-mapping/presets`, `PUT/DELETE export-mapping/presets/{name}` |
| Pipeline | `POST pipeline/run?format=xlsx\|csv\|json`, `GET pipeline/preview`, `POST pipeline/run/{preset}` (also accepts `X-Api-Key`) |
| Export runs | `GET exports`, `GET exports/{seqNo}`, `POST exports/{seqNo}/release\|deliver\|skip` |
| Export definitions | CRUD on `export-definitions`, plus `/{id}/duplicate`, `/enable`, `/preview`, `/test`, `/run`, `/runs` |
| Import definitions | CRUD on `import-definitions`, plus `/{id}/duplicate`, `/enable`, `/preview`, `/runs`, and `suggest-from-export` |
| Import runs | `GET import-runs/{id}`, `POST import-runs/{id}/release\|reject` |
| Settings | `GET/PUT settings/scheduler`, `GET/PATCH gdpr-denied-fields`, `GET/PUT branding` |
| Reference | `GET schema` (ICD column contract, read-only) |

The request and response formats are described in [`knowledge/api/`](knowledge/api/index.md).

---

## Development

### Running the tests

```bash
# Backend unit and integration tests
dotnet test

# Frontend unit tests (Vitest)
cd src/connector-ui && npm test

# End-to-end tests (Playwright; the API and UI must be running)
cd src/connector-ui && npm run test:e2e
```

The integration tests against real databases use the bundled fixtures. If a fixture is not
running, those tests are skipped:

```bash
docker compose --profile test up -d testdb           # PostgreSQL on localhost:5432
docker compose --profile test up -d testdb-mariadb   # MariaDB    on localhost:3306
```

Both use the database `erp_testdb` with the user and password `erp_test` / `erp_test_pw`. The seed
scripts are in [`testdb/`](testdb/).

### Building

```bash
dotnet build Connector.sln
cd src/connector-ui && npm run build
```

### Code quality

Release builds treat all analyzer warnings as errors (Roslyn, SonarAnalyzer and Roslynator).
Formatting is enforced with CSharpier:

```bash
dotnet tool restore
dotnet csharpier --check .    # verify formatting (the same check CI runs)
dotnet csharpier .            # apply formatting

cd src/connector-ui && npm run check:fallow   # dead code, duplication and complexity checks for the UI
```

GitHub Actions runs the format check, the Release build and the full test suite, with PostgreSQL
and MariaDB service containers, on every push and pull request to `main`.

### Adding a migration

```bash
dotnet ef migrations add <MigrationName> \
  --project src/Connector.Infrastructure \
  --startup-project src/Connector.Api \
  --context ExportLogDbContext
```

### Versioning

The version is kept in [`VERSION`](VERSION) and bumped automatically for each pull request merged
into `main`. Patch is the default; the `minor` or `major` label on the pull request selects a
larger bump. Each release is tagged `vX.Y.Z`. The running version is shown on the login screen and
is also returned by `GET /api/version`.

---

## Project structure

```
.
├── src/
│   ├── Connector.Core/            Domain types and mapping configuration (no dependencies)
│   ├── Connector.Infrastructure/  Data source providers, export/import engines, background workers,
│   │                              audit logging, EF Core context and migrations
│   ├── Connector.Api/             ASP.NET Core host: endpoints, authentication, static UI hosting
│   └── connector-ui/              Vue 3 + TypeScript single-page application
├── tests/
│   ├── Connector.Core.Tests/          Unit tests without I/O
│   └── Connector.Integration.Tests/   Tests against PostgreSQL and MariaDB fixtures
├── testdb/                        Seed scripts for the test databases
├── knowledge/                     Architecture and domain documentation
├── Dockerfile                     Multi-stage production image
├── docker-compose.yml             Production stack (plus test database profiles)
└── docker-compose.dev.yml         Development stack with hot reload
```

Dependencies only point inwards: `Api → Infrastructure → Core`.

---

## Documentation

In-depth documentation is kept in the [**knowledge base**](knowledge/index.md):

- [Architecture](knowledge/architecture/): data source abstraction, SQL dialects, the ServiceNow provider, query performance
- [Export pipeline](knowledge/pipeline/index.md) and [export definitions](knowledge/dynamic-export/index.md)
- [Import definitions](knowledge/dynamic-import/index.md)
- [Operations](knowledge/operations/index.md): four-eyes release, GDPR, data retention, monitoring
- [Security](knowledge/security/index.md): data source threat model and controls
- [API reference](knowledge/api/index.md)

---

## License

Released under the [Apache License 2.0](LICENSE).
