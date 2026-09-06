namespace Connector.Core.DynamicImport;

/// <summary>
/// Reshapes Slice 2's <see cref="ImportWalkResult"/> (a per-row diff tree, built for preview/review) into
/// Slice 3's <see cref="ImportPlan"/> (a flat, structured write list plus the six <c>ImportRunEntity</c>
/// (Connector.Infrastructure) statistics) — the reshaping <c>ImportNodeWalker</c>'s own doc comment flags as
/// deferred to this slice (Open Decision #11). Pure and DB-free: everything it needs is already in the walk
/// result.
///
/// <para><b>Only root-row fields become operations.</b> <see cref="ImportChildResult"/> diffs (an
/// object-kind child like <c>maintenancePlan</c>) stay visible in the walk result for the review UI, but are
/// never turned into a write here. This isn't a gap: v1's only real <c>AllowedWritableColumns</c> scope is
/// "confirmation/status fields on the root entity" (Open Decision #5) — no shipped definition writes to a
/// child table — and building a generic child-row write path would need a stable single-row key for an
/// object child (today's <see cref="ImportChildResult"/> confirms a child matched but, deliberately, doesn't
/// carry the matched row's own key value out of the walker) and a per-item key for an array child that
/// <see cref="ImportChildResult"/>'s own doc comment says the data model doesn't have yet. Extending this is
/// a v2+ change with its own review, matching the precedent Open Decision #15 sets for
/// <c>OnMissingChild = insert</c>: the type/walker can represent more than v1 commits.</para>
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
