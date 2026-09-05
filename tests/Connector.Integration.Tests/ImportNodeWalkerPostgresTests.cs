using Connector.Core.DynamicImport;
using Connector.Infrastructure;
using Npgsql;

namespace Connector.Integration.Tests;

/// <summary>
/// Real-Postgres coverage for <see cref="ImportNodeWalker.WalkAsync"/> — Slice 2 of Phase 17
/// (import-definitions.md), the read-only diff-only counterpart to
/// <see cref="DynamicExportServiceNestedJsonPostgresTests"/>. Uses the same local <c>testdb</c> fixture and the
/// same "no-op instead of fail" convention when it isn't running (see that class's doc comment for why:
/// this repo's xunit version, 2.9.2, predates <c>Assert.Skip</c>).
///
/// Requires: <c>docker-compose --profile test up -d testdb</c>.
/// </summary>
public sealed class ImportNodeWalkerPostgresTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=erp_testdb;Username=erp_test;Password=erp_test_pw;Timeout=2";

    // Seeded in testdb/init.sql.
    private const string ActiveCiId = "44444444-4444-4444-4444-444444444444"; // status=active, has a maintenance_plan
    private const string DecommissionedCiId = "66666666-6666-6666-6666-666666666666"; // status=decommissioned, no maintenance_plan
    private const string AcmeManufacturerId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"; // has 2 addresses
    private const string NorthbridgeManufacturerId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"; // has 0 addresses
    private const string UnknownCiId = "ffffffff-ffff-ffff-ffff-ffffffffffff"; // matches no row at all

    private static async Task<NpgsqlConnection?> TryOpenAsync()
    {
        try
        {
            var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            return conn;
        }
        catch
        {
            return null;
        }
    }

    // Wraps a bare JSON array of records in the canonical ImportEnvelope (Open Decision #14) every test needs
    // — schemaVersion first, then records — so individual tests only spell out the part they're actually
    // exercising.
    private static string Envelope(string recordsArrayJson) =>
        $$"""{ "schemaVersion": "{{ImportNodeWalker.SupportedSchemaVersion}}", "records": {{recordsArrayJson}} }""";

    // ── Tree builders ────────────────────────────────────────────────────────────

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

    private static ImportNode SystemConfigurationRoot(params ImportNode[] extraChildren) =>
        new(
            SourceKey: "root",
            Kind: ImportNodeKind.Root,
            TargetColumn: null,
            RelatedTable: null,
            JoinKey: null,
            SourceJoinKey: null,
            OnMissingChild: OnMissingChildPolicy.Reject,
            Mapping: null,
            Children: [Scalar("ciId", "id"), Scalar("confirmationStatus", "status"), .. extraChildren],
            Enabled: true
        );

    private static ImportNode MaintenancePlanChild(string onMissingChild = OnMissingChildPolicy.Reject) =>
        new(
            SourceKey: "maintenancePlan",
            Kind: ImportNodeKind.Object,
            TargetColumn: null,
            RelatedTable: "maintenance_plan",
            JoinKey: "system_configuration_id",
            SourceJoinKey: "id",
            OnMissingChild: onMissingChild,
            Mapping: null,
            Children: [Scalar("allocationChartRef", "allocation_chart_ref")],
            Enabled: true
        );

    private static ImportNode ManufacturerRoot(ImportNode addressesChild) =>
        new(
            SourceKey: "root",
            Kind: ImportNodeKind.Root,
            TargetColumn: null,
            RelatedTable: null,
            JoinKey: null,
            SourceJoinKey: null,
            OnMissingChild: OnMissingChildPolicy.Reject,
            Mapping: null,
            Children: [Scalar("manufacturerId", "id"), addressesChild],
            Enabled: true
        );

    private static ImportNode AddressesChild(string onMissingChild = OnMissingChildPolicy.Reject) =>
        new(
            SourceKey: "addresses",
            Kind: ImportNodeKind.Array,
            TargetColumn: null,
            RelatedTable: "manufacturer_address",
            JoinKey: "manufacturer_id",
            SourceJoinKey: "id",
            OnMissingChild: onMissingChild,
            Mapping: null,
            Children: [Scalar("city", "city")],
            Enabled: true
        );

    private static ImportDefinitionEntity MakeDefinition(
        string rootTable,
        string rootMatchColumn,
        string[] allowedWritableColumns,
        string unmatchedRootPolicy = UnmatchedRootPolicy.Reject
    ) =>
        new()
        {
            Id = 1,
            Name = "Test Import Definition",
            RootTable = rootTable,
            RootMatchColumn = rootMatchColumn,
            RootNode = "",
            AllowedWritableColumns = System.Text.Json.JsonSerializer.Serialize(allowedWritableColumns),
            UnmatchedRootPolicy = unmatchedRootPolicy,
            IsEnabled = true,
            ConfigVersion = 1,
            CreatedBy = "test",
            CreatedAt = "2026-01-01T00:00:00Z",
        };

    // ── Root scalar diff ─────────────────────────────────────────────────────────

    [Fact]
    public async Task WalkAsync_ChangedRootScalar_ProducesFieldDiff()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = SystemConfigurationRoot();
        var definition = MakeDefinition("systemconfiguration", "id", ["status"]);
        var json = Envelope($$"""[{ "ciId": "{{ActiveCiId}}", "confirmationStatus": "confirmed" }]""");

        var result = await ImportNodeWalker.WalkAsync(conn, definition, root, json, CancellationToken.None);

        Assert.Equal(1, result.RecordCount);
        Assert.Equal(1, result.AcceptedCount);
        Assert.Equal(0, result.RejectedCount);
        var row = Assert.Single(result.Rows);
        Assert.Equal(ImportRowStatus.Accepted, row.Status);
        var field = Assert.Single(row.Fields);
        Assert.Equal("status", field.Column);
        Assert.Equal("active", field.OldValue);
        Assert.Equal("confirmed", field.NewValue);
    }

    [Fact]
    public async Task WalkAsync_UnchangedRootScalar_ProducesEmptyFieldDiff()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = SystemConfigurationRoot();
        var definition = MakeDefinition("systemconfiguration", "id", ["status"]);
        var json = Envelope($$"""[{ "ciId": "{{ActiveCiId}}", "confirmationStatus": "active" }]""");

        var result = await ImportNodeWalker.WalkAsync(conn, definition, root, json, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(ImportRowStatus.Accepted, row.Status);
        Assert.Empty(row.Fields);
    }

    // ── ImportEnvelope / schemaVersion gate (Open Decision #14) ──────────────────

    [Fact]
    public async Task WalkAsync_BareArrayWithNoEnvelope_ThrowsValidationException()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = SystemConfigurationRoot();
        var definition = MakeDefinition("systemconfiguration", "id", ["status"]);
        // No longer accepted as of Open Decision #14 — every inbound file must be an ImportEnvelope object.
        var json = $$"""[{ "ciId": "{{ActiveCiId}}", "confirmationStatus": "confirmed" }]""";

        await Assert.ThrowsAsync<ImportValidationException>(() =>
            ImportNodeWalker.WalkAsync(conn, definition, root, json, CancellationToken.None)
        );
    }

    [Fact]
    public async Task WalkAsync_MissingSchemaVersion_ThrowsValidationException()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = SystemConfigurationRoot();
        var definition = MakeDefinition("systemconfiguration", "id", ["status"]);
        var json = $$"""{ "records": [{ "ciId": "{{ActiveCiId}}", "confirmationStatus": "confirmed" }] }""";

        await Assert.ThrowsAsync<ImportValidationException>(() =>
            ImportNodeWalker.WalkAsync(conn, definition, root, json, CancellationToken.None)
        );
    }

    [Fact]
    public async Task WalkAsync_UnrecognizedSchemaVersion_ThrowsValidationException()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = SystemConfigurationRoot();
        var definition = MakeDefinition("systemconfiguration", "id", ["status"]);
        var json = $$"""
            { "schemaVersion": "99", "records": [{ "ciId": "{{ActiveCiId}}", "confirmationStatus": "confirmed" }] }
            """;

        await Assert.ThrowsAsync<ImportValidationException>(() =>
            ImportNodeWalker.WalkAsync(conn, definition, root, json, CancellationToken.None)
        );
    }

    // ── Root correlation-key mismatch ────────────────────────────────────────────

    [Fact]
    public async Task WalkAsync_UnmatchedCorrelationKey_RejectPolicy_RowIsRejected()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = SystemConfigurationRoot();
        var definition = MakeDefinition("systemconfiguration", "id", ["status"], UnmatchedRootPolicy.Reject);
        var json = Envelope($$"""[{ "ciId": "{{UnknownCiId}}", "confirmationStatus": "confirmed" }]""");

        var result = await ImportNodeWalker.WalkAsync(conn, definition, root, json, CancellationToken.None);

        Assert.Equal(0, result.AcceptedCount);
        Assert.Equal(1, result.RejectedCount);
        var row = Assert.Single(result.Rows);
        Assert.Equal(ImportRowStatus.Rejected, row.Status);
        Assert.NotNull(row.RejectReason);
    }

    [Fact]
    public async Task WalkAsync_UnmatchedCorrelationKey_QuarantinePolicy_RowIsQuarantined()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = SystemConfigurationRoot();
        var definition = MakeDefinition("systemconfiguration", "id", ["status"], UnmatchedRootPolicy.Quarantine);
        var json = Envelope($$"""[{ "ciId": "{{UnknownCiId}}", "confirmationStatus": "confirmed" }]""");

        var result = await ImportNodeWalker.WalkAsync(conn, definition, root, json, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(ImportRowStatus.Quarantined, row.Status);
    }

    // ── Column-scope enforcement ──────────────────────────────────────────────────

    [Fact]
    public async Task WalkAsync_TargetColumnOutsideAllowedWritableColumns_ThrowsValidationException()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = SystemConfigurationRoot();
        // "status" is written by the tree but deliberately left off the allowlist.
        var definition = MakeDefinition("systemconfiguration", "id", []);
        var json = Envelope($$"""[{ "ciId": "{{ActiveCiId}}", "confirmationStatus": "confirmed" }]""");

        await Assert.ThrowsAsync<ImportValidationException>(() =>
            ImportNodeWalker.WalkAsync(conn, definition, root, json, CancellationToken.None)
        );
    }

    // ── Object-kind child (maintenance_plan) ─────────────────────────────────────

    [Fact]
    public async Task WalkAsync_ObjectChildMatched_ProducesChildFieldDiff()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = SystemConfigurationRoot(MaintenancePlanChild());
        var definition = MakeDefinition("systemconfiguration", "id", ["status", "allocation_chart_ref"]);
        var json = Envelope(
            $$"""[{ "ciId": "{{ActiveCiId}}", "confirmationStatus": "active", "maintenancePlan": { "allocationChartRef": "AC-2024-099" } }]"""
        );

        var result = await ImportNodeWalker.WalkAsync(conn, definition, root, json, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(ImportRowStatus.Accepted, row.Status);
        var child = Assert.Single(row.Children);
        Assert.True(child.Matched);
        var field = Assert.Single(child.Fields);
        Assert.Equal("allocation_chart_ref", field.Column);
        Assert.Equal("AC-2024-011", field.OldValue);
        Assert.Equal("AC-2024-099", field.NewValue);
    }

    [Fact]
    public async Task WalkAsync_ObjectChildUnresolvedJoinKey_RejectedWithSpecificReason()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = SystemConfigurationRoot(MaintenancePlanChild());
        var definition = MakeDefinition("systemconfiguration", "id", ["status", "allocation_chart_ref"]);
        var json = Envelope(
            $$"""[{ "ciId": "{{DecommissionedCiId}}", "confirmationStatus": "decommissioned", "maintenancePlan": { "allocationChartRef": "AC-2024-099" } }]"""
        );

        var result = await ImportNodeWalker.WalkAsync(conn, definition, root, json, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(ImportRowStatus.Accepted, row.Status); // the root row itself still matched fine
        var child = Assert.Single(row.Children);
        Assert.False(child.Matched);
        Assert.Contains("maintenance_plan", child.RejectReason);
        Assert.Empty(child.Fields);
    }

    [Fact]
    public async Task WalkAsync_ChildOmittedFromPayload_IsNotReportedAtAll()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = SystemConfigurationRoot(MaintenancePlanChild());
        var definition = MakeDefinition("systemconfiguration", "id", ["status", "allocation_chart_ref"]);
        var json = Envelope($$"""[{ "ciId": "{{ActiveCiId}}", "confirmationStatus": "active" }]""");

        var result = await ImportNodeWalker.WalkAsync(conn, definition, root, json, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Empty(row.Children);
    }

    // ── Array-kind child (manufacturer_address) ──────────────────────────────────

    [Fact]
    public async Task WalkAsync_ArrayChildWithMatchingRows_IsMarkedMatched()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = ManufacturerRoot(AddressesChild());
        var definition = MakeDefinition("manufacturer", "id", ["city"]);
        var json = Envelope($$"""[{ "manufacturerId": "{{AcmeManufacturerId}}", "addresses": [{ "city": "Austin" }] }]""");

        var result = await ImportNodeWalker.WalkAsync(conn, definition, root, json, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(ImportRowStatus.Accepted, row.Status);
        var child = Assert.Single(row.Children);
        Assert.True(child.Matched);
    }

    [Fact]
    public async Task WalkAsync_ArrayChildWithNoMatchingRows_IsRejectedPerOnMissingChild()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        var root = ManufacturerRoot(AddressesChild());
        var definition = MakeDefinition("manufacturer", "id", ["city"]);
        var json = Envelope(
            $$"""[{ "manufacturerId": "{{NorthbridgeManufacturerId}}", "addresses": [{ "city": "Somewhere" }] }]"""
        );

        var result = await ImportNodeWalker.WalkAsync(conn, definition, root, json, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        var child = Assert.Single(row.Children);
        Assert.False(child.Matched);
        Assert.Contains("manufacturer_address", child.RejectReason);
    }

    // ── No writes ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WalkAsync_UnderReadOnlyTransaction_NeverAttemptsAWrite()
    {
        await using var conn = await TryOpenAsync();
        if (conn is null)
            return;

        // If ImportNodeWalker ever issued anything but a SELECT, Postgres itself would reject it here with
        // "cannot execute ... in a read-only transaction" — a stronger guarantee than asserting on our own code.
        await using var tx = await conn.BeginTransactionAsync();
        await using (var setRo = new NpgsqlCommand("SET TRANSACTION READ ONLY", conn, tx))
            await setRo.ExecuteNonQueryAsync();

        var root = SystemConfigurationRoot(MaintenancePlanChild());
        var definition = MakeDefinition("systemconfiguration", "id", ["status", "allocation_chart_ref"]);
        var json = Envelope(
            $$"""[{ "ciId": "{{ActiveCiId}}", "confirmationStatus": "confirmed", "maintenancePlan": { "allocationChartRef": "AC-2024-099" } }]"""
        );

        var result = await ImportNodeWalker.WalkAsync(conn, definition, root, json, CancellationToken.None);

        Assert.Equal(1, result.AcceptedCount);
        await tx.RollbackAsync();
    }
}
