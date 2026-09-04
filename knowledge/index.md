---
okf_version: "0.1"
---

# X5 Connector — Knowledge Bundle

The connector reads maintenance-relevant CIs from the ERP (read-only) via a runtime-configurable
mapping (source table, columns, joins — no hardcoded schema) and produces a daily export
(Excel/CSV/JSON) + SHA-256 manifest for four-eyes release to the vendor gateway. The connector
never crosses the air gap — its output ends at the staging folder.

Folders are organized by the question you're asking, not just by topic:

* [pipeline/](pipeline/) — **How does the export actually run?** The live query/build engine,
  the scheduler, the staging writer, and the Phase 14 spec it grew into.
* [dynamic-export/](dynamic-export/) — **How does a saved Export Definition run?** The
  `ExportNode` tree, its own scheduler, and its run-history entity (Phase 14).
* [domain/](domain/) — **What are the data shapes?** Live domain types only.
* [schema/](schema/) — **What's the ICD column contract with the vendor?**
* [api/](api/) — **How do I call the API?** Authentication, on-demand triggers, and the
  Export Definition API reference.
* [operations/](operations/) — **What rules govern running this day to day?** Four-eyes release,
  GDPR compliance, data retention, operational monitoring.
* [legacy/](legacy/) — **What used to exist, and why does some rule still apply?** The original
  fixed six-stage pipeline and its types — deleted from the codebase, kept only as design
  rationale. Nothing here describes running code.
* [planning/](planning/) — **What's not done yet?** Open stakeholder decisions and the
  engineering backlog.
* [changelog.md](changelog.md) — Phase-by-phase record of what shipped, plus current
  in-progress status.

> **2.0 note:** the original Technical Concept described a fixed six-stage pipeline against a
> hardcoded ERP shape. That design was superseded during development by the runtime-configurable
> [DynamicExportService](/pipeline/dynamic-export-service.md); its docs moved to
> [legacy/](legacy/) and no longer describe running code.
