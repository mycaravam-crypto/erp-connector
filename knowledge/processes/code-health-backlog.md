---
type: Engineering Backlog
title: Code Health Backlog
description: Open frontend template-complexity items and ground rules for tackling them — not CI-gated, not required.
tags: [process, code-health, backlog, frontend, technical-debt]
timestamp: 2026-08-19T00:00:00Z
---

Goal for everything on this list: **minimal** (no duplicated or dead
code), **standardized** (one way to do a recurring thing, not N slightly
different ways), **functional** (behavior-preserving — nothing here
should change what the app does, only how the code is organized).

Nothing here is required — CI only gates on `fallow audit` against files
changed in a PR (frontend) and the existing analyzer/build gate (backend).
This page exists so the remaining work isn't lost between sessions.

# Status

Done (session `claude/codebase-minimize-optimize-ywu53y`): both backend
items below, and 5 of 7 frontend template-complexity items (SchemaView,
ErpDatabaseView, ExportView, ExportDetail, SourceSchemaView, plus
`router/index.ts:42`) — see the "✅" rows below for what each split into.
Not done: `NestedGroupEditor.vue`, `IcdSchemaView.vue`, `SettingsView.vue`,
`AuditView.vue`, `App.vue` — lower priority, untouched.

**CI-gate note:** `fallow audit --base origin/main` flags 9 residual
cognitive-complexity findings (e.g. `ActiveMappingSummary.vue` at 37) in
files the completed extractions *created* — smaller in absolute terms than
what they replaced, but counted as newly-introduced since the files are
new relative to `origin/main`. Not coverage-fixable (cyclomatic/cognitive
thresholds are coverage-independent). Needs a human call: raise
`.fallowrc.json`'s thresholds, add `// fallow-ignore-next-line complexity`
suppressions, or accept the red check.

# Ground rules for this work (apply to every item below)

**YAGNI.** Fix the duplication/complexity that's actually documented
below — don't generalize past the concrete call sites found. E.g. the
settings-store helper only needs to cover the 7 keys and 2 shapes
(read/write) actually in use; no plugin system, no support for storage
backends nobody asked for, no config knobs for hypothetical future
callers. Same for frontend extractions: a new sub-component's props are
whatever the call sites at hand actually pass, not a speculative "flexible"
API.

**Minimal code.** Prefer deleting over adding. The smallest diff that
removes the duplication/complexity wins over a more "complete" or
"future-proof" version. If a refactor needs a new abstraction, it should
be justified by the duplication it removes, not by tidiness alone — and
it should net-negative the line count, not net-positive.

**Consistent comments.** One convention per language, applied uniformly:
- TS/Vue: `//` for inline notes; reserve `/** */` for exported
  API surfaces where the shape isn't self-evident from types alone.
- C#: `///` XML doc comments on public members (already the codebase's
  convention — keep it); `//` for inline notes.
- A comment earns its place only by explaining a non-obvious **why**
  (a constraint, an invariant, a workaround, something that would
  surprise a reader) — never a **what** the code already says through
  naming, and never task/PR/rework narration ("added for the X cleanup",
  "changed after refactor", "was Y before"). If removing a comment
  wouldn't confuse a future reader, remove it.

**Clean up leftover-rework comments as you touch each file.** A
repo-wide grep during this pass found no commented-out dead code and no
task-referencing comments in first-party code (only in `node_modules`,
which isn't ours to touch) — so there's no separate backlog item for
this. But it's a live check, not a one-time pass: when a refactor below
moves or rewrites code, re-read every comment in the touched region and
either fix it to describe the new shape or delete it if it no longer
earns its place per the rule above. Don't let a comment describe code
that used to be there.

# Frontend (`src/connector-ui`) — template complexity

`fallow`'s dead-code and duplication findings are already fixed (see git
history on `claude/fallow-js-code-checking-l5imc9`). What's left is
`fallow health`'s **template complexity** findings — Vue `<template>`
blocks (or one `.ts` arrow function) exceeding cyclomatic/cognitive/CRAP
thresholds. Not done yet because verifying a UI refactor needs a running
browser, which wasn't available in the sessions that did the analysis.

Re-run `npx fallow health --hotspots --targets` (from `src/connector-ui/`)
any time to get fresh numbers.

## Priority order (from `fallow health --hotspots --targets`)

Ranked by refactor ROI (score = quick-win ROI, pri = absolute priority):

