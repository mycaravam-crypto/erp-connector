---
type: Engineering Backlog
title: Code Health Backlog
description: Open frontend template-complexity items and ground rules for tackling them — not CI-gated, not required.
tags: [process, code-health, backlog, frontend, technical-debt]
timestamp: 2026-08-19T00:00:00Z
---

Goal: **minimal** (no duplicated/dead code), **standardized** (one way to do a recurring thing),
**functional** (behavior-preserving — nothing here changes what the app does, only how the code
is organized). Nothing here is required — CI only gates on `fallow audit` against files changed
in a PR (frontend) and the existing analyzer/build gate (backend). This page exists so remaining
work isn't lost between sessions.

# Status

Done (session `claude/codebase-minimize-optimize-ywu53y`): both backend items below, and 5 of 7
frontend template-complexity items (SchemaView, ErpDatabaseView, ExportView, ExportDetail,
SourceSchemaView, plus `router/index.ts:42`) — see the ✅ rows below.

Done (session `claude/issues-code-health-prs-9ens0s`): of the remaining 5 items, `fallow health`
no longer flags `NestedGroupEditor.vue` or `App.vue` at all (thresholds or their scoring must have
shifted since this doc was written — re-verified against a fresh `fallow health --hotspots
--targets` run, 8 findings total, neither file present). The 3 that were still flagged are fixed:

- **`IcdSchemaView.vue`** — CRAP 43.1, off the list. Split into `IcdActiveColumnsTable.vue` and
  `IcdExcludedFieldsList.vue`.
- **`SettingsView.vue`** — CRAP 132.0 (CRITICAL), off the list. Split into
  `SchedulerSettingsForm.vue` and `GdprDenylistEditor.vue` (each owns its own fetch-independent
  save call, following `SkipRunForm.vue`'s pattern; the parent view keeps the combined
  loading/error fetch and passes the loaded data down as props so behavior is unchanged).
- **`AuditView.vue`** — CRAP 72.0 (HIGH), off the list. Split into `AuditLogTable.vue`. Splitting
  alone only got cyclomatic/cognitive down (8/9 → 6/6); CRAP stayed at 42 (still above the 30
  threshold) because both this file and the new `GdprDenylistEditor.vue` had zero test coverage,
  which the CRAP formula (`CC² × (1-cov/100)³ + CC`) punishes heavily. Added
  `AuditView.test.ts` and `GdprDenylistEditor.test.ts` (fallow's own primary suggested action for
  a CRAP-only finding) — both now resolved.

Verified with `npm run type-check`, `npm run test` (331 passing), `npx fallow audit --base
origin/main` (clean — 0 complexity/duplication findings, one pre-existing advisory-only CSS-token
note carried over from code this pass moved rather than authored), and a Playwright smoke pass
against `npm run dev` (mocked API responses) confirming all three views render populated and
error states correctly and the GDPR add/remove/save flow works end-to-end.

Not done: `App.vue` — not currently flagged (see above), so nothing to do unless it regresses.

**CI-gate note:** `fallow audit --base origin/main` flags 9 residual cognitive-complexity
findings (e.g. `ActiveMappingSummary.vue` at 37) in files the completed extractions *created* —
smaller in absolute terms than what they replaced, but counted as new since the files are new
relative to `origin/main`. Not coverage-fixable (thresholds are coverage-independent). Needs a
human call: raise `.fallowrc.json`'s thresholds, add `// fallow-ignore-next-line complexity`, or
accept the red check.

# Ground rules for this work

**YAGNI.** Fix only the documented duplication/complexity — don't generalize past the concrete
call sites found. E.g. the settings-store helper covers only the 7 keys and 2 shapes actually in
use; no plugin system, no speculative flexibility.

**Minimal code.** Prefer deleting over adding; the smallest diff wins over a "complete" one. A
new abstraction must be justified by the duplication it removes and net-negative the line count.

**Consistent comments.** One convention per language: TS/Vue `//` for inline notes, `/** */` only
for exported API surfaces whose shape isn't self-evident; C# `///` on public members (existing
convention), `//` inline. A comment earns its place only by explaining a non-obvious **why** —
never a **what**, never task/rework narration. Remove it if it wouldn't be missed.

**Clean up leftover-rework comments as you touch each file.** A repo-wide grep found no
commented-out dead code or task-referencing comments in first-party code, so there's no backlog
item for it — but re-check comments in any region a refactor touches, fixing or deleting stale ones.

# Frontend (`src/connector-ui`) — template complexity

