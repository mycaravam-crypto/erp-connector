using System.Text.Json;
using Connector.Core.DataSources;
using Connector.Core.DynamicExport;
using Connector.Core.DynamicImport;
using Connector.Infrastructure.DataSources;

namespace Connector.Infrastructure;

/// <summary>
/// Slice 3 of Phase 17 (import-definitions.md §3 steps 6-8, §5): commits an approved <see cref="ImportRunEntity"/>
/// to the ERP under the same Operator/Approver four-eyes contract the export release flow already enforces —
/// the write-side counterpart to <see cref="ExportDefinitionRunner"/>. <see cref="ReleaseAsync"/> and
/// <see cref="RejectAsync"/> take an already-loaded <see cref="ImportRunEntity"/> (the caller — an API
/// endpoint — owns fetching it and checking its <see cref="ImportRunStatus"/>) and finalize it; neither
/// method opens its own DbContext scope, matching every other run-mutating helper in this codebase
/// (<see cref="ExportDefinitionRunner.ExecuteAsync"/>, the inline export release handler).
/// </summary>
public static class ImportRunReleaser
{
    /// <summary>
    /// Applies <paramref name="run"/>'s persisted <c>PlanJson</c> to the ERP: one conditional <c>UPDATE</c> per
    /// row (Open Decision #12) — every column that row's plan changes, guarded by every one of that row's
    /// captured expected-old-values in a single <c>WHERE</c> clause, so a row commits all its changed columns
    /// or none of them, never half. Zero affected rows means the row's ERP state moved on since staging; that
    /// row is excluded, counted in <see cref="ImportRunEntity.ConflictCount"/>, and left untouched — it does
    /// not fail the run (Open Decision #6). An unrelated failure (a thrown exception — e.g. a constraint
    /// violation) rolls back everything committed so far in this call and marks the run
    /// <see cref="ImportRunStatus.Failed"/>, matching the "no silent partial success" rule already enforced on
    /// the export side. Caller (an endpoint, in <c>Connector.Api</c>) must have already validated
    /// <paramref name="operatorName"/>/<paramref name="approver"/> for the four-eyes distinctness check and
    /// that <paramref name="run"/>.Status is <see cref="ImportRunStatus.PendingReview"/>.
    /// </summary>
    public static async Task ReleaseAsync(
        ExportLogDbContext db,
        ImportRunEntity run,
        string operatorName,
        string approver,
        AuditService audit,
        IDataSourceProviderResolver resolver,
        CancellationToken ct
    )
    {
        async Task FailAsync(string message)
        {
            run.Status = ImportRunStatus.Failed;
            run.ErrorMessage = message;
            run.FinishedAt = DateTimeOffset.UtcNow.ToString("O");
            await db.SaveChangesAsync(CancellationToken.None);
            await audit.LogAsync(operatorName, "import_run_failed", $"id={run.Id}: {message}");
        }

        var plan = string.IsNullOrWhiteSpace(run.PlanJson) ? null : ImportPlanJson.Deserialize(run.PlanJson);
        if (plan is null)
        {
            await FailAsync("Stored PlanJson could not be read.");
            return;
        }

        var connRaw = await db.GetSettingRawAsync(SettingsKeys.ErpConnection);
        if (connRaw is null)
        {
            await FailAsync("No database connection configured.");
            return;
        }
        var connCfg = JsonSerializer.Deserialize<DataSourceConfig>(connRaw);
        if (connCfg is null)
        {
            await FailAsync("Stored database connection config could not be read.");
            return;
        }

        // Security-review finding SR-05: the plan/policy/schema context was reviewed against whatever
        // connection was configured at staging time, but this method always writes to the *current*
        // connection setting. If that setting changed since staging, an approval given for target A could
        // otherwise get committed against target B. StagedConnectionFingerprint is null for runs staged
        // before this fix — those keep the prior (unverified) behavior rather than failing outright.
        var currentFingerprint = DynamicExportService.ConnectionFingerprint(connCfg);
        if (run.StagedConnectionFingerprint is not null && run.StagedConnectionFingerprint != currentFingerprint)
        {
            await FailAsync(
                $"ERP connection changed since this run was staged (was '{run.StagedConnectionFingerprint}', "
                    + $"now '{currentFingerprint}'). Re-stage this file against the current connection before release."
            );
            return;
        }

        int conflictCount;
        try
        {
            // One transaction on the provider's own ADO.NET connection; SQL rendered with its dialect
            // (Arbeitsauftrag 14) — the provider must have the Imports capability.
            await using var import = await ImportConnection.OpenAsync(resolver, connCfg, ct);
            var dialect = import.Dialect;
            await using var tx = await import.Connection.BeginTransactionAsync(ct);

            conflictCount = 0;
            foreach (var rowOps in plan.Operations.GroupBy(o => (o.Table, o.KeyColumn, o.KeyValue)))
            {
                var ops = rowOps.ToList();
                await using var cmd = import.Connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandTimeout = 10;
                string Bind(object? value)
                {
                    var parameter = cmd.CreateParameter();
                    parameter.ParameterName = dialect.BuildParameterName(cmd.Parameters.Count);
                    parameter.Value = value ?? DBNull.Value;
                    cmd.Parameters.Add(parameter);
                    return parameter.ParameterName;
                }

                var setClause = string.Join(
                    ", ",
                    ops.Select(o => $"{dialect.QuoteIdentifier(o.Column)} = {Bind(o.NewValue)}")
                );
                var guardClause = string.Join(
                    " AND ",
                    ops.Select(o =>
                        dialect.BuildNullSafeEquals(
                            dialect.CastToText(dialect.QuoteIdentifier(o.Column)),
                            Bind(o.ExpectedOldValue)
                        )
                    )
                );
                var keyClause =
                    $"{dialect.CastToText(dialect.QuoteIdentifier(rowOps.Key.KeyColumn))} = {Bind(rowOps.Key.KeyValue)}";
                cmd.CommandText =
                    $"UPDATE {dialect.QuoteIdentifier(rowOps.Key.Table)} SET {setClause} WHERE {keyClause} AND {guardClause}";

                var affected = await cmd.ExecuteNonQueryAsync(ct);
                if (affected == 0)
                    conflictCount++;
            }

            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The `await using` declarations above dispose conn/tx while unwinding out of the try block,
            // before this catch body runs — an uncommitted transaction rolls back on Dispose, so the ERP is
            // left exactly as it was before this call.
            await FailAsync(ErrorSanitizer.Detail(ex));
            return;
        }

        // Security-review finding SR-09: the ERP transaction above is already committed and irreversible at
        // this point, so recording that outcome locally must not be skippable via cancellation — same
        // reasoning FailAsync already applies to its own post-outcome save. Using `ct` here (as this used
        // to) risked exactly the divergence the review flagged: a cancellation landing in this narrow
        // window would leave the run stuck at PendingReview locally despite the ERP write having already
        // gone through, so a retry would replay the plan against rows that no longer match their expected
        // old values and misreport a clean release as a conflict.
        run.Status = ImportRunStatus.Released;
        run.OperatedBy = operatorName;
        run.ApprovedBy = approver;
        run.ReleasedAt = DateTimeOffset.UtcNow.ToString("O");
        run.FinishedAt = run.ReleasedAt;
        run.ConflictCount = conflictCount;
        await db.SaveChangesAsync(CancellationToken.None);

        await audit.LogAsync(
            operatorName,
            "import_run_released",
            $"id={run.Id} approver={approver} matched={run.MatchedCount} changed={run.ChangedCount} "
                + $"unchanged={run.UnchangedCount} rejected={run.RejectedCount} invalid={run.InvalidCount}"
                + (conflictCount > 0 ? $" conflicts={conflictCount}" : "")
        );
    }

    /// <summary>
    /// Declines a <see cref="ImportRunStatus.PendingReview"/> run without touching the ERP. Single-person —
    /// unlike <see cref="ReleaseAsync"/>, this never writes to the system of record, so it doesn't need a
    /// distinct Approver (the same reasoning the export side's own single-person "skip a run" action rests
    /// on).
    /// </summary>
    public static async Task RejectAsync(
        ExportLogDbContext db,
        ImportRunEntity run,
        string operatorName,
        AuditService audit,
        CancellationToken ct
    )
    {
        run.Status = ImportRunStatus.Rejected;
        run.OperatedBy = operatorName;
        run.FinishedAt = DateTimeOffset.UtcNow.ToString("O");
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(operatorName, "import_run_rejected", $"id={run.Id}");
    }
}
