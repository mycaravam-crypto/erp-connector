---
type: Background Service
title: ExportDefinitionWorker (Scheduler)
description: Polls enabled ExportDefinition rows once a minute and runs any whose cron Schedule is due — a sibling of ExportWorker, not a replacement.
resource: src/Connector.Infrastructure/ExportDefinitionWorker.cs
tags: [pipeline, orchestration, scheduling, background-service, dynamic-export, phase-14]
timestamp: 2026-09-03T00:00:00Z
---

`ExportDefinitionWorker` is Phase 14 Slice 4: the automatic-execution half of
[ExportDefinition](/api/export-definition-api.md), which until this shipped could only be
triggered manually via `POST /api/export-definitions/{id}/run`. It is a **sibling** of
[ExportWorker](/pipeline/export-worker.md), not a replacement — the legacy CI-to-vendor
scheduled pipeline (four-eyes/staging) keeps running exactly as before, on its own
`ExportWorker.ScheduledTimeUtc` config, completely untouched by this worker.

# Polling

The worker wakes once a minute (aligned to the top of the minute, like a real cron daemon) and,
each tick:

1. Queries `ExportDefinition` rows where `IsEnabled = true` and `Schedule` is not null
   (`ExportDefinitionWorker.ScheduledCandidates` — a SQL-translatable prefilter).
2. For each candidate, evaluates its `Schedule` cron string against the current minute
   (`CronSchedule.IsDue`) — a disabled definition or one with `Schedule = null` is filtered out
   before this step even runs, so it can never fire automatically.
3. Runs every definition whose cron matches, via `ExportDefinitionRunner.ExecuteAsync` — the same
   execution path `POST .../run` and `POST .../test` already use, with
   `TriggeredBy = "scheduler"`.

A definition whose own run throws (or takes a long time) doesn't stop the rest of the tick — each
run is wrapped independently.

# CronSchedule — the cron matcher

`CronSchedule.IsDue(schedule, utcNow)` is a small, purpose-built 5-field cron matcher (minute
hour day-of-month month day-of-week) — not a general-purpose cron library. It supports `*`,
single values, comma lists, ranges (`a-b`), and steps (`*/n` or `a-b/n`) per field, and applies
standard cron semantics for day-of-month/day-of-week: when *both* are restricted (neither is
`*`), a match on *either* is enough.

Per [Export Definitions 2.0 §11 decision #1](/pipeline/export-definitions-2.0.md#11-open-decisions),
cron granularity is hourly-or-coarser by convention (the UI's Manual/Hourly/Daily/Weekly presets
only ever produce such expressions), matching this project's existing scheduling convention — but
the matcher itself doesn't special-case or reject a finer-grained expression typed into the
advanced free-text field; it just evaluates whatever 5 fields it's given every minute.

A NuGet cron library was deliberately not added — the matching logic is a few dozen lines, and
pulling in a dependency for it would violate [the project's "minimal code"
directive](/pipeline/export-definitions-2.0.md#0-engineering-directive-non-negotiable).

# Scheduled vs. manual runs

A scheduled run and a manual "Run Now" are indistinguishable in
[run history](run-history.md) except for `TriggeredBy` — both go through the same
`ExportDefinitionRunner.ExecuteAsync`, write exactly one `ExportDefinitionRunEntity` row, and
apply the same zero-record-is-Failed rule. There is no separate "scheduled run" code path to
drift out of sync with manual triggering.

# Registration

Registered alongside `ExportWorker` in `Program.cs`
(`builder.Services.AddHostedService<ExportDefinitionWorker>()`) — no new configuration section,
since the worker takes no options (it always polls every minute; every other knob lives on the
`ExportDefinition` row itself, not app config).

# Related

- [ExportWorker](/pipeline/export-worker.md) — the legacy sibling this worker is modeled on and
  runs alongside, untouched
- [Run History](run-history.md) — what a scheduled run writes
- [ExportNode Tree](export-node.md) — what actually gets queried once a run fires
- [Export Definition API](/api/export-definition-api.md) — `PATCH .../enable` and the `Schedule`
  field this worker reads
