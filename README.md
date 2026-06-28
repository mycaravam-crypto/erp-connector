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
IPackager.PackageAsync()
    ↓  ExportPackage  (Excel bytes + ExportManifest with SHA-256 + sequence number)
IExportSink.WriteAsync()
    → staging/export_NNNN_YYYYMMDDTHHmmssZ.xlsx
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

### Build

```bash
cd /home/mycaravam/connector
dotnet build Connector.sln
```

### Run tests

```bash
dotnet test Connector.sln
```

### Run the API (demo mode)

```bash
cd src/Connector.Api
dotnet run
```

The API starts on `http://localhost:5000` (see `Properties/launchSettings.json`).

On first start in `Development` mode the demo ERP database is created and seeded automatically. The export worker runs its first export ~1 second after startup (configured in `appsettings.Development.json`).

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

| Method | Path | Description |
|---|---|---|
| `GET` | `/api/exports` | List all export runs (newest first), with short SHA |
| `GET` | `/api/exports/{seqNo}` | Full detail for one run |
| `POST` | `/api/exports/{seqNo}/release` | Four-eyes release — body: `{"operator":"...", "approver":"..."}` |

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

## Frontend (Vue 3) — Next Development Session

The release UI does not exist yet. A new session should scaffold it here:

```
Connector.sln
├── src/
│   └── connector-ui/          ← Vue 3 app lives here (to be created)
│       ├── src/
│       │   ├── views/
│       │   │   ├── ExportList.vue     ← table of all runs (GET /api/exports)
│       │   │   └── ExportDetail.vue   ← detail + release form (GET + POST)
│       │   ├── components/
│       │   │   └── ReleaseDialog.vue  ← four-eyes form (operator + approver fields)
│       │   └── api/
│       │       └── exports.ts         ← typed fetch wrappers for the three API endpoints
│       ├── package.json
│       └── vite.config.ts             ← proxy /api → http://localhost:5000
```

### What the UI must do

| Screen | Behaviour |
|---|---|
| Export list | Polls or loads `GET /api/exports`; shows sequence number, date, record count, short SHA, status badge |
| Export detail | `GET /api/exports/{seqNo}`; shows full manifest fields |
| Release | `POST /api/exports/{seqNo}/release` with `{ operator, approver }`; blocks if operator == approver (also enforced server-side); only shown for `Pending` runs |

### API the UI consumes

```
GET  /api/exports
     → [{ seqNo, extractedAt, recordCount, sha256Short, status, dataFileName }]

GET  /api/exports/{seqNo}
     → { id, sequenceNo, extractedAt, recordCount, sha256, status,
         releasedAt, operatedBy, approvedBy, dataFileName }

POST /api/exports/{seqNo}/release
     body: { "operator": "...", "approver": "..." }
     → 200 OK  |  400 Bad Request  |  404 Not Found  |  409 Conflict
```

### Setup instructions for the new session

```bash
# From repo root
cd src
npm create vue@latest connector-ui
# Choose: TypeScript, Vue Router, no Pinia, no Vitest for now
cd connector-ui
npm install
npm run dev          # dev server on :5173, proxies /api → :5000
```

Add to `vite.config.ts`:
```ts
server: {
  proxy: {
    '/api': 'http://localhost:5000'
  }
}
```

Run the backend alongside:
```bash
cd src/Connector.Api && dotnet run   # keeps the API + worker running on :5000
```

### Constraints to keep in mind

- **Four-eyes rule is enforced server-side.** The UI should validate client-side too but must not rely on it — the server rejects `operator == approver`.
- **Status transitions are one-way.** A `Released` or `Failed` run cannot be re-released. The UI should hide the release button for those states.
- **SHA-256 displayed shortened** in the list view (server already returns 12-char prefix via `Sha256Short`). Show full SHA on the detail page.
- **No auth yet.** The operator/approver fields are free-text strings for Iteration 1. Auth is an Iteration 2+ concern.

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
