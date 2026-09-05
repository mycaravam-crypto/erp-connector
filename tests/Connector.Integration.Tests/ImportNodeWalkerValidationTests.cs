using Connector.Core.DynamicImport;
using Connector.Infrastructure;

namespace Connector.Integration.Tests;

/// <summary>
/// Coverage for <see cref="ImportNodeWalker.ValidateWritableColumns"/> — the definition-level check that runs
/// before any DB access, so it's testable without the Postgres fixture (unlike the rest of
/// <see cref="ImportNodeWalker"/>; see <see cref="ImportNodeWalkerPostgresTests"/> for those).
/// </summary>
public sealed class ImportNodeWalkerValidationTests
{
    private static ImportNode Scalar(string sourceKey, string targetColumn, bool enabled = true) =>
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
            Enabled: enabled
        );

    private static ImportNode Root(params ImportNode[] children) =>
        new(
            SourceKey: "root",
            Kind: ImportNodeKind.Root,
            TargetColumn: null,
            RelatedTable: null,
            JoinKey: null,
            SourceJoinKey: null,
            OnMissingChild: OnMissingChildPolicy.Reject,
            Mapping: null,
            Children: children,
            Enabled: true
        );

    [Fact]
    public void ValidateWritableColumns_AllColumnsAllowed_ReturnsNoViolations()
    {
        var matchField = Scalar("ciId", "id");
        var statusField = Scalar("confirmationStatus", "status");
        var root = Root(matchField, statusField);

        var violations = ImportNodeWalker.ValidateWritableColumns(
            root,
            matchField,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "status" }
        );

        Assert.Empty(violations);
    }

    [Fact]
    public void ValidateWritableColumns_ColumnOutsideAllowlist_IsReported()
    {
        var matchField = Scalar("ciId", "id");
        var storageField = Scalar("storageLocation", "storage_location");
        var root = Root(matchField, storageField);

        var violations = ImportNodeWalker.ValidateWritableColumns(
            root,
            matchField,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "status" }
        );

        Assert.Contains("storage_location", violations);
    }

    [Fact]
    public void ValidateWritableColumns_MatchFieldItself_IsNeverReported()
    {
        // The correlation-key field is read for matching only — it must never need to appear in
        // AllowedWritableColumns, since v1 definitions only allowlist confirmation/status columns (§1).
        var matchField = Scalar("ciId", "id");
        var root = Root(matchField);

        var violations = ImportNodeWalker.ValidateWritableColumns(root, matchField, new HashSet<string>());

        Assert.Empty(violations);
    }

    [Fact]
    public void ValidateWritableColumns_GdprDeniedColumn_IsReportedEvenIfAllowlisted()
    {
        var matchField = Scalar("ciId", "id");
        // "technician_name" is on DynamicExportService.GdprDeniedFields — defence-in-depth per Open Decision #7
        // means it's rejected even if a misconfigured definition also allowlists it.
        var technicianField = Scalar("technicianName", "technician_name");
        var root = Root(matchField, technicianField);

        var violations = ImportNodeWalker.ValidateWritableColumns(
            root,
            matchField,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "technician_name" }
        );

        Assert.Contains("technician_name", violations);
    }

    [Fact]
    public void ValidateWritableColumns_NestedChildColumnOutsideAllowlist_IsReportedRecursively()
    {
        var matchField = Scalar("ciId", "id");
        var childScalar = Scalar("planStatus", "status");
        var childNode = new ImportNode(
            SourceKey: "maintenancePlan",
            Kind: ImportNodeKind.Object,
            TargetColumn: null,
            RelatedTable: "maintenance_plan",
            JoinKey: "system_configuration_id",
            SourceJoinKey: "id",
            OnMissingChild: OnMissingChildPolicy.Reject,
            Mapping: null,
            Children: [childScalar],
            Enabled: true
        );
        var root = Root(matchField, childNode);

        var violations = ImportNodeWalker.ValidateWritableColumns(root, matchField, new HashSet<string>());

        Assert.Contains("status", violations);
    }

    [Fact]
    public void ValidateWritableColumns_DisabledColumn_IsIgnored()
    {
        var matchField = Scalar("ciId", "id");
        var disabledField = Scalar("storageLocation", "storage_location", enabled: false);
        var root = Root(matchField, disabledField);

        var violations = ImportNodeWalker.ValidateWritableColumns(root, matchField, new HashSet<string>());

        Assert.Empty(violations);
    }
}
