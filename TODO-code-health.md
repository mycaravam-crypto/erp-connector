# Code health TODO

Goal for everything on this list: **minimal** (no duplicated or dead
code), **standardized** (one way to do a recurring thing, not N slightly
different ways), **functional** (behavior-preserving — nothing here
should change what the app does, only how the code is organized).

Started as a `fallow` (JS/TS analyzer) follow-up; extended to a
whole-repo pass covering the .NET backend too. Frontend findings come
from a tool (`fallow`) and are exact; backend findings come from manual
review (see "Backend tooling" note below) — re-verify file/line numbers
before acting, since they drift as files change.

Nothing here is required — CI only gates on `fallow audit` against files
changed in a PR (frontend) and the existing analyzer/build gate
(backend). This file exists so the work isn't lost between sessions.

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

1. **`src/views/SchemaView.vue`** — 56 cyclomatic, 141 cognitive, 877 lines, CRAP 733.4 (CRITICAL, effort: high)
   Biggest offender by far. `<template>` at line 476, ~877 lines.
   Also contains its own internal 16-line duplicate block
   (`confirmSavePreset`-style config-building logic repeated at
   lines ~87-102 and ~397-412 — found via `fallow dupes --min-occurrences 2`,
   below the default `.fallowrc.json` threshold of 3). Worth extracting a
   shared `buildMappingConfig()` helper as part of this refactor.
2. **`src/views/ErpDatabaseView.vue`** — 51 cyclomatic, 104 cognitive, 312 lines, CRAP 612.8 (CRITICAL, effort: medium)
   Already reduced once (detail-panel markup deduped into `CiDetailPanel.vue`,
   was 120 cognitive/367 lines). Remaining complexity is mostly the flat-list
   vs. BOM-tree mode branching — candidates: extract a `<FlatRecordsTable>`
   and a `<BomTree>` sub-component.
3. **`src/views/ExportView.vue`** — 43 cyclomatic, 78 cognitive, 324 lines, CRAP 442.4 (CRITICAL, effort: medium)
4. **`src/views/ExportDetail.vue`** — 25 cyclomatic, 48 cognitive, 254 lines, CRAP 160.0 (CRITICAL, effort: medium)
5. **`src/views/SourceSchemaView.vue`** — 14 cyclomatic, 42 cognitive, 140 lines, CRAP 56.3 (CRITICAL, effort: medium)

### Also flagged, lower priority (not in top-5 ROI ranking)

6. `src/components/NestedGroupEditor.vue` — 13 cyclomatic, 16 cognitive, 207 lines, CRAP 49.5
7. `src/views/IcdSchemaView.vue` — 12 cyclomatic, 19 cognitive, 139 lines, CRAP 43.1
8. `src/views/SettingsView.vue` — 11 cyclomatic, 19 cognitive, 240 lines, CRAP 132.0 (CRITICAL)
9. `src/views/AuditView.vue` — 8 cyclomatic, 9 cognitive, 81 lines, CRAP 72.0 (HIGH)
10. `src/router/index.ts:42` (arrow fn, not a template) — 6 cyclomatic, 6 cognitive, 8 lines, CRAP 42.0
11. `src/App.vue` — 5 cyclomatic, 7 cognitive, 85 lines, CRAP 30.0

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

### 1. Duplicated app-settings read/write boilerplate (highest value, low risk)

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

### 2. Duplicated Postgres connect-and-introspect logic

`Endpoints/ConnectionEndpoints.cs` has two near-identical blocks that open
an `NpgsqlConnection`, call `IntrospectSchemaAsync`, and wrap the result in
a `SourceSchemaDto`:
- `POST /api/connection` handler, lines ~43-63 (surfaces failures to the
  client as `400 Bad Request`)
- `GET /api/source-schema` fallback path, lines ~84-97 (silently swallows
  failures and falls through to the demo schema)

The error handling genuinely differs between the two call sites, so this
isn't a pure copy-paste — but the connect+introspect+wrap core (~10 lines)
is identical and could be a private helper returning either the DTO or a
thrown/caught exception, called with different catch behavior at each site.

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
