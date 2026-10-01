---
type: Business Process
title: Connector Alignment Trial
description: Checklist for a first end-to-end run of two connector instances aligning two air-gapped systems (A → B).
tags: [process, import, export, connector-to-connector, verification]
timestamp: 2026-10-01T00:00:00Z
---

A manual dry run before relying on [connector-to-connector alignment](/pipeline/import-definitions.md#9-connector-to-connector-alignment)
in production. `ConnectorAlignmentPostgresTests` runs the same flow automatically against `testdb`; this
checklist covers what only two real installations can show (separate hosts, real file carry, real target
schema).

# Setup

1. **Instance IDs.** Note each instance's ID (Settings, once #224 lands; until then the `instance_id` row in
   `AppSetting`). The two must differ — an installation cloned from the other's database shares its ID.
2. **Instance B — import definition.** Root table and match column on B's system; map every column A sends.
   Writable columns include the soft-delete flag if deletes should carry over. Set an integration key and
   contract version; choose *If unmatched: Insert* if B should receive new records. Every `NOT NULL` column
   without a default must be mapped, or the release fails.
3. **Instance A — export definition.** JSON output; the same integration key and contract version;
   *Target import job* = the name of B's import definition; a schedule.

# Run

4. Let A's schedule fire (or wait for it). Check the run history: Success, with a staged file name.
5. Carry the data file **and** its `.manifest.json` from A's staging folder into B's `inbound/` folder.
6. On B, the run appears as Pending Review within one poll interval. Check:
   - "From connector" names A's instance ID;
   - inserted / changed / unchanged counts match expectations;
   - the diff shows "(new row)" for inserts.
7. Release with two different users. Check B's system: new rows present with correct types (dates, IDs),
   changed rows updated, soft-deleted rows flagged.

# Negative checks

8. Drop the same file into B's `inbound/` again → audited as a duplicate, no second run.
9. Drop A's file into **A's** own `inbound/` → quarantined to `rejected/` as its own export.
10. Change a row on B, then release a run staged before that change → the row is counted as a conflict,
    not overwritten.
