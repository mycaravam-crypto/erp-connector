# Fallow follow-up: template complexity cleanup

Context: `fallow` (JS/TS code health tool, see `.fallowrc.json` and the
`frontend-checks` CI job in `.github/workflows/ci.yml`) was added to this
project and its dead-code / duplication findings have been fixed (see git
history on `claude/fallow-js-code-checking-l5imc9`).

What's left is `fallow health`'s **template complexity** findings — 11
Vue `<template>` blocks (or one `.ts` arrow function) that exceed
cyclomatic/cognitive/CRAP thresholds. These are real but larger refactors
(extracting sub-components/composables), not done yet because they need a
running browser to visually verify no regressions, which wasn't available
in the session that did the dead-code/duplication pass.

Re-run `npx fallow health` (from `src/connector-ui/`) any time to get fresh
numbers — line numbers below will drift as files change.

## Priority order (from `fallow health --hotspots --targets`)

Ranked by refactor ROI (score = quick-win ROI, pri = absolute priority):

1. **`src/views/SchemaView.vue`** — 56 cyclomatic, 141 cognitive, 877 lines, CRAP 733.4 (CRITICAL, effort: high)
   Biggest offender by far. `<template>` at line 476, ~877 lines.
2. **`src/views/ErpDatabaseView.vue`** — 51 cyclomatic, 104 cognitive, 312 lines, CRAP 612.8 (CRITICAL, effort: medium)
   Already reduced once (detail-panel markup deduped into `CiDetailPanel.vue`,
   was 120 cognitive/367 lines). Remaining complexity is mostly the flat-list
   vs. BOM-tree mode branching — candidates: extract a `<FlatRecordsTable>`
   and a `<BomTree>` sub-component.
3. **`src/views/ExportView.vue`** — 43 cyclomatic, 78 cognitive, 324 lines, CRAP 442.4 (CRITICAL, effort: medium)
4. **`src/views/ExportDetail.vue`** — 25 cyclomatic, 48 cognitive, 254 lines, CRAP 160.0 (CRITICAL, effort: medium)
5. **`src/views/SourceSchemaView.vue`** — 14 cyclomatic, 42 cognitive, 140 lines, CRAP 56.3 (CRITICAL, effort: medium)

## Also flagged, lower priority (not in top-5 ROI ranking)

6. `src/components/NestedGroupEditor.vue` — 13 cyclomatic, 16 cognitive, 207 lines, CRAP 49.5
7. `src/views/IcdSchemaView.vue` — 12 cyclomatic, 19 cognitive, 139 lines, CRAP 43.1
8. `src/views/SettingsView.vue` — 11 cyclomatic, 19 cognitive, 240 lines, CRAP 132.0 (CRITICAL)
9. `src/views/AuditView.vue` — 8 cyclomatic, 9 cognitive, 81 lines, CRAP 72.0 (HIGH)
10. `src/router/index.ts:42` (arrow fn, not a template) — 6 cyclomatic, 6 cognitive, 8 lines, CRAP 42.0
11. `src/App.vue` — 5 cyclomatic, 7 cognitive, 85 lines, CRAP 30.0

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

## Not required

CI (`frontend-checks` job) only gates on `fallow audit` against files
changed in a PR, so none of this blocks merges — it's tracked here purely
so it doesn't get lost.
