# Connector — Implementation Roadmap

Tracks progress against the AI Coding Agent Roadmap (session-persistent).  
Last updated: 2026-06-28 (session 2)

---

## Status Key

| Symbol | Meaning |
|---|---|
| ✅ | Done and verified |
| 🔄 | In progress (current session) |
| ❌ | Not yet started |
| ⚠️ | Done but needs fix |

---

## Phase 0 — Document Reconciliation ✅

| Task | Status | Notes |
|---|---|---|
| 0.1 Fix Correlation Key (Section 5.3) | ✅ | GUID is coalesce key; serial no longer blocks export |
| 0.2 Update Scope Predicate (IErpReader doc) | ✅ | Maintenance-plan predicate explicit in interface doc |
| 0.3 Sync Open Points (Section 10) | ✅ | 8 items, #1 and #2 resolved; retention worker noted |

File: `../connector_document/TECHNICAL_CONCEPT.md`

---

## Phase 1 — Solution Setup & Domain Contracts

| Task | Status | Notes |
|---|---|---|
| 1.1 Project scaffolding | ✅ | All 5 projects + strict dependency rules |
| 1.2 Domain models | ✅ | `Guid` added to `ErpConfigurationItem`, `ExportItem`, `MappedExportRecord` |
| 1.3 Pipeline interfaces | ✅ | All 6 interfaces with XML docs |

---

## Phase 2 — Pipeline Implementation

| Task | Status | Notes |
|---|---|---|
| 2.1 ExportFilter | ✅ | Blocks on missing GUID; missing serial allowed |
| 2.2 DataMinimizer | ✅ | Passes `Guid` through; `SerialNumber` nullable |
| 2.3 SchemaMapper | ✅ | Throws `InvalidCorrelationKeyException` on empty GUID; maps Guid field |

---

## Phase 3 — Infrastructure, I/O & Orchestration

| Task | Status | Notes |
|---|---|---|
| 3.1 ExcelPackager | ✅ | `guid` written as first column; all columns shifted |
| 3.2 SQLite Export Log | ✅ | `ExportRun` table with all required fields |
| 3.3 ExportWorker | ✅ | BackgroundService with PeriodicTimer, try/catch, Failed status |
| 3.4 Data Retention Cleanup | ✅ | Daily purge of staging files + Released/Failed log rows; configurable `RetentionDays` |

---

## Phase 4 — Release API & Frontend

| Task | Status | Notes |
|---|---|---|
| 4.1 ASP.NET Minimal API | ✅ | GET /api/exports, GET /api/exports/{seqNo}, POST /api/exports/{seqNo}/release |
| 4.2 Vue.js UI scaffolding | ✅ | Vite + Vue 3 + TypeScript; proxy to :5189 |
| 4.3 ExportList view | ✅ | Traffic-light status badges |
| 4.4 4-Eyes Release Dialog | ✅ | Operator ≠ Approver enforced client + server side |
| 4.5 ERP database API | ✅ | GET /api/erp/records — all CIs with scope flags, BOM links, excluded fields surfaced |
| 4.6 Schema API | ✅ | GET /api/schema — schema version + full column mapping definitions |
| 4.7 Navigation bar | ✅ | App header nav: Export Runs · ERP Database · Export Schema with active-link highlighting |
| 4.8 ERP Database view | ✅ | BOM tree (expand/collapse) · flat list with search + column sort · per-row detail panel showing excluded fields with GDPR / Open Point #4 tags |
| 4.9 Export Schema view | ✅ | Schema version badge · active column mapping table · pending fields section · coalesce key explanation — **read-only; schema is hardcoded in Program.cs** |
| 4.10 Workflow-based UI restructure | ✅ | Nav replaced with 4-step flow: Connect → Source Schema → Export Schema → Export; step badges + numbered circles in header |
| 4.11 ConnectionView (Step 1) | ✅ | Form for Postgres host/port/db/user/password; Test Connection button calls `/api/source-schema`; config persisted to `localStorage` |
| 4.12 SourceSchemaView (Step 2) | ✅ | Expandable table/column browser; calls `/api/source-schema`; shows type, nullable, PK per column |
| 4.13 `/api/source-schema` endpoint | ✅ | Returns all 4 ERP tables with column types — mirrors what a production PostgreSQL reader would expose |
| 4.14 Export Schema column toggles (Step 3) | ✅ | Checkboxes enable/disable columns; active count badge updates live; format picker (xlsx/csv/json) saved to `localStorage` |
| 4.15 ExportView — merged Step 4 | ✅ | Format picker + Run Export button + preview table + export run history in one view |
| 4.16 Multi-format export (csv/json) | ✅ | `POST /api/pipeline/run?format=xlsx\|csv\|json`; CSV and JSON built inline; `BuildManifestFileName` fixed to handle non-xlsx extensions |

