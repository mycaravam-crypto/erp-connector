# Phase 14 Implementation Plan — Generic, tree-based multi-export definitions

> Working plan for implementing the "Phase 14" scope described in `REQUIREMENTS-2.0.md` and
> tracked as "Planned next" in `ROADMAP.md`. Saved here (rather than only in a local plan-mode
> scratch file) so a fresh session on this branch can pick up implementation without re-deriving
> the grounding research below.

## Status as of 2026-08-19

- No code has been written yet — this document is the plan only.
- **Environment constraint**: the sandbox this plan was drafted in has no `dotnet` SDK installed
  (confirmed via `which dotnet` — not found, and no install under any common path). All backend
  work described below must be written carefully by hand against the exact patterns cited, but
  **cannot be locally compiled, migrated, or test-run in an SDK-less session** — rely on CI (this
  repo's `dotnet build`/`dotnet test`/`csharpier check` pipeline) to catch compile errors, and
  treat any push as "needs a CI round-trip before trusting it," not "verified." If a future session
  on this branch *does* have the SDK, use it locally instead of guessing.
- Recommended execution order is the 6 slices below, each roughly PR-sized. Start with Slice 1 —
  everything else depends on the `ExportNode`/`ExportDefinition` shape being settled first.

## Context

`REQUIREMENTS-2.0.md` (status "Draft for review") specs the next piece of work for this connector:
today there is exactly **one** exportable mapping (plus save-slot "presets"), for one source
table, with three parallel and overlapping shapes (`Fields`, `Relations`, `NestedGroups`) —
nesting only works for JSON output. `TODO-code-health.md` is a separate, lower-priority
frontend-complexity backlog (items 6–11: `NestedGroupEditor.vue`, `IcdSchemaView.vue`,
`SettingsView.vue`, `AuditView.vue`, `App.vue`) that is explicitly **not required** by CI and
orthogonal to Phase 14 — noted for completeness, not part of this plan.

The goal: replace the single mapping + presets with **N independently named, saved, scheduled
export definitions**, each rooted at any table, each with **unlimited nesting depth in every
output format** (today only JSON nests), field-level transforms, row filters, and its own
execution history — implemented as *one* recursive tree type/query-builder/UI component
(`ExportNode`), not three parallel shapes. This plan is grounded directly in the current code (see
facts below), not written from a blank page.

**Note (out of scope but relevant):** `knowledge/processes/open-points.md` still references
entities deleted in Phase 13 (`IErpReader`, `IDataMinimizer`, `ExportItem`) in its "Related"
section and open points #5/#8. Not touched by this plan — flagging so a future doc pass catches it.

## Current-code facts this plan is grounded in

- **Persistence**: no dedicated mapping table today — `export_mapping`/`export_presets` are JSON
  blobs in `AppSetting.Value` (PK `Key`), via `SettingsKeys` + `GetSettingAsync<T>`/
  `SetSettingAsync<T>`/`GetSettingRawAsync` extension methods on `ExportLogDbContext`
  (`src/Connector.Infrastructure/ExportLogDbContext.cs:7-45`). Only one EF migration exists so far
  (`InitialSchema`, `src/Connector.Infrastructure/Migrations/`) — Phase 14 adds the second.
- **Mapping shapes**: `ExportMappingField`/`ExportMappingRelation`/`ExportMappingNestedGroup` in
  `src/Connector.Core/DynamicExport/ExportMappingTypes.cs`. `ExportMappingNestedGroup` is already
  a self-referencing tree (`Children: ExportMappingNestedGroup[]`) but JSON-only, and
  `GetColumnNames`/flat CSV/Excel logic never look at it — that's exactly the gap `ExportNode`
  closes. `ExportMappingJson.DeserializeConfig`/`DeserializePresets` (same file, lines 77-129) are
  the mandatory backfill-on-read entry points — never call `JsonSerializer.Deserialize` directly on
  these blobs.
- **Query/service engine**: `DynamicExportService` (static class, actually in
  `src/Connector.Infrastructure/DynamicExportService.cs`, *not* `Connector.Core` despite the
  requirements doc's package name). Key existing pieces to reuse/generalize:
  `UsesNestedJson` (single decision point shared by Run Now/Preview/Worker — a generalized
  `ExportNode` engine must preserve exactly one such decision point), `BuildNestedGroupExpr`
  (recursive SQL builder — the direct ancestor of the new tree-walking query builder),
  `MaxNestedDepth = 16`, `QI()`/`SqlLit()` identifier quoting, `GetDeniedFieldsAsync` (GDPR
  denylist, must apply at every depth as it already does for `NestedGroups`).
- **Endpoints**: plain `static class X { internal static void MapXEndpoints(this WebApplication app) }`
  modules wired in `Program.cs:168-175`, every route `.RequireAuthorization()`. See
  `ExportMappingEndpoints.cs` (recursive validator pattern: `ValidateNestedGroups`/
  `ValidateNestedGroup`, `SqlIdentifierRegex`, depth guard, duplicate-key check, GDPR check) and
  `PipelineEndpoints.cs` (Run Now / Preview) as the templates to follow.
- **Scheduler**: `ExportWorker : BackgroundService` (`src/Connector.Infrastructure/ExportWorker.cs`)
  — one global daily time + one global format, polls via `Task.Delay` to next occurrence. Phase 14
  adds a **sibling** worker, does not touch this one (legacy pipeline stays as-is per §10
  Non-Goals / the requirements doc's explicit instruction).
- **Frontend**: `NestedGroupEditor.vue` (208 lines, already a self-referencing Vue 3 SFC via
  `defineOptions({ name: 'NestedGroupEditor' })`, local `MAX_NESTED_DEPTH = 16` duplicated from the
  backend constant) is *already* the recursive tree editor the requirements doc calls for — it
  needs to become format-agnostic and gain scalar-field mapping UI, not be rebuilt.
  `api/mapping.ts` is the fetch-wrapper template for a new `api/exportDefinitions.ts`.
  `router/index.ts` guards (`needsLogin`, `needsConnection`) are the pattern for new routes.
- **Tests**: `ExportMappingJsonTests.cs` (backfill/normalize), `DynamicExportServiceTests.cs`
  (pure unit, byte-serialization + `UsesNestedJson`), `DynamicExportServiceNestedJsonPostgresTests.cs`
  (7 real-Postgres integration tests against the `testdb` fixture — the direct template for testing
  the new tree query builder), `mapping-api.test.ts` (template for the new API-wrapper test file).
  **No existing test file exercises `NestedGroupEditor`/`NestedGroupsSection` directly** — gap to
  close for the generalized editor.

## Design decisions carried over from `REQUIREMENTS-2.0.md` §11 (Open Decisions)

Adopting the doc's own recommendations, since no stakeholder input contradicts them:
1. Cron minimum granularity: **hourly**, matching this project's existing "hourly minimum"
   scheduling convention.
2. Legacy `/api/export-mapping` endpoints: **kept read-only** after the one-time converter runs
   (return 200 with the migrated config's read-model, reject writes with 410 Gone or similar),
   removed in a following release — not in this phase.
3. Test-run cap: **50 rows**, fixed (not user-configurable) for this phase — smallest thing that
   satisfies the requirement; can become configurable later if asked for.

## Implementation slices (recommended order — each is independently testable/shippable)

### Slice 1 — Data model + migration converter (backend foundation) — START HERE

- New file `src/Connector.Core/DynamicExport/ExportNode.cs` (or extend `ExportMappingTypes.cs`):
  `ExportNode` record (`TargetKey`, `Kind` — follow this codebase's existing convention of a plain
  string discriminator like `ExportMappingNestedGroup.Kind` ("object"|"array"), extended with
  "root"|"scalar-field", rather than introducing a C# enum + `JsonStringEnumConverter` this
  codebase doesn't otherwise use; `SourceField?`, `RelatedTable?`, `JoinKey?`, `SourceJoinKey?`,
  `Filter?`, `Mapping: FieldMapping?`, `Children: ExportNode[]`, `Enabled`), `FieldMapping` record
  (`DefaultValue?`, `Transform` string discriminator `none|uppercase|lowercase|trim|dateFormat|constant`,
  `TransformArg?`, `DataType` string discriminator `string|number|boolean|date`). Follow the
  existing XML-doc convention (state *why*, matching `ExportMappingTypes.cs`'s style) per the
  requirements doc's Engineering Directive. Add an `ExportNodeJson` normalizer mirroring
  `ExportMappingJson`'s backfill pattern if `ExportNode` needs the same missing-property protection
  once it's persisted as raw JSON (likely yes, per the `ExportDefinitionEntity` storage choice
  below).
- New EF entities in `src/Connector.Infrastructure/`: `ExportDefinitionEntity` (Id, Name,
  Description, RootTable, RootNode — stored as a JSON string column via `JsonSerializer`, same
  approach as `AppSettingEntity.Value`, since this codebase has no prior use of EF's native
  JSON-column support; OutputFormat, IsEnabled, Schedule?, ConfigVersion, CreatedBy/At,
  UpdatedBy/At) and `ExportDefinitionRunEntity` (mirrors `ExportRunEntity`'s plain-POCO style,
  Id/ExportDefinitionId/ConfigVersion/StartedAt/FinishedAt/Status/RecordCount/ErrorMessage/
  TriggeredBy).
- Extend `ExportLogDbContext`: add `DbSet<ExportDefinitionEntity>`, `DbSet<ExportDefinitionRunEntity>`,
  `OnModelCreating` fluent config (table names, PKs, an index on `ExportDefinitionId` for the run
  table — same pattern as `IX_ExportRun_SequenceNo`). Add new `SettingsKeys` only if a new
  AppSettings key is actually needed (the migration converter reads existing keys, doesn't add
  one).
- New migration (hand-written, matching `20260701083054_InitialSchema.cs`'s shape exactly, since
  `dotnet ef migrations add` isn't runnable in an SDK-less sandbox — a session with the SDK should
  instead generate it properly and diff against the hand-written version): adds `ExportDefinition`
  and `ExportDefinitionRun` tables plus the Designer/snapshot files. **Do not trust a hand-written
  migration without a session that can actually run `dotnet ef migrations add`/`dotnet build`
  against it first** — flag this explicitly when handing off.
- One-time startup converter (new static method, e.g. `ExportDefinitionMigrator.MigrateLegacyMappingsAsync`,
  called once from `Program.cs` near `BootstrapMigrationsAsync`/`MigrateAsync`): reads
  `export_mapping` + `export_presets` via `ExportMappingJson.DeserializeConfig`/`DeserializePresets`
  (never raw `JsonSerializer.Deserialize` — must go through the backfill layer), converts each
  `ExportMappingField`→scalar-field node, `ExportMappingRelation`→array node with flattening info
  preserved in `Mapping`/a flatten marker, `ExportMappingNestedGroup`→object/array node verbatim
  (recursive), writes one `ExportDefinitionEntity` per legacy config (name = preset name, or a
  fixed name like `"Legacy Export"` for the unnamed single mapping) if no `ExportDefinitionEntity`
  rows exist yet (idempotency guard — only run once, never re-run/duplicate on every app start).
  Leaves the `AppSettings` rows in place, unread by anything else after this.
- **Tests**: `ExportNodeTests.cs` unit tests (tree shape, `FieldMapping` transform discriminator),
  `ExportDefinitionMigratorTests.cs` covering: empty state (no-op), single legacy mapping converts
  correctly (flat fields + relations + nested groups all present), presets each become a separate
  `ExportDefinition`, idempotency (running twice doesn't duplicate rows), depth preserved on a
  3-level nested legacy config (reuse the exact fixture shape from
  `ExportMappingJsonTests.NestedGroupMissingChildrenAndFields_BackfilledRecursivelyThreeLevelsDeep`).

### Slice 2 — Query/format-writer engine

- Generalize `DynamicExportService` (or add a new sibling if the existing static class would
  otherwise need two unrelated code paths side by side — prefer extending in place per the "prefer
  widening an existing type" directive, splitting only if the diff would otherwise tangle
  legacy-mapping and `ExportNode` logic together): one recursive tree-walking method that builds a
  single SQL query from any `ExportNode` tree, generalizing `BuildNestedGroupExpr`'s recursion
  (same `MaxNestedDepth` guard, same `QI()`/`SqlLit()` escaping, same alias-counter collision
  avoidance) to also emit plain columns for `Kind="scalar-field"` (with `Mapping.DataType`
  coercion — `::text`/`::numeric`/etc — and `Filter` as a `WHERE` fragment scoped to that node's
  table), not just JSON object/array shapes.
- `IExportFormatWriter` interface (per requirements §8 OCP) with three implementations
  (CSV/Excel/JSON) that each accept the same `ExportNode`-shaped result set and either honor
  nesting or flatten it per documented rules — reuse `BuildCsvBytes`/`BuildJsonBytes`/
  `BuildNestedJsonBytes`/`BuildExcelBytes`'s existing serialization logic where the row shape
  matches; adapt where CSV/Excel now need to flatten what used to only be reachable via
  `NestedGroups` (arbitrary depth in every format is the actual new capability here). Apply
  `FieldMapping.Transform` (uppercase/lowercase/trim/date-format/constant) and `DefaultValue`
  null-fallback at the point each scalar value is read, before serialization. A single "does this
  tree need the nested-JSON code path" decision function (successor to `UsesNestedJson`) must
  remain the one place Preview/Run-Now/Test/Scheduler all call — do not let it re-fork into 3
  independently-normalizing call sites like the pre-Phase-13 bug the requirements doc calls out.
- **Tests**: extend `DynamicExportServiceNestedJsonPostgresTests.cs`'s pattern (or a new
  `ExportNodeQueryPostgresTests.cs`) against the `testdb` fixture: object/array nesting at 3+
  levels in CSV and Excel (not just JSON — this is the actual new coverage), transform application
  (each of the 5 transform kinds), `DefaultValue` null-fallback, `Filter` fragment scoping,
  GDPR-denylist stripping at every depth in every format, empty-array-not-null in all formats.

### Slice 3 — API endpoints (CRUD + run + history)

- New `ExportDefinitionEndpoints.cs` module (same static-class-extension-method pattern as
  `ExportMappingEndpoints.cs`), registered in `Program.cs` alongside the existing
  `app.MapXxxEndpoints()` chain:
  - `GET/POST /api/export-definitions`, `GET/PUT/DELETE /api/export-definitions/{id}`,
    `POST /api/export-definitions/{id}/duplicate`, `PATCH .../enable` (`IsEnabled` toggle).
  - `POST /api/export-definitions/{id}/preview` (capped rows, same query path as run),
    `POST /api/export-definitions/{id}/run` (full run → `ExportDefinitionRunEntity` row, writes to
    staging via the existing `IExportSink`), `POST /api/export-definitions/{id}/test` (50-row cap,
    flagged as test in the run row, same code path per requirements §6 — no untracked shortcut).
  - `GET /api/export-definitions/{id}/runs` (history list).
  - Validator: adapt `ExportMappingEndpoints`'s recursive-validator pattern
    (`ValidateNestedGroup`→generalized `ValidateExportNode`) to the unified `ExportNode` shape:
    depth guard, identifier-safety regex on every identifier field (this time applied uniformly,
    closing the gap the old validator deliberately left open for `Fields`/`Relations`), GDPR
    denylist at every depth, duplicate-`TargetKey` check among enabled siblings per level.
  - Legacy cutover: modify `ExportMappingEndpoints.cs`'s `PUT` handlers to return 410/405 once the
    migrator has run (§ Design decisions #2) — `GET` handlers stay as read-only pass-through.
- **Tests**: new `Connector.Integration.Tests/ExportDefinitionEndpointsTests.cs` (CRUD round-trip,
  validation rejection cases, duplicate/enable-toggle, run→history-row assertions) — confirm
  whatever integration-test harness the existing endpoint tests use (e.g. `WebApplicationFactory`)
  before writing new harness code; none was confirmed present as of this plan.

### Slice 4 — Scheduler

- New `ExportDefinitionWorker : BackgroundService` sibling to `ExportWorker` (registered via a
  second `AddHostedService<>()` in `Program.cs`), polling **enabled** `ExportDefinitionEntity` rows
  whose cron `Schedule` is due (hourly minimum granularity per Design decision #1) — a generalized
  version of `ExportWorker`'s delay-loop, but keyed per-definition instead of one global time.
  Every run (scheduled or manual) writes exactly one `ExportDefinitionRunEntity` row with
  `Status = Failed` + specific `ErrorMessage` on any failure or zero-record result — never a
  silent partial success, matching `ExportRunStatus`'s existing reliability contract.
- **Tests**: unit tests around cron-due computation (check `Connector.Infrastructure.csproj` for an
  existing Cronos/NCrontab-style package before adding a new dependency — none was confirmed
  present as of this plan, so evaluate the smallest option), plus an integration test that a due
  definition produces exactly one run row.

### Slice 5 — Frontend

- New `api/exportDefinitions.ts` (mirrors `api/mapping.ts`'s fetch-wrapper/type-mirroring pattern)
  with camelCase types mirroring `ExportNode`/`FieldMapping`/`ExportDefinition`.
- Generalize `NestedGroupEditor.vue` in place: the component already handles depth/children
  recursion correctly, it needs (a) a `scalar-field` node kind with inline `FieldMapping` transform
  UI, (b) to stop being gated behind "JSON format only" in `SchemaView.vue`/`NestedGroupsSection.vue`,
  (c) optionally, `MAX_NESTED_DEPTH` fetched from a validate-endpoint response instead of
  hand-duplicated (polish, not required).
- New `ExportDefinitionsListView.vue` (name/root-table/format/enabled-toggle/last-run/next-run/
  actions table) + `ExportDefinitionEditView.vue` (tree builder: root-table picker reusing
  `SchemaView.vue`'s existing schema-introspection dropdown, preview panel calling the same
  preview endpoint Run Now uses, execution-history panel backed by
  `GET /api/export-definitions/{id}/runs`).
- New routes in `router/index.ts` (`/export-definitions`, `/export-definitions/:id`) with the same
  `needsLogin`/`needsConnection` guard pattern as existing routes.
- **Tests**: `exportDefinitions-api.test.ts` (mirrors `mapping-api.test.ts`), component tests for
  the generalized node editor's new scalar-field/transform UI (closing the pre-existing gap that
  `NestedGroupEditor` has zero dedicated test coverage today), `ExportDefinitionsListView.test.ts`/
  `ExportDefinitionEditView.test.ts` following `SchemaView.test.ts`'s coverage breadth (load/error
  states, CRUD actions, save flow, preview). Manually verify in a running browser (`npm run dev`)
  per this project's UI-change convention — build a 3-level nested export using only the tree UI as
  the acceptance check from requirements §8 Usability.

### Slice 6 — Docs

- New `knowledge/dynamic-export/` bundle (mirrors `knowledge/domain|schema|pipeline|processes/`
  structure): pages for the `ExportNode` tree, the new scheduler, and the run-history entity —
  extends `knowledge/pipeline/dynamic-export-service.md` (the Phase 13 baseline), does not replace
  it (legacy single-mapping docs stay until the read-only-then-removed cutover completes).
- `ROADMAP.md`: new "## Phase 14 — Generic export definitions ✅" entry on completion, following
  the exact table format every prior phase entry uses; move the "Planned next" line at the top to
  point at whatever's next after this.

## Verification (end to end, after all slices)

1. `dotnet build Connector.sln -c Release` clean (warnings-as-errors baseline), `dotnet csharpier
   check .` clean, full `dotnet test` suite green including new Postgres-integration tests against
   the `testdb` fixture (`docker-compose --profile test up -d testdb`).
2. `npm run type-check && npm run test` in `src/connector-ui`, `npx fallow audit --base
   origin/main` clean (or document any new findings the way `TODO-code-health.md` already does).
3. Manual browser walkthrough (`npm run dev` + API): create a new export definition rooted at a
   table with a 3-level nested relation (e.g. `manufacturer` → `manufacturer_address`, using the
   `testdb` fixture data), add a field transform (e.g. uppercase) and a `DefaultValue`, preview it,
   save it, set an hourly schedule, run it manually, confirm the run appears in execution history
   with the right `ConfigVersion`, confirm CSV/Excel output now contains the nested data flattened
   (the concrete new capability over today's JSON-only nesting).
4. Confirm the legacy single-mapping flow (`/export-schema` page) still round-trips read-only after
   the one-time migration converter runs, and that the converter does not duplicate rows on a
   second app restart.

## Handoff checklist for a new session

- [ ] Slice 1 — data model + entities + migration + converter + tests
- [ ] Slice 2 — query/format-writer engine + tests
- [ ] Slice 3 — API endpoints + tests
- [ ] Slice 4 — scheduler + tests
- [ ] Slice 5 — frontend + tests + manual browser verification
- [ ] Slice 6 — docs (`knowledge/dynamic-export/`, `ROADMAP.md` Phase 14 entry)

None of the above are checked off yet. A new session should re-read this file, `REQUIREMENTS-2.0.md`,
and `ROADMAP.md` before starting, then proceed slice by slice — each slice's own "Tests" bullet is
the acceptance bar for marking it done. If a session has the `dotnet` SDK available, use it to
actually run `dotnet ef migrations add`, `dotnet build`, and `dotnet test` rather than hand-writing
and hoping, especially for Slice 1's migration file.