1. ✅ **`src/views/SchemaView.vue`** — CRAP 733.4→88.0 (CRITICAL→high). Split into 8 sub-components (`PresetsToolbar`, `RelationCard`/`RelationsSection`, `ColumnMappingTable`, `NestedGroupsSection`, `JsonEnvelopeEditor`, `SuggestedRelations`, `ExportFormatPicker`); internal duplicate `buildMappingConfig()` also fixed.
2. ✅ **`src/views/ErpDatabaseView.vue`** — CRAP 612.8→63.6 (CRITICAL→high). Split into `FlatRecordsTable.vue` and `BomTree.vue` (further split into `BomTreeRow.vue`).
3. ✅ **`src/views/ExportView.vue`** — CRAP 442.4, off the targets list entirely. Split into `ActiveMappingSummary.vue`, `PreviewTable.vue`, `ExportRunsTable.vue`.
4. ✅ **`src/views/ExportDetail.vue`** — CRAP 160.0→43.1 (CRITICAL→moderate). Split into `RunDetailTable.vue`, `SkipRunForm.vue`, `DeliverRunForm.vue` (latter two follow `ReleaseDialog.vue`'s seqNo-prop/single-emit pattern).
5. ✅ **`src/views/SourceSchemaView.vue`** — CRAP 56.3, off the targets list entirely. Split into `SourceColumnsTable.vue`.

## Also flagged, lower priority (not in top-5 ROI ranking) — not done

6. `src/components/NestedGroupEditor.vue` — 13 cyclomatic, 16 cognitive, 207 lines, CRAP 49.5
7. `src/views/IcdSchemaView.vue` — 12 cyclomatic, 19 cognitive, 139 lines, CRAP 43.1
8. `src/views/SettingsView.vue` — 11 cyclomatic, 19 cognitive, 240 lines, CRAP 132.0 (CRITICAL)
9. `src/views/AuditView.vue` — 8 cyclomatic, 9 cognitive, 81 lines, CRAP 72.0 (HIGH)
10. ✅ `src/router/index.ts:42` (arrow fn, not a template) — CRAP 42.0, off the list. Split into `needsLogin()`/`needsConnection()` named predicates.
11. `src/App.vue` — 5 cyclomatic, 7 cognitive, 85 lines, CRAP 30.0

Items 6-9 and 11 are untouched — same numbers as before this session,
still lower priority per the original ROI ranking.

## Suggested approach per item

- Extract repeated/large template branches (e.g. table row rendering, mode
  switches) into small sub-components under `src/components/`, following
  the `CiDetailPanel.vue` pattern from the dedup pass.
- After each extraction: `npm run type-check`, `npm run test`, and start
  `npm run dev` to visually confirm the view still renders/behaves
  correctly (these are UI changes — tests alone don't prove the UI is
  right).
- Re-run `npx fallow health --hotspots --targets` to confirm the item
  dropped off the list, and `npx fallow audit --base origin/main` to make
  sure nothing new was introduced.
- `src/router/index.ts:42` is a plain function, not a template — likely a
  straightforward extract-function refactor, lowest risk of the batch.

## Not required (frontend)

CI (`frontend-checks` job) only gates on `fallow audit` against files
changed in a PR.

# Backend (.NET, `src/Connector.*`)

The gaps found below are duplication and standardization — the backend's
automated quality gates (`Directory.Build.props`: `AnalysisLevel=latest`,
`EnforceCodeStyleInBuild=true`, `SonarAnalyzer.CSharp` +
`Roslynator.Analyzers`, `TreatWarningsAsErrors` in Release) catch build/style
issues but not cross-file duplication.

## 1. ✅ Duplicated app-settings read/write boilerplate — done

7 setting keys (`erp_connection`, `export_mapping`, `active_columns`,
`column_mappings`, `scheduler_config`, `gdpr_denied_fields`,
`export_presets`) were referenced as raw string literals ~30 times across
5 endpoint files, each hand-rolling the same find/deserialize and
find/upsert/save pattern against `AppSettings`. Fixed via `SettingsKeys`
constants + `GetSettingAsync<T>`/`SetSettingAsync<T>`/`GetSettingRawAsync`
extension methods on `ExportLogDbContext`
(`src/Connector.Infrastructure/ExportLogDbContext.cs`); net-removed ~90
lines.

## 2. ✅ Duplicated Postgres connect-and-introspect logic — done

Fixed via a private `ConnectAndIntrospectAsync` helper in
`ConnectionEndpoints.cs`, called from both `POST /api/connection`
(catches and returns 400) and the `GET /api/source-schema` fallback
(catches and falls through to the demo schema) — call-site error handling
preserved exactly, only the connect+introspect+wrap core was deduped.

## Backend tooling note

This repo has no equivalent of `fallow` for C# (dead-code/duplication
detection). When a session has the `dotnet` SDK available:
- Run `dotnet build Connector.sln -c Release` to confirm the current
  warnings-as-errors baseline is clean (it should be, per CI).
- Run `dotnet csharpier check .` to confirm formatting.
- Consider whether the duplication findings above are common enough
  elsewhere in the solution to justify adding a C# duplication/complexity
  analyzer (e.g. enabling more Roslynator/Sonar duplication rules as
  errors, if they aren't already) rather than relying on manual review
  going forward.

## Not covered by this pass

`tests/Connector.Core.Tests` and `tests/Connector.Integration.Tests`
(only 4 integration test files, ~1.7k lines total) weren't reviewed for
duplicated setup/fixture boilerplate — lower priority than production
code for the "minimal/standardized" goal, but worth a look if this list
gets revisited.

# Related

- [Export Definitions 2.0](/pipeline/export-definitions-2.0.md) — Phase 14, the orthogonal in-progress initiative this backlog is explicitly not part of