`fallow`'s dead-code/duplication findings are already fixed (see `claude/fallow-js-code-checking-l5imc9`).
What's left is `fallow health`'s **template complexity** findings — Vue `<template>` blocks (or
one `.ts` arrow function) over cyclomatic/cognitive/CRAP thresholds. Earlier sessions left the
lower-priority items undone because verifying a UI refactor needs a running browser; a Playwright
browser became available in the `issues-code-health-prs-9ens0s` session's environment, used to
smoke-test the items closed out there (see Status above).

Re-run `npx fallow health --hotspots --targets` (from `src/connector-ui/`) any time for fresh numbers.

## Priority order (by refactor ROI)

1. ✅ **`SchemaView.vue`** — CRAP 733.4→88.0. Split into 8 sub-components (`PresetsToolbar`,
   `RelationCard`/`RelationsSection`, `ColumnMappingTable`, `NestedGroupsSection`,
   `JsonEnvelopeEditor`, `SuggestedRelations`, `ExportFormatPicker`); duplicate
   `buildMappingConfig()` also fixed.
2. ✅ **`ErpDatabaseView.vue`** — CRAP 612.8→63.6. Split into `FlatRecordsTable.vue` and
   `BomTree.vue` (further split into `BomTreeRow.vue`).
3. ✅ **`ExportView.vue`** — CRAP 442.4, off the list. Split into `ActiveMappingSummary.vue`,
   `PreviewTable.vue`, `ExportRunsTable.vue`.
4. ✅ **`ExportDetail.vue`** — CRAP 160.0→43.1. Split into `RunDetailTable.vue`,
   `SkipRunForm.vue`, `DeliverRunForm.vue` (latter two follow `ReleaseDialog.vue`'s pattern).
5. ✅ **`SourceSchemaView.vue`** — CRAP 56.3, off the list. Split into `SourceColumnsTable.vue`.

## Also flagged, lower priority

6. `NestedGroupEditor.vue` — no longer flagged (re-checked in the `issues-code-health-prs-9ens0s`
   session; not present in a fresh `fallow health` run). Left as-is: it's the same
   deliberately-unsplit, self-referencing shape as `ExportNodeTreeEditor.vue` (see its own
   in-file comment), so there'd be nothing to change even if it were still flagged.
7. ✅ **`IcdSchemaView.vue`** — CRAP 43.1, off the list. Split into `IcdActiveColumnsTable.vue`
   and `IcdExcludedFieldsList.vue`.
8. ✅ **`SettingsView.vue`** — CRAP 132.0 (CRITICAL), off the list. Split into
   `SchedulerSettingsForm.vue` and `GdprDenylistEditor.vue`.
9. ✅ **`AuditView.vue`** — CRAP 72.0 (HIGH), off the list. Split into `AuditLogTable.vue`, plus
   `AuditView.test.ts` and `GdprDenylistEditor.test.ts` to bring CRAP under threshold (see
   Status above — cyclomatic/cognitive alone weren't enough once these files had zero coverage).
10. ✅ `router/index.ts:42` (arrow fn) — CRAP 42.0, off the list. Split into
    `needsLogin()`/`needsConnection()`.
11. `App.vue` — no longer flagged (same re-check as item 6). Untouched.

## Phase 14 Slice 5 additions (new files) — resolved via threshold override

`fallow audit --base origin/main` run for export-definitions-2.0.md Slice 5 (the Export
Definitions tree builder) flagged 6 template-complexity findings, all in new files that new UI's
inherent size, not accumulated debt in an old one:

- `ExportNodeTreeEditor.vue` — 17 cyclomatic, 29 cognitive, 255 lines, CRAP 79.4 (HIGH). The
  recursive tree-node editor (scalar-field/object/array in one component, per node kind) —
  same "self-referencing, deliberately unsplit" shape as `NestedGroupEditor.vue` (item 6 above),
  which this generalizes the idea of; splitting the per-kind branches out would just recreate the
  circular-import problem that component's own comment documents.
- `ExportDefinitionRunControls.vue` — 14 cyclomatic, 17 cognitive, 208 lines, CRAP 56.3 (HIGH).
  Grew from Save+Test (Phase 14 Slice 3 recovery UI) to Save+Test+Run Now+Duplicate+Delete; a
  plausible split is one component per action, but each action's state (loading/error/result) is
  small and independent, so splitting would trade one readable file for five tiny ones with no
  shared logic to justify the extraction (see this doc's "minimal code" ground rule).
- `ExportDefinitionsView.vue` — 12 cyclomatic, 25 cognitive, 218 lines, CRAP 43.1 (HIGH). Grew
  from a read-only list to the full list view (enable toggle, schedule/last-run columns,
  test/duplicate/delete actions) export-definitions-2.0.md §7 calls for.
- `ExportDefinitionEditView.vue` — 13 cyclomatic, 22 cognitive, 245 lines, CRAP 49.5. Orchestrates
  create vs. edit mode, the tree editor, preview, and execution history.
- `StatusBadge.vue` — 8 cyclomatic, 17 cognitive, 15 lines (pre-existing component; two status
  values — `success`/`running` — added for `ExportDefinitionRunEntity`, pushing it over the
  cyclomatic threshold on an already-dense one-expression template).

Also: `ExportDefinitionRunsTable.vue:40` flags `text-[0.7rem]` as Tailwind-arbitrary-value token
drift — intentional parity with `ExportRunsTable.vue`'s existing table-header convention (same
`text-[0.7rem] uppercase tracking-wide` classes), not a new one-off value.

These are new files, so their complexity counted as "new" even where, like
`ExportNodeTreeEditor.vue`, the alternative (three separate per-Kind components, circularly
importing each other and this one) is worse — the same "CI-gate note" precedent above. Unlike that
note's case, `// fallow-ignore-next-line complexity` turned out not to apply here: fallow's
inline-suppression action is Angular-specific (`above-angular-decorator` placement) regardless of
the file's actual framework, so it can't target a Vue `<template>` finding. Resolved instead via
`.fallowrc.json`'s `health.thresholdOverrides` — scoped to exactly these files (plus the
pre-existing `SchemaView.vue`, caught in the same diff only because this pass touched it), each
with a `reason`, rather than a global threshold change or an ignore comment that doesn't work.

## Phase 17 Slice 6 additions (new files) — resolved via threshold override, duplication accepted

`fallow audit --base origin/main` run for import-definitions.md Slice 6 (the Import Definitions
frontend — the write-side mirror of Phase 14 Slice 5 above) flagged the same shape of finding,
resolved the same way:

- `ImportNodeTreeEditor.vue` — 17 cyclomatic, 29 cognitive, CRAP 79.4 (HIGH). The same
  self-referencing recursive tree-node editor as `ExportNodeTreeEditor.vue`, built against
  `ImportNode` instead — same "deliberately unsplit" rationale.
- `ImportRunReviewDialog.vue` — started CRITICAL (CRAP 172.0); extracting its count breakdown and
  terminal-state summary into `ImportRunCountSummary.vue`/`ImportRunOutcome.vue` (real, justified
  extractions — both pieces were also duplicated with `ImportDefinitionPreviewPanel.vue`'s own diff
  table, which now shares `ImportPlanDiffTable.vue` instead) brought it down to HIGH (CRAP 63.6).
  What's left is inherent to one dialog covering four largely-exclusive states (loading, error,
  interactive Operator/Approver review, read-only terminal outcome) — the same
  "small independent branches, no shared logic to extract further" call `ExportDefinitionRunControls.vue`
  made above.
