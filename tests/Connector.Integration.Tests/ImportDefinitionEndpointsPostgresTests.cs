using Connector.Api;
using Connector.Api.Endpoints;
using Connector.Core.DynamicImport;

namespace Connector.Integration.Tests;

/// <summary>
/// Real-Postgres coverage for <see cref="ImportDefinitionEndpoints.ValidateRequestAsync"/> — Phase 17
/// Slice 5's save-time guardrails (import-definitions.md §6 Open Decisions #9 and #15), the primary safety
/// boundary of the whole import feature. Uses the same local <c>testdb</c> fixture and the same "no-op
/// instead of fail" convention as <see cref="ImportNodeWalkerPostgresTests"/>/<see cref="ImportRunReleaserPostgresTests"/>
/// when it isn't running — every test that needs to introspect the real schema opens a throwaway
/// connection first and returns early if that fails.
///
/// The <c>ImportNodeWalker.OnMissingChild == Insert</c> rejection test is the one exception: that branch
/// of <see cref="ImportDefinitionEndpoints.ValidateRequestAsync"/> returns before ever touching the ERP
/// connection, so it runs unconditionally.
///
/// Requires: <c>docker-compose --profile test up -d testdb</c> for every test but the OnMissingChild one.
/// </summary>
public sealed class ImportDefinitionEndpointsPostgresTests
{
    // ── Tree builders (mirrors ImportNodeWalkerPostgresTests' own Scalar/SystemConfigurationRoot) ──────

    private static ImportNode Scalar(string sourceKey, string targetColumn) =>
        new(
            SourceKey: sourceKey,
            Kind: ImportNodeKind.ScalarField,
            TargetColumn: targetColumn,
            RelatedTable: null,
            JoinKey: null,
            SourceJoinKey: null,
            OnMissingChild: OnMissingChildPolicy.Reject,
            Mapping: null,
            Children: [],
            Enabled: true
        );

    private static ImportNode Root(ImportNode matchChild, params ImportNode[] extraChildren) =>
        new(
            SourceKey: "root",
            Kind: ImportNodeKind.Root,
            TargetColumn: null,
            RelatedTable: null,
            JoinKey: null,
            SourceJoinKey: null,
            OnMissingChild: OnMissingChildPolicy.Reject,
            Mapping: null,
            Children: [matchChild, .. extraChildren],
            Enabled: true
        );

    private static ImportDefinitionRequest RequestFor(
        ImportNode root,
        List<string> allowedWritableColumns,
        string rootMatchColumn = "id"
    ) =>
        new(
            Name: "Test Import Definition",
            Description: null,
            RootTable: "systemconfiguration",
            RootMatchColumn: rootMatchColumn,
            RootNode: root,
            AllowedWritableColumns: allowedWritableColumns,
            UnmatchedRootPolicy: UnmatchedRootPolicy.Reject,
            IsEnabled: true
        );

    [Fact]
    public async Task ValidateRequestAsync_WritableColumnInAllowlistAndSchema_Succeeds()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        await using var local = await LocalDb.NewAsync();
        var root = Root(Scalar("ciId", "id"), Scalar("confirmationStatus", "status"));
        var request = RequestFor(root, ["status"]);

        var (resultRoot, error) = await ImportDefinitionEndpoints.ValidateRequestAsync(
            request,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(error);
        Assert.NotNull(resultRoot);
    }

    [Fact]
    public async Task ValidateRequestAsync_ColumnNotInAllowedWritableColumns_RejectsAsOutOfScope()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        await using var local = await LocalDb.NewAsync();
        // storage_location exists on systemconfiguration and isn't on the GDPR denylist — the only thing
        // wrong with it here is that the allowlist below doesn't mention it.
        var root = Root(Scalar("ciId", "id"), Scalar("location", "storage_location"));
        var request = RequestFor(root, ["status"]);

        var (resultRoot, error) = await ImportDefinitionEndpoints.ValidateRequestAsync(
            request,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(resultRoot);
        Assert.Contains("not present in AllowedWritableColumns", error);
    }

