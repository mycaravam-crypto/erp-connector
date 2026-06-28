---
type: Pipeline Stage
title: IErpReader — Stage 1
description: Reads all maintainable Configuration Items from the ERP as a full read-only snapshot.
resource: src/Connector.Core/Interfaces/IErpReader.cs
tags: [pipeline, stage-1, erp, read-only, snapshot]
timestamp: 2026-06-28T00:00:00Z
---

The first stage of the export pipeline. Reads all maintainable CIs from the ERP source system.

# Contract

```csharp
Task<IReadOnlyList<ErpConfigurationItem>> ReadMaintainableCIsAsync(CancellationToken ct);
```

# Constraints

- **Read-only**: implementations must never write to the ERP.
- **Idempotent**: multiple calls in the same time window must return the same data.
- **Full snapshot**: no delta parameter in Iteration 1 (Open Point #5: volume to be assessed).
- Throws `ErpConnectionException` if the ERP is unreachable.
- Must honour the `CancellationToken` cooperatively.

# Implementations

| Class           | Project            | Data source          | Use case           |
|-----------------|--------------------|----------------------|--------------------|
| `DemoErpReader` | `Connector.Erp`    | SQLite (demo_erp.db) | Dev / integration tests |
| _(TBD)_         | `Connector.Erp`    | PostgreSQL           | Production         |

To swap to a production reader: implement `IErpReader`, register it in `Program.cs` in place
of `DemoErpReader`. No other pipeline files change.

# Demo Seed (DemoErpReader)

7 CIs in the seed database; 5 are in scope after filtering. The demo `DemoErpDbContext`
uses `AsNoTracking()` to enforce read-only semantics at the EF Core level.

| CI              | Serial Number  | State           | In scope? |
|-----------------|----------------|-----------------|-----------|
| sc-rack-0001    | SN-RACK-0001   | Active          | Yes (root)|
| sc-blade-0001   | SN-BLD-0001    | Active          | Yes       |
| sc-blade-0002   | SN-BLD-0002    | InRepair        | Yes       |
| sc-psu-0001     | SN-PSU-0001    | Active          | Yes       |
| sc-psu-0002     | SN-PSU-0002    | Active          | No (no maintenance plan) |
| sc-sw-0001      | SN-SW-0001     | Active          | Yes       |
| sc-rack-0002    | SN-RACK-0002   | Decommissioned  | No (plan inactive) |

# Output

Produces [ErpConfigurationItem](/domain/erp-configuration-item.md) records passed to
[IExportFilter](/pipeline/export-filter.md).
