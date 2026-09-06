using Connector.Core.DynamicImport;

namespace Connector.Core.Tests;

/// <summary>
/// Coverage for <see cref="ImportPlanBuilder"/> — Slice 3's reshaping of Slice 2's
/// <see cref="ImportWalkResult"/> into the persisted <see cref="ImportPlan"/> (Open Decision #11). Pure and
/// DB-free, unlike <c>ImportNodeWalker</c> itself, so no Postgres fixture is needed here (see
/// <c>ImportNodeWalkerPostgresTests</c> in Connector.Integration.Tests for the walker's own coverage).
/// </summary>
public sealed class ImportPlanBuilderTests
{
    private const string RootTable = "systemconfiguration";
    private const string RootMatchColumn = "id";

    private static ImportRowResult AcceptedRow(string correlationValue, params ImportFieldDiff[] fields) =>
        new(correlationValue, ImportRowStatus.Accepted, null, fields, []);

    [Fact]
    public void Build_AcceptedRowWithFieldDiff_CountsAsChangedAndEmitsOneOperationPerField()
    {
        var walkResult = new ImportWalkResult(
            RecordCount: 1,
            AcceptedCount: 1,
            RejectedCount: 0,
            Rows: [AcceptedRow("guid-1", new ImportFieldDiff("status", "active", "confirmed"))]
        );

        var plan = ImportPlanBuilder.Build(walkResult, RootTable, RootMatchColumn);

        Assert.Equal(1, plan.RecordCount);
        Assert.Equal(1, plan.MatchedCount);
        Assert.Equal(1, plan.ChangedCount);
        Assert.Equal(0, plan.UnchangedCount);
        Assert.Equal(0, plan.RejectedCount);
        Assert.Equal(0, plan.InvalidCount);

        var op = Assert.Single(plan.Operations);
        Assert.Equal("guid-1", op.CorrelationValue);
        Assert.Equal(RootTable, op.Table);
        Assert.Equal(RootMatchColumn, op.KeyColumn);
        Assert.Equal("guid-1", op.KeyValue);
        Assert.Equal("status", op.Column);
        Assert.Equal("active", op.ExpectedOldValue);
        Assert.Equal("confirmed", op.NewValue);
    }

    [Fact]
    public void Build_AcceptedRowWithNoFieldDiff_CountsAsUnchangedAndEmitsNoOperation()
    {
        var walkResult = new ImportWalkResult(1, 1, 0, [AcceptedRow("guid-1")]);

        var plan = ImportPlanBuilder.Build(walkResult, RootTable, RootMatchColumn);

        Assert.Equal(1, plan.MatchedCount);
        Assert.Equal(0, plan.ChangedCount);
        Assert.Equal(1, plan.UnchangedCount);
        Assert.Empty(plan.Operations);
    }

    [Fact]
    public void Build_RejectedRow_CountsAsRejectedNotMatched()
    {
        var walkResult = new ImportWalkResult(
            1,
            0,
            1,
            [new ImportRowResult("guid-1", ImportRowStatus.Rejected, "no match", [], [])]
        );

        var plan = ImportPlanBuilder.Build(walkResult, RootTable, RootMatchColumn);

        Assert.Equal(0, plan.MatchedCount);
        Assert.Equal(1, plan.RejectedCount);
        Assert.Empty(plan.Operations);
    }

    [Fact]
    public void Build_QuarantinedRow_FoldsIntoRejectedCount()
    {
        // ImportRunEntity has no separate quarantine counter (Open Decision #11 lists only
        // matched/changed, matched/unchanged, rejected, invalid) — see ImportRowStatus's doc comment.
        var walkResult = new ImportWalkResult(
            1,
            0,
            1,
            [new ImportRowResult("guid-1", ImportRowStatus.Quarantined, "no match", [], [])]
        );

        var plan = ImportPlanBuilder.Build(walkResult, RootTable, RootMatchColumn);

        Assert.Equal(1, plan.RejectedCount);
        Assert.Empty(plan.Operations);
    }

    [Fact]
    public void Build_InvalidRow_CountsSeparatelyFromRejected()
    {
        var walkResult = new ImportWalkResult(
            1,
            0,
            1,
            [new ImportRowResult(null, ImportRowStatus.Invalid, "Record is not a JSON object.", [], [])]
        );

        var plan = ImportPlanBuilder.Build(walkResult, RootTable, RootMatchColumn);

        Assert.Equal(1, plan.InvalidCount);
        Assert.Equal(0, plan.RejectedCount);
        Assert.Empty(plan.Operations);
    }

    [Fact]
    public void Build_ObjectChildFieldDiff_IsNotEmittedAsAnOperation()
    {
        // v1 scope: only root-level fields ever become write operations — see ImportPlanBuilder's own doc
        // comment for why a child's Fields never turn into a PlanJson operation.
        var childWithDiff = new ImportChildResult(
            "maintenancePlan",
            "maintenance_plan",
            true,
            null,
            [new ImportFieldDiff("allocation_chart_ref", "AC-2024-011", "AC-2024-099")],
            []
        );
        var row = new ImportRowResult("guid-1", ImportRowStatus.Accepted, null, [], [childWithDiff]);
        var walkResult = new ImportWalkResult(1, 1, 0, [row]);

        var plan = ImportPlanBuilder.Build(walkResult, RootTable, RootMatchColumn);

        // The row itself has no root-level field diff, so it's unchanged at the root even though its child
        // has one — the child diff stays visible on the walk result for review, just not in the plan.
        Assert.Equal(1, plan.UnchangedCount);
        Assert.Empty(plan.Operations);
    }

    [Fact]
    public void Build_MultipleRowsMixedOutcomes_CountsEachExactlyOnce()
    {
        var walkResult = new ImportWalkResult(
            RecordCount: 4,
            AcceptedCount: 2,
            RejectedCount: 2,
            Rows:
            [
                AcceptedRow("guid-1", new ImportFieldDiff("status", "active", "confirmed")),
                AcceptedRow("guid-2"),
                new ImportRowResult("guid-3", ImportRowStatus.Rejected, "no match", [], []),
                new ImportRowResult(null, ImportRowStatus.Invalid, "not an object", [], []),
            ]
        );

        var plan = ImportPlanBuilder.Build(walkResult, RootTable, RootMatchColumn);

        Assert.Equal(4, plan.RecordCount);
        Assert.Equal(2, plan.MatchedCount);
        Assert.Equal(1, plan.ChangedCount);
        Assert.Equal(1, plan.UnchangedCount);
        Assert.Equal(1, plan.RejectedCount);
        Assert.Equal(1, plan.InvalidCount);
        Assert.Single(plan.Operations);
    }
}