    [Fact]
    public async Task ValidateRequestAsync_ColumnDoesNotExistOnTable_RejectsWithSpecificError()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        await using var local = await LocalDb.NewAsync();
        var root = Root(Scalar("ciId", "id"), Scalar("nope", "no_such_column"));
        var request = RequestFor(root, ["no_such_column"]);

        var (resultRoot, error) = await ImportDefinitionEndpoints.ValidateRequestAsync(
            request,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(resultRoot);
        Assert.Contains("does not exist on table 'systemconfiguration'", error);
    }

    [Fact]
    public async Task ValidateRequestAsync_TargetIsPrimaryKey_Rejects()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        await using var local = await LocalDb.NewAsync();
        // Uses "serial" as the correlation key here so "id" is free to be tested as a (rejected) write
        // target — in a real definition the match column and "id" are almost always the same thing.
        var root = Root(Scalar("ciSerial", "serial"), Scalar("ciId", "id"));
        var request = RequestFor(root, ["id"], rootMatchColumn: "serial");

        var (resultRoot, error) = await ImportDefinitionEndpoints.ValidateRequestAsync(
            request,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(resultRoot);
        Assert.Contains("is the primary key", error);
    }

    [Fact]
    public async Task ValidateRequestAsync_TargetIsGeneratedColumn_Rejects()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        await using var local = await LocalDb.NewAsync();
        // status_upper is `GENERATED ALWAYS AS (upper(status)) STORED` in testdb/init.sql — dedicated to
        // exercising this exact rejection branch.
        var root = Root(Scalar("ciId", "id"), Scalar("statusUpper", "status_upper"));
        var request = RequestFor(root, ["status_upper"]);

        var (resultRoot, error) = await ImportDefinitionEndpoints.ValidateRequestAsync(
            request,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(resultRoot);
        Assert.Contains("identity/computed column", error);
    }

    [Fact]
    public async Task ValidateRequestAsync_TargetIsForeignKey_Rejects()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        await using var local = await LocalDb.NewAsync();
        var root = Root(Scalar("ciId", "id"), Scalar("articleId", "article_id"));
        var request = RequestFor(root, ["article_id"]);

        var (resultRoot, error) = await ImportDefinitionEndpoints.ValidateRequestAsync(
            request,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(resultRoot);
        Assert.Contains("is a foreign key", error);
        Assert.Contains("not writable in v1", error);
    }

    [Fact]
    public async Task ValidateRequestAsync_AllowedWritableColumnsListsGdprDeniedField_RejectsBeforeTouchingTheErp()
    {
        // Doesn't need testdb: the GDPR-denylist-in-allowlist check runs (and fails) before the schema-aware
        // pass ever opens an ERP connection.
        await using var local = await LocalDb.NewAsync();
        var root = Root(Scalar("ciId", "id"), Scalar("tech", "technician_name"));
        var request = RequestFor(root, ["technician_name"]);

        var (resultRoot, error) = await ImportDefinitionEndpoints.ValidateRequestAsync(
            request,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(resultRoot);
        Assert.Contains("GDPR-denied", error);
    }

    [Fact]
    public async Task ValidateRequestAsync_OnMissingChildInsert_RejectsWithoutTouchingTheErp()
    {
        // Doesn't need testdb: the tree walk rejects OnMissingChild = "insert" before ever reaching the
        // schema-aware pass — see ValidateRequestAsync's own doc comment.
        await using var local = await LocalDb.NewAsync();
        var maintenancePlanChild = new ImportNode(
            SourceKey: "maintenancePlan",
            Kind: ImportNodeKind.Object,
            TargetColumn: null,
            RelatedTable: "maintenance_plan",
            JoinKey: "system_configuration_id",
            SourceJoinKey: "id",
            OnMissingChild: OnMissingChildPolicy.Insert,
            Mapping: null,
            Children: [Scalar("allocationChartRef", "allocation_chart_ref")],
            Enabled: true
        );
        var root = Root(Scalar("ciId", "id"), maintenancePlanChild);
        var request = RequestFor(root, []);

        var (resultRoot, error) = await ImportDefinitionEndpoints.ValidateRequestAsync(
            request,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(resultRoot);
        Assert.Contains("OnMissingChild = \"insert\" is not permitted in v1", error);
    }
}
