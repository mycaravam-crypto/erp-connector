# Code health TODO

Goal for everything on this list: **minimal** (no duplicated or dead
code), **standardized** (one way to do a recurring thing, not N slightly
different ways), **functional** (behavior-preserving — nothing here
should change what the app does, only how the code is organized).

## Status (session on `claude/codebase-minimize-optimize-ywu53y`)

Done: both backend items, and all 5 top-priority + 2 lower-priority
frontend template-complexity items (SchemaView, ErpDatabaseView,
ExportView, ExportDetail, SourceSchemaView, the SchemaView internal
duplicate, and router/index.ts:42). Not done: the remaining lower-priority
frontend items (NestedGroupEditor.vue, IcdSchemaView.vue, SettingsView.vue,
AuditView.vue, App.vue) — untouched, same numbers as before.

**CI-gate caveat:** `fallow audit --base origin/main` (what CI's
`frontend-checks` job runs) currently returns `verdict: fail` on this
branch — 9 complexity findings, all in files newly created by these
extractions. Splitting each CRITICAL monolith into several small,
single-purpose, well-tested components is a large net improvement (e.g.
SchemaView.vue's template: CRAP 733.4 → 88.0, no longer CRITICAL), but
because every extracted file is *new* relative to `origin/main`, 100% of
its (much smaller) residual complexity counts as "introduced" by the
changeset rather than inherited debt — audit's gate doesn't credit "this
replaced something far worse." All 9 remaining findings are driven by
cognitive complexity slightly over the default threshold (15) — e.g.
`ActiveMappingSummary.vue` at 37, `RelationCard.vue` at 16 — the same
threshold the pre-existing, already-merged `NestedGroupEditor.vue` (16)
also exceeds. Feeding real coverage (`vitest run --coverage`) into
`fallow audit --coverage <path>` fixes the CRAP-only findings (confirmed:
dropped 11 → 9) but doesn't touch cyclomatic/cognitive-threshold findings,
which are coverage-independent. Getting all 9 under the default threshold
would need materially more fragmentation of already-small components —
judged not worth the added indirection for what's left. Left for a human
call: raise `.fallowrc.json`'s cognitive/CRAP thresholds slightly (the
defaults are aggressive for real-world Vue templates — see the
NestedGroupEditor.vue precedent), add inline `// fallow-ignore-next-line
complexity` suppressions on these 9, or accept a red check on this PR.

Started as a `fallow` (JS/TS analyzer) follow-up; extended to a
whole-repo pass covering the .NET backend too. Frontend findings come
from a tool (`fallow`) and are exact; backend findings come from manual
review (see "Backend tooling" note below) — re-verify file/line numbers
before acting, since they drift as files change.

Nothing here is required — CI only gates on `fallow audit` against files
changed in a PR (frontend) and the existing analyzer/build gate
(backend). This file exists so the work isn't lost between sessions.

---

## Ground rules for this work (apply to every item below)

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

---

## Frontend (`src/connector-ui`) — template complexity

`fallow`'s dead-code and duplication findings are already fixed (see git
history on `claude/fallow-js-code-checking-l5imc9`). What's left is
`fallow health`'s **template complexity** findings — Vue `<template>`
blocks (or one `.ts` arrow function) exceeding cyclomatic/cognitive/CRAP
thresholds. Not done yet because verifying a UI refactor needs a running
browser, which wasn't available in the sessions that did the analysis.

Re-run `npx fallow health --hotspots --targets` (from `src/connector-ui/`)
any time to get fresh numbers.

### Priority order (from `fallow health --hotspots --targets`)

Ranked by refactor ROI (score = quick-win ROI, pri = absolute priority):

1. ✅ **`src/views/SchemaView.vue`** — was 56 cyclomatic, 141 cognitive, 877 lines, CRAP 733.4 (CRITICAL, effort: high)
   Biggest offender by far. Split into 8 sub-components (`PresetsToolbar`,
   `RelationCard`/`RelationsSection`, `ColumnMappingTable`,
   `NestedGroupsSection`, `JsonEnvelopeEditor`, `SuggestedRelations`,
   `ExportFormatPicker`). Now 18 cyclomatic, 29 cognitive, 458 lines,
   CRAP 88.0 (high, no longer critical). Its internal 16-line duplicate
   block (`buildMappingConfig()`) was also fixed.
