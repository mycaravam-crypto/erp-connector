# Connector — Technical Development Reference

**Status:** Active development — Iteration 1  
**Stack:** .NET 9 · ASP.NET Core Minimal API · EF Core 9 · SQLite · ClosedXML · xUnit · Vue 3 (frontend — next)

---

## What This Is

The connector is the only software component we build as part of the ERP-to-ServiceNow integration. It reads maintenance-relevant configuration items from the ERP (read-only), runs them through a 5-step data pipeline, and packages a daily Excel export + SHA-256 manifest for the four-eyes release authority.

The connector never crosses the air gap. Its output ends at the staging folder; the physical transfer (gateway → USB → vendor) is a separate, human-controlled process.

---

## Solution Structure

```
Connector.sln
│
├── src/
│   ├── Connector.Core          ← Domain types + interface contracts (no dependencies)
│   ├── Connector.Erp           ← IErpReader implementation; demo SQLite ERP database
│   ├── Connector.Export        ← Pipeline step implementations (filter, minimize, map, package)
│   ├── Connector.Infrastructure ← Worker, file sink, export-log DbContext (SQLite)
│   └── Connector.Api           ← ASP.NET Core host; minimal API for the release UI
│
└── tests/
    ├── Connector.Core.Tests        ← Unit tests for Export + Core (no I/O)
    └── Connector.Integration.Tests ← Full pipeline tests against demo ERP DB
```

### Project dependency rules

```
Connector.Api
  └── Connector.Core, Connector.Erp, Connector.Export, Connector.Infrastructure

Connector.Infrastructure
  └── Connector.Core

Connector.Export
  └── Connector.Core

Connector.Erp
  └── Connector.Core

Connector.Core
  └── (no project dependencies)
```

---

## Pipeline

```
IErpReader.ReadMaintainableCIsAsync()
    ↓  ErpConfigurationItem[]  (raw — may contain personal data)
IExportFilter.Filter()
    ↓  excludes CIs without a serial number (= missing correlation key)
IDataMinimizer.Minimize()
    ↓  ExportItem[]  (TechnicianName, StorageLocation stripped; type system enforces this)
ISchemaMapper.Map()
    ↓  MappedExportRecord[]  (ISO-8601 dates, all identifiers as strings)
IPackager.PackageAsync()          (xlsx only — routed by format param)
    OR inline CSV / JSON builder   (csv / json formats — built directly in the run endpoint)
    ↓  ExportPackage  (file bytes + ExportManifest with SHA-256 + sequence number)
IExportSink.WriteAsync()
    → staging/export_NNNN_YYYYMMDDTHHmmssZ.{xlsx|csv|json}
    → staging/export_NNNN_YYYYMMDDTHHmmssZ.manifest.json
```

Every run is logged to the export-log SQLite database (`ExportRun` table). After a successful run the record sits in `Pending` status until a release operator and a separate approver confirm it via the API (four-eyes rule).

### Key design constraints

| Constraint | Where enforced |
|---|---|
| Read-only ERP access | `IErpReader` contract; demo reader uses `AsNoTracking()` |
| Personal data excluded before any write | `IDataMinimizer` → `ExportItem` type has no personal fields |
| Serial number preserved as string | `SchemaMapper` — explicit `string` assignment, not numeric |
| Dates as ISO 8601 | `SchemaMapper` → `DateOnly.ToString("yyyy-MM-dd")` |
| Excel text formatting (no auto-coerce) | `ExcelPackager` — column format `49` (`@`) for all columns |
| Atomic staging write | `FileSystemExportSink` — write to `.tmp`, then `File.Move` |
| Four-eyes release | `POST /api/exports/{seqNo}/release` — Operator ≠ Approver enforced |

---

## Getting Started

### Prerequisites

- .NET 9 SDK — `dotnet --version` should show `9.x.x`

> **WSL / non-global install:** the SDK lives at `/home/mycaravam/.dotnet/dotnet`.  
> Add to PATH: `export PATH="$HOME/.dotnet:$PATH"` (or use the full path in every command below).

- Node.js ≥ 18 — for the Vue 3 frontend

### Build

```bash
cd /home/mycaravam/connector
dotnet build Connector.sln
```

### Run .NET tests

```bash
dotnet test Connector.sln
```

### Run the full stack (recommended)

```bash
./dev.sh
```

Starts API (`:5189`) and UI (`:5173`) in one terminal. Ctrl-C stops both. If either port is already in use the script kills the existing process first.

### Run the API only

```bash
# Create staging directory on first run (one-time):
mkdir -p src/Connector.Api/staging

cd src/Connector.Api
dotnet run
```

The API starts on **`http://localhost:5189`** (see `Properties/launchSettings.json`).

On first start in `Development` mode the demo ERP databases are created and seeded automatically. The export worker fires at the UTC time set in `ExportWorker.ScheduledTimeUtc` (default `00:00:01` — midnight + 1 s). To trigger an immediate run during development, temporarily set the value to ~2 minutes from now and restart.