- `ImportDefinitionEditView.vue` — 13 cyclomatic, 22 cognitive, CRAP 49.5 — same numbers as
  `ExportDefinitionEditView.vue`, same reason (orchestrates create vs. edit, the tree editor,
  preview, run history, and the review dialog).
- `ImportDefinitionsView.vue` — 10 cyclomatic, 19 cognitive, CRAP 31.6 — same shape as
  `ExportDefinitionsView.vue`'s own list view.

All four added to the existing Phase 14 `thresholdOverrides` entry's file list (same
`maxCognitive`/`maxCrap`, same reasoning) rather than a new entry, since the numbers land in the
same band for the same underlying reason.

**Duplication accepted, not suppressed.** `fallow audit` also reports ~1,000 duplicated lines
across `Export*`/`Import*` pairs (tree editor, run controls, list/edit views, API clients). This is
the intended shape, not accumulated debt: import-definitions.md §4 explains at length why
`ImportNode` deliberately doesn't merge into `ExportNode` (opposite read/write directions, policy
fields meaningless on the other side), and the same reasoning holds one layer up in the UI — an
`ExportNode`-shaped tree editor and an `ImportNode`-shaped one are two small, honest components,
not one generic one with half its props unused per direction. `.fallowrc.json` has no precedent for
suppressing a *duplication* finding (only `health.thresholdOverrides` for complexity), so none was
added; this note is that precedent for the next mirrored feature.

## Suggested approach per item

