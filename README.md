# X5 Connector

A self-contained .NET 9 + Vue 3 application that extracts warranty-relevant Configuration Items from an ERP system, runs them through a GDPR-compliant data pipeline, and produces a daily export package (Excel / CSV / JSON + SHA-256 manifest) for four-eyes review before physical transfer to a vendor's CMDB.

**Stack:** .NET 9 · ASP.NET Core Minimal API · EF Core 9 · SQLite · ClosedXML · Vue 3 · TypeScript · Tailwind CSS · Serilog

**Docs:** this file covers setup, running, testing, and deployment. Everything else — architecture,
domain concepts, the changelog, and the in-progress Phase 14 (generic multi-export) spec and status
— lives in the **[knowledge base](knowledge/index.md)**.

Status: Phase 13 (2.0 simplification) is shipped; Phase 14 (generic, tree-based multi-export
definitions) is in progress — see [Export Definitions 2.0](knowledge/pipeline/export-definitions-2.0.md)
for the live per-slice status.

---

## What it does

```
ERP database (read-only, PostgreSQL)
    ↓  runtime-configurable mapping: source table, columns, joins — no hardcoded schema
Query   — flat SQL, or nested json_build_object/json_agg for JSON export
    ↓
GDPR strip — denylisted fields (TechnicianName etc.) removed at query time
    ↓
Package — Excel (ClosedXML) / CSV / JSON + SHA-256 manifest
    ↓
Staging folder (operator-controlled transfer to vendor)
```

Every run is logged in SQLite. A pending run stays locked until **two different registered users** confirm it (four-eyes release). Delivery back-acknowledgement closes the custody chain.

---

## Key features

| Area | What's built |
|---|---|
| **GDPR compliance** | Personal fields removed at query time via a runtime denylist, configurable via UI; GDPR Art. 5(1)(c) enforced at mapping-save |
| **Four-eyes release** | Operator ≠ Approver enforced server-side; JWT identity non-spoofable; audit trail entry on every action |
| **Full audit log** | Every state-changing action (login, release, deliver, skip, mapping changes, scheduler changes) written to `AuditLog` table; browsable in UI |
| **Dynamic ERP mapping** | Source table, columns, and 1:N joins configured at runtime via UI — no hardcoded schema; foreign keys auto-detected and suggested as candidate joins; each join can pull multiple independently renamed columns |
| **Multi-format export** | xlsx (default), csv, json; SHA-256 checksum on every package |
| **Sequence integrity** | Gap detection warns when an earlier run is unresolved before release; Skipped status for permanent failures |
| **Delivery tracking** | Records imported record count and notes when the physical handover is completed |
| **Scheduler** | Daily background export at configurable UTC time; retention cleanup at configurable retention period |
| **Production deployment** | Docker (multi-stage, non-root, health check); Serilog with JSON output in production |

---

## Solution structure

```
Connector.sln
│
├── src/
│   ├── Connector.Core          ← Domain types (ExportManifest, ExportPackage) + dynamic-mapping config types
│   ├── Connector.Infrastructure ← DynamicExportService, ExportWorker, FileSystemSink, AuditService, EF Core DbContext + migrations
│   └── Connector.Api           ← ASP.NET Core host; endpoint modules; JWT auth; Serilog
│
└── tests/
    ├── Connector.Core.Tests        ← Unit tests, no I/O
    └── Connector.Integration.Tests ← DynamicExportService tests; nested-JSON tests against the Postgres testdb fixture
```

### Dependency rules

```
Connector.Api → Core, Infrastructure
Connector.Infrastructure → Core
Connector.Core → (none)
```

See [`knowledge/pipeline/dynamic-export-service.md`](knowledge/pipeline/dynamic-export-service.md) for how the export pipeline works today — a 2.0 cleanup removed an earlier fixed-schema pipeline (`Connector.Erp`, `Connector.Export`) that was fully superseded by the runtime-configurable mapping during development and carried no live traffic.

---

