using Connector.Api;
using Connector.Api.Endpoints;
using Connector.Core.DynamicExport;
using Connector.Core.DynamicImport;
using Microsoft.EntityFrameworkCore;

namespace Connector.Integration.Tests;

/// <summary>
/// Slice 1 coverage (knowledge/pipeline/import-mapping-presets.md §3.1) for the two save-time guardrails
/// added to <see cref="ExportDefinitionEndpoints.ValidateRequestAsync"/> and
/// <see cref="ImportDefinitionEndpoints.ValidateRequestAsync"/>: IntegrationKey/ContractVersion must be
/// set together or not at all, and at most one *enabled* definition of a given type may ever claim the
/// same (IntegrationKey, ContractVersion) pair. Runs entirely against the in-memory Sqlite
/// <see cref="LocalDb"/> fixture — these checks run before either validator ever touches the ERP
/// connection, so no Postgres testdb is required.
/// </summary>
public sealed class IntegrationKeyValidationTests
{
    // ── Export side ──────────────────────────────────────────────────────────────────────────────────

    private static ExportNode ExportScalar(string targetKey, string sourceField) =>
        new(
            TargetKey: targetKey,
            Kind: ExportNodeKind.ScalarField,
            SourceField: sourceField,
            RelatedTable: null,
            JoinKey: null,
            SourceJoinKey: null,
            Filter: null,
            Mapping: null,
            Children: [],
            Enabled: true
        );

    private static ExportNode ExportRoot(params ExportNode[] children) =>
        new(
            TargetKey: "root",
            Kind: ExportNodeKind.Root,
            SourceField: null,
            RelatedTable: null,
            JoinKey: null,
            SourceJoinKey: null,
            Filter: null,
            Mapping: null,
            Children: children,
            Enabled: true
        );

    private static ExportDefinitionRequest ExportRequest(
        string? integrationKey = null,
        int? contractVersion = null,
        bool isEnabled = true
    ) =>
        new(
            Name: "Test Export Definition",
            Description: null,
            RootTable: "systemconfiguration",
            RootNode: ExportRoot(ExportScalar("id", "id")),
            OutputFormat: "json",
            IsEnabled: isEnabled,
            Schedule: null,
            IntegrationKey: integrationKey,
            ContractVersion: contractVersion
        );