2. ✅ **`src/views/ErpDatabaseView.vue`** — was 51 cyclomatic, 104 cognitive, 312 lines, CRAP 612.8 (CRITICAL, effort: medium)
   Split into `FlatRecordsTable.vue` and `BomTree.vue` (which itself
   further split into `BomTreeRow.vue` to dedupe root/child row markup).
   Now 15 cyclomatic, 17 cognitive, CRAP 63.6 (high).
3. ✅ **`src/views/ExportView.vue`** — was 43 cyclomatic, 78 cognitive, 324 lines, CRAP 442.4 (CRITICAL, effort: medium)
   Split into `ActiveMappingSummary.vue`, `PreviewTable.vue`,
   `ExportRunsTable.vue`. ExportView.vue itself dropped off the
   refactoring-targets list entirely.
4. ✅ **`src/views/ExportDetail.vue`** — was 25 cyclomatic, 48 cognitive, 254 lines, CRAP 160.0 (CRITICAL, effort: medium)
   Split into `RunDetailTable.vue`, `SkipRunForm.vue`, `DeliverRunForm.vue`
   (the latter two follow `ReleaseDialog.vue`'s existing seqNo-prop/
   single-emit pattern). Now 12 cyclomatic, 18 cognitive, CRAP 43.1 (moderate).
5. ✅ **`src/views/SourceSchemaView.vue`** — was 14 cyclomatic, 42 cognitive, 140 lines, CRAP 56.3 (CRITICAL, effort: medium)
   Split into `SourceColumnsTable.vue`. Dropped off the refactoring-targets
   list entirely.

### Also flagged, lower priority (not in top-5 ROI ranking) — not done

6. `src/components/NestedGroupEditor.vue` — 13 cyclomatic, 16 cognitive, 207 lines, CRAP 49.5
7. `src/views/IcdSchemaView.vue` — 12 cyclomatic, 19 cognitive, 139 lines, CRAP 43.1
8. `src/views/SettingsView.vue` — 11 cyclomatic, 19 cognitive, 240 lines, CRAP 132.0 (CRITICAL)
9. `src/views/AuditView.vue` — 8 cyclomatic, 9 cognitive, 81 lines, CRAP 72.0 (HIGH)
10. ✅ `src/router/index.ts:42` (arrow fn, not a template) — was 6 cyclomatic, 6 cognitive, 8 lines, CRAP 42.0.
    Split into `needsLogin()`/`needsConnection()` named predicates; dropped
    off the "above threshold" list.
11. `src/App.vue` — 5 cyclomatic, 7 cognitive, 85 lines, CRAP 30.0

Items 6-9 and 11 are untouched — same numbers as before this session,
still lower priority per the original ROI ranking.

### Suggested approach per item

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

### Not required (frontend)

CI (`frontend-checks` job) only gates on `fallow audit` against files
changed in a PR.

---

## Backend (.NET, `src/Connector.*`)

The `dotnet` SDK isn't available in this sandbox, so this pass is a
manual read-through, not a tool-verified scan like the frontend one —
treat these as leads to confirm, not settled findings. The backend
already has strict automated quality gates that the frontend lacked
before `fallow` (`Directory.Build.props`: `AnalysisLevel=latest`,
`EnforceCodeStyleInBuild=true`, `SonarAnalyzer.CSharp` +
`Roslynator.Analyzers`, `TreatWarningsAsErrors` in Release), so build/style
issues are unlikely — the gaps found below are duplication and
standardization, which those analyzers don't fully cover.

### 1. ✅ Duplicated app-settings read/write boilerplate (highest value, low risk) — done

Fixed via `SettingsKeys` constants + `GetSettingAsync<T>`/`SetSettingAsync<T>`/
`GetSettingRawAsync` extension methods on `ExportLogDbContext`
(`src/Connector.Infrastructure/ExportLogDbContext.cs`), migrated across
all 5 files below. Net-removed ~90 lines. Not verified with `dotnet build`
(SDK unavailable in this session, same as when this list was written) —
re-verify when a session has the SDK.