**Dev credentials:** `alice / alice123` and `bob / bob123` (seeded automatically in Development mode).

---

## Demo ERP Database

The `Connector.Erp` project contains a self-contained SQLite demo database that stands in for the real ERP. It models the ERP schema described in the ICD concept document:

| Table | Purpose |
|---|---|
| `masterdata` | Product model definitions (article/model records) |
| `systemconfiguration` | Installed CI instances with serial numbers |
| `articlestructure` | BOM parent/child relationships |
| `maintenance_plan` | Links a CI to an active maintenance contract |

### Seed scenario

```
sc-rack-0001  SN-RACK-0001  Industrial Rack System    Active    → in scope (root)
  sc-blade-0001  SN-BLD-0001   Compute Module MK2        Active    → in scope
  sc-blade-0002  SN-BLD-0002   Compute Module MK2        InRepair  → in scope
  sc-psu-0001    SN-PSU-0001   Power Supply 2400W        Active    → in scope
  sc-psu-0002    SN-PSU-0002   Power Supply 2400W        Active    → EXCLUDED (no maintenance plan)
  sc-sw-0001     SN-SW-0001    Managed Switch 24P        Active    → in scope
sc-rack-0002  SN-RACK-0002  Industrial Rack System    Decommissioned → EXCLUDED (plan inactive)
```

Expected export record count: **5** (rack-0001, blade-0001, blade-0002, psu-0001, switch-0001)