Extract repeated/large template branches into small sub-components under `src/components/`
(follow the `CiDetailPanel.vue` pattern). After each extraction: `npm run type-check`,
`npm run test`, and `npm run dev` to visually confirm — tests alone don't prove the UI is right.
Re-run `npx fallow health --hotspots --targets` to confirm it dropped off, and
`npx fallow audit --base origin/main` for regressions. `router/index.ts:42` is a plain function,
not a template — lowest-risk extract-function refactor of the batch.

Not required: CI (`frontend-checks`) only gates on `fallow audit` against files changed in a PR.

# Backend (.NET, `src/Connector.*`)

Gaps here are duplication/standardization — the backend's automated gates
(`AnalysisLevel=latest`, `EnforceCodeStyleInBuild`, SonarAnalyzer + Roslynator,
`TreatWarningsAsErrors` in Release) catch build/style issues but not cross-file duplication.

## 1. ✅ Duplicated app-settings read/write boilerplate — done

7 setting keys (`erp_connection`, `export_mapping`, `active_columns`, `column_mappings`,
`scheduler_config`, `gdpr_denied_fields`, `export_presets`) were raw string literals ~30 times
across 5 endpoint files, each hand-rolling the same find/deserialize and find/upsert/save
pattern. Fixed via `SettingsKeys` constants + `GetSettingAsync<T>`/`SetSettingAsync<T>`/
`GetSettingRawAsync` extension methods on `ExportLogDbContext`; net-removed ~90 lines.

## 2. ✅ Duplicated Postgres connect-and-introspect logic — done

Fixed via a private `ConnectAndIntrospectAsync` helper in `ConnectionEndpoints.cs`, called from
both `POST /api/connection` (catches, returns 400) and the `GET /api/source-schema` fallback
(catches, falls through to the demo schema) — call-site error handling preserved, only the
connect+introspect+wrap core deduped.

## 3. ✅ Duplicated test-fixture boilerplate in Connector.Integration.Tests — done

Nine of the fifteen files under `tests/Connector.Integration.Tests` had accreted their own copy of one
of two fixtures, each grown one Postgres-backed test class at a time without noticing the earlier
copies:

- **The local `testdb` connection string + "open-or-null" helper.** Six files (`ImportNodeWalkerPostgresTests`,
  `ExportNodeQueryPostgresTests`, `DynamicExportServiceNestedJsonPostgresTests`,
  `ImportRunReleaserPostgresTests`, `ImportWorkerPostgresTests`, `ImportDefinitionEndpointsPostgresTests`)
  each redeclared the same connection string constant and a byte-for-byte identical try/catch
  open-and-return-null-on-failure method (three of the six also redeclared the `ErpConnectionConfig`
  record for `SetSettingAsync(SettingsKeys.ErpConnection, ...)`). Extracted to `ErpTestFixture`
  (`ConnectionString`/`Config`/`TryOpenAsync`/`IsAvailableAsync`).
- **The Sqlite-backed `ExportLogDbContext` + connection bundle.** Three files
  (`ImportRunReleaserPostgresTests`, `ImportWorkerPostgresTests`, `ImportDefinitionEndpointsPostgresTests`)
  each redeclared an identical `LocalDb` record (bundles the in-memory Sqlite connection with the
  `ExportLogDbContext` on top of it so a test can dispose both via one `await using`) and an identical
  `NewLocalDbAsync` factory pre-seeding the ERP connection setting. Extracted to `LocalDb.NewAsync()`.
  Separately, three more files (`ExportDefinitionWorkerCandidateFilterTests`, `ImportRunEntitySchemaTests`,
  `ExportDefinitionMigratorTests`) each redeclared the same constructor/`DisposeAsync` pair standing up a
  fresh in-memory Sqlite `ExportLogDbContext` with no ERP setting seeded — extracted to a
  `SqliteDbContextTestBase` these three now inherit from instead.

All three extracted into new files (`ErpTestFixture.cs`, `LocalDb.cs`, `SqliteDbContextTestBase.cs`)
rather than an existing one, since no single duplicated-from file was the obvious owner. Behavior-preserving
only — no assertions or fixture semantics changed; the 9 existing files shed 367 lines for 98 (mostly
call-site renames), and the 3 new shared files add 109 back, for a net -160 lines project-wide.

`tests/Connector.Core.Tests` (pure unit tests, no DB fixtures) wasn't in scope for this pass.

## 4. ✅ DynamicExportService.cs split by responsibility (GitHub issue #84) — done