Every persisted app setting is stored as a JSON blob keyed by string in
`AppSettings` (via `ExportLogDbContext`), and every endpoint that reads or
writes one hand-rolls the same pattern instead of sharing a helper:

**Read pattern** (`db.AppSettings.FindAsync(key)` → null-check →
`JsonSerializer.Deserialize<T>`), repeated in:
- `Endpoints/SchemaEndpoints.cs:16` (`active_columns`), `:21` (`column_mappings`)
- `Endpoints/SettingsEndpoints.cs:18` (`scheduler_config`)
- `Endpoints/ConnectionEndpoints.cs:18` (`erp_connection`), `:78` (`erp_connection`, fallback path)
- `Endpoints/PipelineEndpoints.cs:46` (`export_mapping`), `:58` (`erp_connection`), `:209` (`export_mapping`), `:228` (`erp_connection`)
- `Endpoints/ExportMappingEndpoints.cs:17` (`export_mapping`)

**Write pattern** (`JsonSerializer.Serialize` → `FindAsync` → null-check →
`Add` new entity or mutate `.Value` → `SaveChangesAsync`), repeated in:
- `Endpoints/SchemaEndpoints.cs:99-103` (`active_columns`), `:123-127` (`column_mappings`)
- `Endpoints/SettingsEndpoints.cs:55-59` (`scheduler_config`), `:101-105` (`gdpr_denied_fields`)
- `Endpoints/ExportMappingEndpoints.cs:40-44` (`export_mapping`), `:96-100` (`export_presets`), `:124` (`export_presets` update)
- `Endpoints/ConnectionEndpoints.cs:52-58` (`erp_connection`)

That's 7 identical-shaped setting keys (`erp_connection`, `export_mapping`,
`active_columns`, `column_mappings`, `scheduler_config`,
`gdpr_denied_fields`, `export_presets`) referenced as raw string literals
30 times across 5 files, with zero shared constant — a typo in any one of
them is a silent runtime bug, not a compile error.

**Suggested fix:**
- Add a small `SettingsKeys` static class (or `const string` per key
  co-located with the DTO it stores) so the key is typed once.
- Add `Task<T?> GetSettingAsync<T>(string key)` and
  `Task SetSettingAsync<T>(string key, T value)` extension methods on
  `ExportLogDbContext` (or a thin `AppSettingsStore` service) that wrap the
  find/deserialize and find/upsert/save patterns above.
- Migrate the ~10 call sites to use them. Should net-remove on the order
  of 60-80 lines of duplicated logic and remove the class of bug where one
  call site's upsert diverges slightly from another's (worth checking for
  during migration — e.g. confirm every write site actually calls
  `SaveChangesAsync` exactly once).
- Existing tests in `tests/Connector.Core.Tests` and
  `tests/Connector.Integration.Tests` should catch behavior changes; run
  the full suite after.

### 2. ✅ Duplicated Postgres connect-and-introspect logic — done

Fixed via a private `ConnectAndIntrospectAsync` helper in
`ConnectionEndpoints.cs`, called from both `POST /api/connection`
(catches and returns 400) and the `GET /api/source-schema` fallback
(catches and falls through to the demo schema) — call-site error handling
preserved exactly, only the connect+introspect+wrap core was deduped.

### Backend tooling note

This repo has no equivalent of `fallow` for C# (dead-code/duplication
detection). The existing analyzer stack (SonarAnalyzer + Roslynator + CA/IDE
rules) catches style and many correctness issues at build time, but not
cross-file duplication like the finding above. When a session has the
`dotnet` SDK available:
- Run `dotnet build Connector.sln -c Release` to confirm the current
  warnings-as-errors baseline is clean (it should be, per CI).
- Run `dotnet csharpier check .` to confirm formatting.
- Consider whether the duplication findings above are common enough
  elsewhere in the solution to justify adding a C# duplication/complexity
  analyzer (e.g. enabling more Roslynator/Sonar duplication rules as
  errors, if they aren't already) rather than relying on manual review
  going forward.

### Not covered by this pass

`tests/Connector.Core.Tests` and `tests/Connector.Integration.Tests`
(only 4 integration test files, ~1.7k lines total) weren't reviewed for
duplicated setup/fixture boilerplate — lower priority than production
code for the "minimal/standardized" goal, but worth a look if this list
gets revisited.
