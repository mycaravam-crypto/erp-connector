namespace Connector.Core.DynamicImport;

/// <summary>
/// Reshapes an <see cref="ImportWalkResult"/> (a per-row diff tree, built for preview/review) into an
/// <see cref="ImportPlan"/> (a flat, structured write list plus the six <c>ImportRunEntity</c>
/// (Connector.Infrastructure) statistics). Pure and DB-free: everything it needs is already in the walk
/// result.
///
/// <para><b>Only root-row fields become operations.</b> <see cref="ImportChildResult"/> diffs (an
/// object-kind child like <c>maintenancePlan</c>) stay visible in the walk result for the review UI, but are
/// never turned into a write here: <c>AllowedWritableColumns</c> covers root-entity columns only. A child-row
/// write path would need a stable single-row key for an object child (<see cref="ImportChildResult"/>
/// confirms a child matched but doesn't carry the matched row's own key value out of the walker) and a
/// per-item key for an array child, which the data model doesn't have.</para>
/// </summary>
public static class ImportPlanBuilder
{
    /// <summary>
    /// Builds the commit-ready <see cref="ImportPlan"/> for one <paramref name="walkResult"/>, addressing every
    /// operation's row via <paramref name="rootTable"/>/<paramref name="rootMatchColumn"/> — the same pair
    /// <c>ImportNodeWalker.WalkAsync</c> matched each row against, so the commit step can never target a row
    /// the walker didn't actually resolve.
    /// </summary>
    public static ImportPlan Build(ImportWalkResult walkResult, string rootTable, string rootMatchColumn)
    {
        var operations = new List<ImportPlanOperation>();
        int changed = 0,
            unchanged = 0,
            rejected = 0,
            invalid = 0;

        foreach (var row in walkResult.Rows)
        {
            switch (row.Status)
            {
                case ImportRowStatus.Invalid:
                    invalid++;
                    continue;
                case ImportRowStatus.Rejected:
                case ImportRowStatus.Quarantined:
                    // Folded together on ImportRunEntity — see ImportRowStatus's doc comment for why there's
                    // no separate QuarantinedCount.
                    rejected++;
                    continue;
            }

            // row.Status == Accepted from here on: a matched root row, per ImportNodeWalker.WalkAsync — its
            // CorrelationValue is therefore never null (only unmatched/malformed rows leave it null).
            if (row.Fields.Count == 0)
            {
                unchanged++;
                continue;
            }

            changed++;
            foreach (var field in row.Fields)
            {
                operations.Add(
                    new ImportPlanOperation(
                        row.CorrelationValue!,
                        rootTable,
                        rootMatchColumn,
                        row.CorrelationValue!,
                        field.Column,
                        field.OldValue,
                        field.NewValue
                    )
                );
            }
        }

        return new ImportPlan(
            walkResult.RecordCount,
            changed + unchanged,
            changed,
            unchanged,
            rejected,
            invalid,
            operations
        );
    }
}