---

## Phase 5 — Tests

| Task | Status | Notes |
|---|---|---|
| 5.1 Unit tests — DataMinimizer | ✅ | GUID in `MakeItem`; `Minimize_PreservesGuid` added |
| 5.2 Unit tests — 4-Eyes Release | ✅ | Operator == Approver → 400 |
| 5.3 Format hazard tests | ✅ | `Map_EmptyGuid_Throws…`; `Map_GuidPreservedAsString`; `Map_NullSerial_ReturnsEmptyString` |

Integration tests: ✅
- Schema version `2.0` asserted
- `FullPipeline_Guid_MatchesErpId` added
- `ExportSchemaTests` snapshot updated (7 columns incl. `guid`)

Frontend tests (Vitest): ✅ 51/51 passing
- `erp-api.test.ts` — `listErpRecords`, `getSchema` API wrappers
- `ErpDatabaseView.test.ts` — BOM tree, expand/collapse, search, scope filter, detail panel (20 tests)
- `SchemaView.test.ts` — version badge, column table, pending fields, coalesce note (8 tests)

---

## Phase 6 — Operational Enhancements (current)

Goal: close the loop between export and vendor import; make the daily workflow safer and more observable.
YAGNI constraint: no premature abstraction; each item is the minimum that delivers value.

| Task | Status | Notes |
|---|---|---|
| 6.1 Health check endpoint | ✅ | `GET /api/health` — ERP DB, log DB, staging writability; no auth |
| 6.2 Stale pending indicator | ✅ | `IsStale` on `ExportRunSummary`; ExportView shows callout when Pending > 24h |
| 6.3 Sequence gap detection | ✅ | `GET /api/exports/{seqNo}` returns `SequenceGapWarning`; ExportDetail shows banner |
| 6.4 Delivery acknowledgement | ✅ | `POST /api/exports/{seqNo}/deliver`; delivery fields on `ExportRunEntity`; closes custody chain |
| 6.5 Schema column persistence | ✅ | `AppSetting` table; `PATCH /api/schema/columns`; SchemaView toggles now saved server-side |

### Not in scope (Iteration 2)

| Item | Reason deferred |
|---|---|
| Real Postgres IErpReader | Requires ICD + connection config wired to backend |
| Delta/incremental export | Requires ERP change-tracking field and volume assessment (Open Point #5) |

---

## Remaining Work

42 .NET tests · 48 Vitest tests — all passing.

### Verified gaps (Iteration 2 scope)

| Gap | Current state | Work needed |
|---|---|---|
| Real Postgres IErpReader | Source schema and data still read from demo SQLite | Production `IErpReader` against real PostgreSQL |
| Connection form → backend | Host/port/db entered in Step 1 stored in localStorage only | Wire config to backend so `/api/source-schema` introspects actual Postgres target |

### Open points that will drive future code changes (tracked in `13-open-points.md`):
| Open Point | When it unblocks | Code impact |
|---|---|---|
| #3 Classification marking | Legal decision | Release API may need a marking field |
| #4 `storagelocation` entitlement | Data owner + legal | `DataMinimizer` and `ExportSchema` update if confirmed in scope |
| #5 Snapshot volume | ERP data steward | Pagination in `IErpReader` if > ~500k CIs |
| #6 Return-channel timing | Vendor + sponsor | Iteration 2 scope and schedule |
| #7 Retention periods | Legal + DPO | `RetentionDays` config value (default 30 — adjust when decided) |
| #8 Allocation chart import | ERP + vendor | Production `IErpReader` scope predicate |
