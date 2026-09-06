using System.Text.Json;

namespace Connector.Core.DynamicImport;

/// <summary>
/// One column-level write the commit step (Slice 3) will make: the value observed on the ERP row when the
/// plan was built (<see cref="ExpectedOldValue"/>) and the value to write in its place. This is the same
/// value <see cref="ImportFieldDiff.OldValue"/> already carries — the field is renamed here, not
/// re-derived, to make its second job explicit: <c>ImportRunReleaser</c>'s (Connector.Infrastructure)
/// conditional <c>UPDATE ... WHERE ... IS NOT DISTINCT FROM @expectedOldValue</c> guard (Open Decision #12)
/// uses this exact value, so a row whose ERP state moved on since staging is excluded from the commit rather
/// than silently overwritten. <see cref="CorrelationValue"/> is carried for audit/troubleshooting only — the
/// commit itself addresses the row via <see cref="Table"/>/<see cref="KeyColumn"/>/<see cref="KeyValue"/>,
/// which for a v1 real definition are always <c>RootTable</c>/<c>RootMatchColumn</c>/the matched root row's
/// correlation value (see <see cref="ImportPlanBuilder"/> for why child-table writes aren't emitted here).
/// </summary>
public sealed record ImportPlanOperation(
    string CorrelationValue,
    string Table,
    string KeyColumn,
    string KeyValue,
    string Column,
    string? ExpectedOldValue,
    string? NewValue
);

/// <summary>
/// The persisted write protocol for one <c>ImportRunEntity</c> (Connector.Infrastructure; Open Decision #11):
/// <see cref="Operations"/> is the structured, versioned list <c>ImportRunReleaser</c> commits from, and the
/// only authoritative source for what a release actually writes — a UI diff or audit-log line is a read
/// projection of this, never a second independently-computed shape. The five counts mirror
/// <c>ImportRunEntity</c>'s own (<see cref="MatchedCount"/> = <see cref="ChangedCount"/> +
/// <see cref="UnchangedCount"/>) so the entity can be populated by copying this record's fields verbatim.
/// </summary>
public sealed record ImportPlan(
    int RecordCount,
    int MatchedCount,
    int ChangedCount,
    int UnchangedCount,
    int RejectedCount,
    int InvalidCount,
    IReadOnlyList<ImportPlanOperation> Operations
);

/// <summary>
/// Serializes/deserializes <see cref="ImportPlan"/> for <c>ImportRunEntity.PlanJson</c> (Connector.Infrastructure)
/// — a thin wrapper (matching <see cref="ImportNodeJson"/>'s role for <c>RootNode</c>) so every read/write of
/// that column goes through one place, even though, unlike <see cref="ImportNode"/>, <see cref="ImportPlan"/>
/// has no recursive shape needing missing-property backfill.
/// </summary>
public static class ImportPlanJson
{
    public static ImportPlan? Deserialize(string json) => JsonSerializer.Deserialize<ImportPlan>(json);

    public static string Serialize(ImportPlan plan) => JsonSerializer.Serialize(plan);
}