Each `systemconfiguration` record also carries `TechnicianName` (personal data) and `StorageLocation` (Open Point #4). These fields are present in the raw ERP output and stripped by `DataMinimizer` before any file is written.

---

## API Endpoints

All endpoints except `/api/auth/login` require a JWT bearer token (`Authorization: Bearer <token>`).

| Method | Path | Description |
|---|---|---|
| `POST` | `/api/auth/login` | Authenticate — body: `{"username":"...","password":"..."}` → `{"token":"..."}` |
| `GET` | `/api/health` | Health check (no auth) — checks ERP DB, log DB, and staging writability |
| `GET` | `/api/exports` | List all export runs (newest first), with short SHA and `isStale` flag |
| `GET` | `/api/exports/{seqNo}` | Full detail for one run, including delivery fields and `sequenceGapWarning` |
| `POST` | `/api/exports/{seqNo}/release` | Four-eyes release — body: `{"approver":"..."}` (operator from JWT) |
| `POST` | `/api/exports/{seqNo}/deliver` | Record physical handover — body: `{"importedRecordCount":N,"notes":"..."}` |
| `GET` | `/api/source-schema` | Source database tables and columns (schema browser) |
| `GET` | `/api/erp/records` | All ERP CIs with scope flags, BOM parent links, and excluded GDPR fields |
| `GET` | `/api/schema` | Export schema version, column mappings, and persisted active flags |
| `PATCH` | `/api/schema/columns` | Persist active column set — body: `{"columns":["guid","serial_number",...]}` |
| `POST` | `/api/pipeline/run?format=xlsx\|csv\|json` | Trigger an immediate pipeline run in the requested format (default: xlsx) |

The release endpoint enforces `Operator != Approver` and rejects runs already in `Released` or `Failed` status.

---

## Configuration

All runtime configuration lives in `appsettings.Development.json` during development:

```jsonc
{
  "ConnectionStrings": {
    "ExportLog": "Data Source=export_log.db",    // export-run audit log
    "DemoErp":   "Data Source=demo_erp.db"       // demo ERP (created on startup)
  },
  "ExportSink": {
    "StagingPath": "./staging"                   // where export files land
  },
  "ExportWorker": {
    "ScheduledTimeUtc": "00:00:01"              // 1 second after midnight UTC (dev only)
  }
}
```

For production, replace `DemoErp` with the real ERP PostgreSQL connection and swap `DemoErpReader` for the production `IErpReader` implementation.

---

## Connecting a Real ERP

The real ERP (PostgreSQL-backed) is not yet implemented. To add it:

1. Create a new class in `Connector.Erp` implementing `IErpReader`
2. Inject `NpgsqlConnection` (or `IDbContextFactory<RealErpDbContext>`)
3. Replace the `DemoErpReader` registration in `Program.cs`
4. Add `Npgsql` or `Npgsql.EntityFrameworkCore.PostgreSQL` to `Connector.Erp.csproj`

The pipeline, tests, and API are all decoupled from the reader via `IErpReader` — no other files change.

---

## Frontend (Vue 3)

The release UI lives at `src/connector-ui/` — a Vue 3 + TypeScript + Vue Router app scaffolded with `npm create vue@latest`.

The UI follows a **four-step workflow** that mirrors the actual connector process:

| Step | Route | View | Purpose |
|---|---|---|---|
| 1 | `/connect` | `ConnectionView` | Configure and test the source database connection |
| 2 | `/source-schema` | `SourceSchemaView` | Browse tables and columns read from the source DB |
| 3 | `/export-schema` | `SchemaView` | Toggle export columns on/off and choose output format |
| 4 | `/exports` | `ExportView` | Trigger export (xlsx / csv / json), preview data, review past runs |

```
src/connector-ui/
├── src/
│   ├── views/
│   │   ├── LoginView.vue        ← JWT login form
│   │   ├── ConnectionView.vue   ← Step 1: source DB connection config + test
│   │   ├── SourceSchemaView.vue ← Step 2: expandable table/column browser (GET /api/source-schema)
│   │   ├── SchemaView.vue       ← Step 3: column toggles + format picker (GET /api/schema)
│   │   ├── ExportView.vue       ← Step 4: format select + run + preview + run history
│   │   └── ExportDetail.vue     ← detail + four-eyes release form
│   ├── components/
│   │   └── ReleaseDialog.vue    ← four-eyes form (approver field; operator inferred from JWT)
│   ├── api/
│   │   ├── auth.ts              ← login / token storage
│   │   ├── erp.ts               ← listErpRecords, getSchema
│   │   ├── exports.ts           ← typed fetch wrappers for export endpoints
│   │   ├── pipeline.ts          ← runNow(format), getPreview
│   │   └── connection.ts        ← getSourceSchema
│   └── __tests__/               ← Vitest + @vue/test-utils test suite
├── package.json
└── vite.config.ts               ← proxy /api → http://localhost:5189
```

### Run the frontend

```bash
cd src/connector-ui
npm install       # first time only
npm run dev       # dev server on http://localhost:5173
```

Vite proxies all `/api/*` requests to the backend on `:5189` — no CORS configuration needed.

### Run frontend tests

```bash
cd src/connector-ui
npm test          # vitest run (~1 s)
npm run coverage  # with coverage report
```

### UI constraints

- **JWT auth required.** All views redirect to `/login` when no valid token is present. Dev users `alice` and `bob` are seeded automatically.
- **Four-eyes rule enforced server-side.** Client validates `operator != approver`; the server is authoritative.
- **Status transitions are one-way.** `Released` and `Failed` runs hide the release form.
- **SHA-256 short in list, full on detail.** Server returns 12-char prefix as `sha256Short`; `sha256` is the full hex string.
- **Column toggles are server-persisted.** `SchemaView` calls `PATCH /api/schema/columns` on every toggle; the active set survives page reload. `GET /api/schema` reflects the saved preference. (The scheduled nightly export always includes all columns; on-demand runs respect column preferences in future iteration.)
- **Source schema shows demo DB structure.** `SourceSchemaView` reads `/api/source-schema`, which returns the schema of the demo ERP (mirroring what a production PostgreSQL reader would expose).
- **Export format persisted in `localStorage`.** The format choice (xlsx / csv / json) is remembered across sessions per browser.

---

## Code Quality

Static analysis and formatting run automatically — no manual setup required beyond a normal `dotnet restore`.

### Static analysis

[Directory.Build.props](Directory.Build.props) applies to every project in the solution:

| Setting | Effect |
|---|---|
| `AnalysisLevel=latest` | All Roslyn / CA rules at the latest rule set |
| `EnforceCodeStyleInBuild=true` | IDE code-style rules (IDE*) are enforced at build time |
| `TreatWarningsAsErrors` (Release only, non-test projects) | Any analyzer warning fails the Release build — this is the CI gate |
| `SonarAnalyzer.CSharp` | Sonar bug and code-smell rules |
| `Roslynator.Analyzers` | Additional idiomatic C# rules |

The Debug build keeps warnings as warnings so the inner dev loop stays fast.

### Formatting (CSharpier)

CSharpier is installed as a local dotnet tool (`.config/dotnet-tools.json`). Restore it once:

```bash
dotnet tool restore
```

Check formatting (what CI does):

```bash
dotnet csharpier --check .
```

Apply formatting:

```bash
dotnet csharpier .
```

Print width is 120 (see `.csharpierrc.json`).

### CI pipeline

GitHub Actions ([.github/workflows/ci.yml](.github/workflows/ci.yml)) runs on every push and PR to `main`:

1. `dotnet tool restore` — restores CSharpier
2. `dotnet csharpier --check .` — fails if any file is not formatted
3. `dotnet build -c Release` — fails on any analyzer warning or code-style violation
4. `dotnet test -c Release` — runs the full test suite

---

## Open Development Tasks

These track items from the concept document that directly block or affect the implementation:

| # | Topic | Impact on Code |
|---|---|---|
| 3 | Classification marking | Release API may need a marking field |
| 4 | `StorageLocation` entitlement | `DataMinimizer` and `ExportSchema` need updating if confirmed in scope |
| 5 | CI population volume | Informs whether pagination is needed in `IErpReader` |
| 7 | Retention policy | Drives when `ExportRun` records can be purged |
| 8 | Maintenance allocation chart import | Determines filter predicate in `DemoErpReader` / production reader |

Full detail: `../connector_document/decisions/13-open-points.md`