## Getting started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9)
- [Node.js ≥ 22](https://nodejs.org/) (for the Vue 3 frontend)
- [Docker](https://docs.docker.com/get-docker/) (optional — for containerised deployment)

### Run the full stack in development

```bash
./dev.sh
```

Starts the API on `:5189` and the Vite dev server on `:5173`. Ctrl-C stops both.

**Dev credentials:** `alice / alice123` and `bob / bob123` (hard-coded in Development mode only).

#### Alternative: one-command Docker dev environment

No local .NET/Node toolchain required. Runs the API (`dotnet watch`), the UI (Vite dev
server with hot reload), and the test Postgres ERP source together:

```bash
docker-compose -f docker-compose.dev.yml up
```

- UI: `http://localhost:5173` — API: `http://localhost:5189` — Postgres: `localhost:5432`
- Edits to `src/Connector.Api` (and the other `src/Connector.*` projects) or `src/connector-ui`
  on the host are picked up live inside the containers.
- All three services' output is interleaved in one `docker-compose` log stream, each line
  prefixed `api |` / `ui |` / `testdb |` — `Ctrl-C` stops everything.
- This is a separate file from [`docker-compose.yml`](docker-compose.yml), which builds the
  production image instead (compiled UI baked into the API container, no hot reload).
- **Step 1 ("Connect to Source Database") host:** the API runs *inside* the `api` container,
  so `localhost:5432` from the host machine is **not** reachable from there. On the Step 1
  screen, use host `testdb` (the docker-compose service name), not `localhost`. Full values:
  `testdb` / `5432` / `erp_testdb` / `erp_test` / `erp_test_pw`.
  (If you're running via `./dev.sh` instead, the API runs directly on the host and `localhost`
  is correct there.)

### Run tests

```bash
# .NET (unit + integration tests)
dotnet test

# Frontend (186 Vitest tests)
cd src/connector-ui && npm test

# E2E (Playwright — requires both servers running)
cd src/connector-ui && npm run test:e2e

# JS/TS code health (dead code, duplication, complexity — gated on changed files)
cd src/connector-ui && npm run check:fallow
```

One connection e2e test exercises a real successful Step 1 connection and self-skips if
it can't reach a database; start a disposable local Postgres fixture for it with:

```bash
docker-compose --profile test up -d testdb   # localhost:5432, erp_test/erp_test_pw/erp_testdb
```

### Build manually

```bash
dotnet build Connector.sln          # .NET solution
cd src/connector-ui && npm run build  # Vue UI
```

---

## Docker deployment

Build and run with docker-compose:

```bash
docker-compose up --build
```

The API starts on port `8080`. SQLite databases and staging files are persisted on named Docker volumes (`connector-db`, `connector-staging`).

See [`docker-compose.yml`](docker-compose.yml) and [`Dockerfile`](Dockerfile) for full configuration. At minimum, override the JWT secret before deploying:

```bash
Auth__JwtSecret=<your-secret-min-32-chars> docker-compose up
```

---

## Configuration

All production keys are documented in [`appsettings.Production.json`](src/Connector.Api/appsettings.Production.json). The most important settings:

| Key | Description |
|---|---|
| `Auth:JwtSecret` | ≥ 32-char random secret for JWT signing — **must be set via env var** |
| `Auth:Users` | Array of `{ Username, PasswordHash }` — generate BCrypt hashes with `POST /api/auth/hash` (dev only) |
| `ConnectionStrings:ExportLog` | SQLite path for the export log database |
| `ExportSink:StagingPath` | Directory where export packages are written |
| `ExportWorker:ScheduledTimeUtc` | Daily export time in UTC (HH:mm) |
| `ExportWorker:RetentionDays` | How long to keep export files and log records |

Environment variable overrides use `__` as the separator (e.g. `Auth__JwtSecret=...`).

---

## API reference

All endpoints except `/api/health` and `/api/auth/login` require `Authorization: Bearer <token>`.

### Auth

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/auth/login` | Authenticate — `{"username":"…","password":"…"}` → `{"token":"…"}` |
| `GET` | `/api/health` | Health check (no auth) — ERP DB, log DB, staging writability |

### Export runs

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/exports` | List all runs (newest first) with `isStale` flag |
| `GET` | `/api/exports/{seqNo}` | Full detail including gap warning and delivery fields |
| `POST` | `/api/exports/{seqNo}/release` | Four-eyes release — `{"approver":"…"}` (operator from JWT) |
| `POST` | `/api/exports/{seqNo}/deliver` | Record physical handover — `{"importedRecordCount":N,"notes":"…"}` |
| `POST` | `/api/exports/{seqNo}/skip` | Skip a Pending/Failed run — `{"reason":"…"}` logged to audit trail |

### Pipeline

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/pipeline/run?format=xlsx\|csv\|json` | Trigger an immediate export (default: xlsx) |
| `GET` | `/api/pipeline/preview` | Read-only preview of the first 50 records |

### Schema & mapping

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/schema` | Read-only ICD reference contract (version + column list) — documentation, decoupled from the live export |
| `GET` | `/api/export-mapping` | Current dynamic mapping config (source table, fields, joins) — this is what actually drives every export |
| `PUT` | `/api/export-mapping` | Save full mapping config (GDPR-denylist-checked) |
| `GET` | `/api/export-mapping/presets` | All saved mapping presets |
| `PUT` | `/api/export-mapping/presets/{name}` | Save a named preset |
| `DELETE` | `/api/export-mapping/presets/{name}` | Delete a named preset |

### Connection & source schema

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/connection` | Stored ERP connection info (no password returned) |
| `POST` | `/api/connection` | Test and persist a Postgres ERP connection |
| `GET` | `/api/source-schema` | Live source schema (falls back to demo schema if no connection) |

### Settings

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/settings/scheduler` | Effective scheduler config — time, retention, and export format (DB override or appsettings default) |
| `PUT` | `/api/settings/scheduler` | Update scheduled time, retention period, and export format |
| `GET` | `/api/gdpr-denied-fields` | Current GDPR denylist |
| `PATCH` | `/api/gdpr-denied-fields` | Replace GDPR denylist |
| `GET` | `/api/audit` | Audit log entries (newest first, default 100) |

---

## Frontend (Vue 3)

The UI lives at `src/connector-ui/`. It implements a **four-step workflow** plus supporting views:

| Route | View | Purpose |
|---|---|---|
| `/connect` | `ConnectionView` | Configure and test the source PostgreSQL connection |
| `/source-schema` | `SourceSchemaView` | Browse tables and columns from the live source DB |
| `/export-schema` | `SchemaView` | Toggle export columns, set column name overrides |
| `/exports` | `ExportView` | Trigger export, live preview, run history |
| `/exports/:seqNo` | `ExportDetail` | Four-eyes release form, delivery form, skip form |
| `/icd-schema` | `IcdSchemaView` | ICD column contract reference (read-only, decoupled from the live mapping) |
| `/settings` | `SettingsView` | Scheduler time/retention, GDPR denylist tag editor |
| `/audit` | `AuditView` | Full audit log with action labels and timestamps |

Route guards block `/source-schema` and `/export-schema` until a connection is configured.

Run the frontend in development:

```bash
cd src/connector-ui
npm install        # first time only
npm run dev        # dev server on http://localhost:5173
```

Vite proxies all `/api/*` requests to `:5189` — no CORS configuration needed in development.

---

## Code quality

Static analysis runs as part of the build. `TreatWarningsAsErrors` is enabled in Release mode — any Roslyn, Roslynator, or SonarAnalyzer finding that would be a warning in Debug fails the CI build.

```bash
dotnet tool restore          # restore CSharpier
dotnet csharpier --check .   # check formatting (what CI does)
dotnet csharpier .           # apply formatting
```

CI runs on every push via GitHub Actions: format check → Release build → test suite.

---

## Database schema management

The export-log database schema is managed via **EF Core migrations**. To apply migrations on startup, the app calls `Database.MigrateAsync()`. Adding a new migration:

```bash
dotnet ef migrations add <MigrationName> \
  --project src/Connector.Infrastructure \
  --startup-project src/Connector.Api \
  --context ExportLogDbContext
```

---

## License

MIT