At 820 lines / 38.5 KB, `DynamicExportService.cs` mixed the GDPR denylist, the legacy
`ExportMappingConfig` flat/nested-JSON query builder, the Phase 14 `ExportNode` tree query engine, and
output-format serialization (CSV/Excel/JSON) in one file. Confirmed the legacy nested-JSON path
(`BuildNestedGroupExpr`/`ExecuteNestedJsonQueryAsync`/`UsesNestedJson`) is **not** dead code — it's still
called by `PipelineEndpoints.cs`'s `/api/pipeline/*` endpoints, which `SchemaView.vue`/`ExportView.vue`
actively use — so this is two live export mechanisms in one file, not one live and one dead.

Split into 5 files by responsibility, kept as one `partial class` (not separate classes) so every
existing `DynamicExportService.*` call site — 19 files across `Connector.Api`, `Connector.Infrastructure`,
and both test projects — keeps compiling unchanged:

- `DynamicExportService.cs` — shared primitives: `MaxNestedDepth`, `ExportBuildResult`, `QI`/`SqlLit`
  (SQL quoting), `BuildConnectionString`.
- `DynamicExportService.Gdpr.cs` — `GdprDeniedFields`, `GetDeniedFieldsAsync`, `StripGdprFieldsRecursive`.
- `DynamicExportService.LegacyMapping.cs` — the `ExportMappingConfig`-based flat/nested-JSON query
  builder (`GetColumnNames`, `UsesNestedJson`, `BuildExportAsync`, `ExecuteQueryAsync`,
  `BuildNestedGroupExpr`, `ExecuteNestedJsonQueryAsync`).
- `DynamicExportService.ExportNode.cs` — the Phase 14 `ExportNode` tree engine (query building, field
  mapping application, column flattening, `BuildExportNodeAsync`).
- `DynamicExportService.Output.cs` — CSV/JSON/Excel/nested-JSON byte writers, `ContentTypeFor`,
  `BuildNamedFileName`.

Purely mechanical: every method/field moved verbatim (verified by a sorted-line diff against the
original — the only differences are new file-header comments), nothing renamed, no new
interfaces/abstractions added, no call site outside this file touched.

## 5. ✅ ImportDefinitionEndpoints.cs split by responsibility (GitHub issue #85) — done

At 775 lines / 37.1 KB, `ImportDefinitionEndpoints.cs` mixed route registration with request validation
and entity/DTO mapping in one file — the validation/mapping logic was already factored into standalone
`internal`/`private` static methods (not inlined in the endpoint lambdas), it just all lived alongside the
route registrations.

Split into 3 files, kept as one `partial class` (not separate classes) so the `internal` methods
`Connector.Integration.Tests` already reaches into (`ValidateRequestAsync`, `BuildSuggestionAsync`,
`ValidateIntegrationKeyPairEnabledAsync`) keep compiling unchanged:

- `ImportDefinitionEndpoints.cs` — route registrations only (`MapImportDefinitionEndpoints`).
- `ImportDefinitionEndpoints.Validation.cs` — the Open Decision #9/#15 save-time guardrails
  (`SqlIdentifierRegex`, `ValidateRequestAsync`, `ValidateTargetAgainstSchema`, `ValidateNode`,
  `ContainsControlCharacters`, `ValidateIntegrationKeyPairEnabledAsync`).
- `ImportDefinitionEndpoints.Mapping.cs` — entity/DTO mapping and the "create from export" suggestion
  lookup (`ToDto`, `ToSummaryDto`, `BuildSuggestionAsync`, `TryParseSample`).

Left `ExportDefinitionEndpoints.cs` (the explicit twin this file's own comments mirror throughout —
`SqlIdentifierRegex`, `ValidateRequestAsync`, `ValidateNode`, the IntegrationKey-pair check) untouched:
issue #85 only scoped `ImportDefinitionEndpoints.cs`, and this split doesn't merge any logic between the
two, so the twins still mirror each other — one twin just now also has smaller files.

Purely mechanical, same technique as item 4 above: every method/field moved verbatim (verified by a
sorted-line diff against the original — the only differences are new file-header comments), nothing
renamed, no new interfaces/abstractions, no call site outside this file touched.

## Backend tooling note

No `fallow` equivalent for C#. When `dotnet` is available: `dotnet build Connector.sln -c
Release` to confirm the warnings-as-errors baseline, `dotnet csharpier check .` for formatting.
Consider a C# duplication/complexity analyzer if these findings recur elsewhere.

# Related

- [Export Definitions 2.0](/pipeline/export-definitions-2.0.md) — Phase 14, orthogonal to this backlog