    [Fact]
    public async Task Export_IntegrationKeyWithoutContractVersion_Rejected()
    {
        await using var local = await LocalDb.NewAsync();
        var request = ExportRequest(integrationKey: "ci-confirmation", contractVersion: null);

        var (root, error) = await ExportDefinitionEndpoints.ValidateRequestAsync(
            request,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(root);
        Assert.Contains("set together", error);
    }

    [Fact]
    public async Task Export_ContractVersionWithoutIntegrationKey_Rejected()
    {
        await using var local = await LocalDb.NewAsync();
        var request = ExportRequest(integrationKey: null, contractVersion: 1);

        var (root, error) = await ExportDefinitionEndpoints.ValidateRequestAsync(
            request,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(root);
        Assert.Contains("set together", error);
    }

    [Fact]
    public async Task Export_SecondEnabledDefinitionWithSamePair_Rejected()
    {
        await using var local = await LocalDb.NewAsync();
        var first = ExportRequest(integrationKey: "ci-confirmation", contractVersion: 1);
        var (firstRoot, firstError) = await ExportDefinitionEndpoints.ValidateRequestAsync(
            first,
            local.Db,
            CancellationToken.None
        );
        Assert.Null(firstError);
        local.Db.ExportDefinitions.Add(
            new()
            {
                Name = first.Name,
                RootTable = first.RootTable,
                RootNode = ExportNodeJson.Serialize(firstRoot!),
                OutputFormat = first.OutputFormat,
                IsEnabled = true,
                ConfigVersion = 1,
                CreatedBy = "tester",
                CreatedAt = "now",
                IntegrationKey = first.IntegrationKey,
                ContractVersion = first.ContractVersion,
            }
        );
        await local.Db.SaveChangesAsync();

        var second = ExportRequest(integrationKey: "ci-confirmation", contractVersion: 1);
        var (secondRoot, secondError) = await ExportDefinitionEndpoints.ValidateRequestAsync(
            second,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(secondRoot);
        Assert.Contains("already uses IntegrationKey", secondError);
    }

    [Fact]
    public async Task Export_UpdatingTheSameDefinitionThatOwnsThePair_DoesNotRejectItself()
    {
        await using var local = await LocalDb.NewAsync();
        var request = ExportRequest(integrationKey: "ci-confirmation", contractVersion: 1);
        var (root, _) = await ExportDefinitionEndpoints.ValidateRequestAsync(request, local.Db, CancellationToken.None);
        var entity = new Connector.Infrastructure.ExportDefinitionEntity
        {
            Name = request.Name,
            RootTable = request.RootTable,
            RootNode = ExportNodeJson.Serialize(root!),
            OutputFormat = request.OutputFormat,
            IsEnabled = true,
            ConfigVersion = 1,
            CreatedBy = "tester",
            CreatedAt = "now",
            IntegrationKey = request.IntegrationKey,
            ContractVersion = request.ContractVersion,
        };
        local.Db.ExportDefinitions.Add(entity);
        await local.Db.SaveChangesAsync();

        var (updatedRoot, updateError) = await ExportDefinitionEndpoints.ValidateRequestAsync(
            request,
            local.Db,
            CancellationToken.None,
            excludeId: entity.Id
        );

        Assert.Null(updateError);
        Assert.NotNull(updatedRoot);
    }

    [Fact]
    public async Task Export_SamePairOnADisabledDefinition_DoesNotConflict()
    {
        await using var local = await LocalDb.NewAsync();
        var first = ExportRequest(integrationKey: "ci-confirmation", contractVersion: 1, isEnabled: false);
        var (firstRoot, firstError) = await ExportDefinitionEndpoints.ValidateRequestAsync(
            first,
            local.Db,
            CancellationToken.None
        );
        Assert.Null(firstError);
        local.Db.ExportDefinitions.Add(
            new()
            {
                Name = first.Name,
                RootTable = first.RootTable,
                RootNode = ExportNodeJson.Serialize(firstRoot!),
                OutputFormat = first.OutputFormat,
                IsEnabled = false,
                ConfigVersion = 1,
                CreatedBy = "tester",
                CreatedAt = "now",
                IntegrationKey = first.IntegrationKey,
                ContractVersion = first.ContractVersion,
            }
        );
        await local.Db.SaveChangesAsync();

        var second = ExportRequest(integrationKey: "ci-confirmation", contractVersion: 1);
        var (secondRoot, secondError) = await ExportDefinitionEndpoints.ValidateRequestAsync(
            second,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(secondError);
        Assert.NotNull(secondRoot);
    }

    [Fact]
    public async Task Export_CorrelationKeySourceFieldNotAnIdentifier_Rejected()
    {
        await using var local = await LocalDb.NewAsync();
        var request = ExportRequest() with { CorrelationKeySourceField = "not an identifier" };

        var (root, error) = await ExportDefinitionEndpoints.ValidateRequestAsync(
            request,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(root);
        Assert.Contains("CorrelationKeySourceField", error);
    }

    // ── Import side ──────────────────────────────────────────────────────────────────────────────────

    private static ImportNode ImportScalar(string sourceKey, string targetColumn) =>
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

    private static ImportNode ImportRoot(params ImportNode[] children) =>
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

    // Deliberately never reaches the schema-aware AllowedWritableColumns pass: the IntegrationKey checks
    // added in Slice 1 all return before ValidateRequestAsync ever opens an ERP connection, same ordering
    // ImportDefinitionEndpointsPostgresTests' own OnMissingChildInsert test relies on.
    private static ImportDefinitionRequest ImportRequest(
        string? integrationKey = null,
        int? contractVersion = null,
        bool isEnabled = true
    ) =>
        new(
            Name: "Test Import Definition",
            Description: null,
            RootTable: "systemconfiguration",
            RootMatchColumn: "id",
            RootNode: ImportRoot(ImportScalar("id", "id")),
            AllowedWritableColumns: [],
            UnmatchedRootPolicy: UnmatchedRootPolicy.Reject,
            IsEnabled: isEnabled,
            IntegrationKey: integrationKey,
            ContractVersion: contractVersion
        );

    [Fact]
    public async Task Import_IntegrationKeyWithoutContractVersion_Rejected()
    {
        await using var local = await LocalDb.NewAsync();
        var request = ImportRequest(integrationKey: "ci-confirmation", contractVersion: null);

        var (root, error) = await ImportDefinitionEndpoints.ValidateRequestAsync(
            request,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(root);
        Assert.Contains("set together", error);
    }

    [Fact]
    public async Task Import_ContractVersionBelowOne_Rejected()
    {
        await using var local = await LocalDb.NewAsync();
        var request = ImportRequest(integrationKey: "ci-confirmation", contractVersion: 0);

        var (root, error) = await ImportDefinitionEndpoints.ValidateRequestAsync(
            request,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(root);
        Assert.Contains("positive integer", error);
    }

    [Fact]
    public async Task Import_SecondEnabledDefinitionWithSamePair_Rejected()
    {
        await using var local = await LocalDb.NewAsync();
        // Inserted directly rather than routed through ValidateRequestAsync: the full validator's
        // schema-aware AllowedWritableColumns pass (Open Decision #9) needs a real Postgres connection,
        // which this test — unlike ImportDefinitionEndpointsPostgresTests — deliberately doesn't depend
        // on. `first`'s tree is already well-formed via the ImportRoot/ImportScalar helpers above.
        var first = ImportRequest(integrationKey: "ci-confirmation", contractVersion: 1);
        local.Db.ImportDefinitions.Add(
            new()
            {
                Name = first.Name,
                RootTable = first.RootTable,
                RootMatchColumn = first.RootMatchColumn,
                RootNode = ImportNodeJson.Serialize(first.RootNode),
                UnmatchedRootPolicy = first.UnmatchedRootPolicy,
                IsEnabled = true,
                ConfigVersion = 1,
                CreatedBy = "tester",
                CreatedAt = "now",
                IntegrationKey = first.IntegrationKey,
                ContractVersion = first.ContractVersion,
            }
        );
        await local.Db.SaveChangesAsync();

        var second = ImportRequest(integrationKey: "ci-confirmation", contractVersion: 1);
        var (secondRoot, secondError) = await ImportDefinitionEndpoints.ValidateRequestAsync(
            second,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(secondRoot);
        Assert.Contains("already uses IntegrationKey", secondError);
    }

    [Fact]
    public async Task Import_DifferentContractVersionOfSameKey_DoesNotConflict()
    {
        // Unlike the sibling "rejected" test above, a *successful* second validation here runs all the
        // way through to ImportDefinitionEndpoints.ValidateRequestAsync's schema-aware
        // AllowedWritableColumns pass (Open Decision #9), which needs a real Postgres connection — same
        // "no-op instead of fail" convention as ImportDefinitionEndpointsPostgresTests.
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        await using var local = await LocalDb.NewAsync();
        // See the sibling test above for why `first` is inserted directly instead of via ValidateRequestAsync.
        var first = ImportRequest(integrationKey: "ci-confirmation", contractVersion: 1);
        local.Db.ImportDefinitions.Add(
            new()
            {
                Name = first.Name,
                RootTable = first.RootTable,
                RootMatchColumn = first.RootMatchColumn,
                RootNode = ImportNodeJson.Serialize(first.RootNode),
                UnmatchedRootPolicy = first.UnmatchedRootPolicy,
                IsEnabled = true,
                ConfigVersion = 1,
                CreatedBy = "tester",
                CreatedAt = "now",
                IntegrationKey = first.IntegrationKey,
                ContractVersion = first.ContractVersion,
            }
        );
        await local.Db.SaveChangesAsync();

        var second = ImportRequest(integrationKey: "ci-confirmation", contractVersion: 2);
        var (secondRoot, secondError) = await ImportDefinitionEndpoints.ValidateRequestAsync(
            second,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(secondError);
        Assert.NotNull(secondRoot);
    }
}
