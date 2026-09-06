using Connector.Api;
using Connector.Api.Endpoints;
using Connector.Core.DynamicExport;
using Connector.Infrastructure;

namespace Connector.Integration.Tests;

/// <summary>
/// Slice 4 coverage (knowledge/pipeline/import-mapping-presets.md §3.4/§4) for
/// <see cref="ImportDefinitionEndpoints.BuildSuggestionAsync"/> — the "Create from export" API the New
/// Import Definition flow calls. Runs entirely against the in-memory Sqlite <see cref="LocalDb"/> fixture,
/// same as <see cref="IntegrationKeyValidationTests"/>: this method never opens an ERP connection, so no
/// Postgres testdb is required.
/// </summary>
public sealed class ImportMappingSuggestionEndpointTests
{
    private static ExportNode ExportScalar(string targetKey, string sourceField, bool enabled = true) =>
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
            Enabled: enabled
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

    private static async Task<ExportDefinitionEntity> SeedExportAsync(
        LocalDb local,
        string integrationKey,
        int contractVersion,
        ExportNode root,
        string? correlationKeySourceField = "guid",
        bool isEnabled = true
    )
    {
        var entity = new ExportDefinitionEntity
        {
            Name = "CI confirmation export",
            RootTable = "masterdata",
            RootNode = ExportNodeJson.Serialize(root),
            OutputFormat = "json",
            IsEnabled = isEnabled,
            ConfigVersion = 1,
            CreatedBy = "tester",
            CreatedAt = "now",
            IntegrationKey = integrationKey,
            ContractVersion = contractVersion,
            CorrelationKeySourceField = correlationKeySourceField,
        };
        local.Db.ExportDefinitions.Add(entity);
        await local.Db.SaveChangesAsync();
        return entity;
    }

    private const string SampleEnvelope = """
        {"schemaVersion":"1","provenance":{"integrationKey":"ci-confirmation","contractVersion":1},"records":[{"guidField":"abc","status":"confirmed"}]}
        """;

    [Fact]
    public async Task ExactMatch_ReturnsDeterministicPrefillAndCandidateFields()
    {
        await using var local = await LocalDb.NewAsync();
        var root = ExportRoot(ExportScalar("guidField", "guid"), ExportScalar("status", "status"));
        var export = await SeedExportAsync(local, "ci-confirmation", 1, root);

        var suggestion = await ImportDefinitionEndpoints.BuildSuggestionAsync(
            SampleEnvelope,
            local.Db,
            CancellationToken.None
        );

        Assert.NotNull(suggestion);
        Assert.Equal(export.Id, suggestion.ExportDefinitionId);
        Assert.Equal("CI confirmation export", suggestion.ExportDefinitionName);
        Assert.Equal("ci-confirmation", suggestion.IntegrationKey);
        Assert.Equal(1, suggestion.ContractVersion);
        Assert.Equal("masterdata", suggestion.RootTable);
        Assert.Equal("guid", suggestion.RootMatchColumn);
        // Resolved from the export's own TargetKey for the correlation field, not assumed equal to the
        // column name — the export's TargetKey ("guidField") differs from its SourceField ("guid") here.
        Assert.Equal("guidField", suggestion.RootMatchSourceKey);
        var candidate = Assert.Single(suggestion.CandidateFields);
        Assert.Equal("status", candidate.SourceKey);
        Assert.Equal("status", candidate.TargetColumn);
    }

    [Fact]
    public async Task NoProvenanceBlock_ReturnsNullSilently()
    {
        await using var local = await LocalDb.NewAsync();
        await SeedExportAsync(local, "ci-confirmation", 1, ExportRoot(ExportScalar("guid", "guid")));

        var suggestion = await ImportDefinitionEndpoints.BuildSuggestionAsync(
            """{"schemaVersion":"1","records":[{"guid":"abc"}]}""",
            local.Db,
            CancellationToken.None
        );

        Assert.Null(suggestion);
    }

    [Fact]
    public async Task MalformedJson_ReturnsNullSilentlyRatherThanThrowing()
    {
        await using var local = await LocalDb.NewAsync();

        var suggestion = await ImportDefinitionEndpoints.BuildSuggestionAsync(
            "not json at all {{{",
            local.Db,
            CancellationToken.None
        );

        Assert.Null(suggestion);
    }

    [Fact]
    public async Task NoMatchingExport_ReturnsNull()
    {
        await using var local = await LocalDb.NewAsync();
        await SeedExportAsync(local, "some-other-key", 1, ExportRoot(ExportScalar("guid", "guid")));

        var suggestion = await ImportDefinitionEndpoints.BuildSuggestionAsync(
            SampleEnvelope,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(suggestion);
    }

    [Fact]
    public async Task DisabledExport_IsNeverSuggested()
    {
        await using var local = await LocalDb.NewAsync();
        await SeedExportAsync(
            local,
            "ci-confirmation",
            1,
            ExportRoot(ExportScalar("guidField", "guid")),
            isEnabled: false
        );

        var suggestion = await ImportDefinitionEndpoints.BuildSuggestionAsync(
            SampleEnvelope,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(suggestion);
    }

    [Fact]
    public async Task ExportWithNoCorrelationKeySourceField_ReturnsNull()
    {
        await using var local = await LocalDb.NewAsync();
        await SeedExportAsync(
            local,
            "ci-confirmation",
            1,
            ExportRoot(ExportScalar("guidField", "guid")),
            correlationKeySourceField: null
        );

        var suggestion = await ImportDefinitionEndpoints.BuildSuggestionAsync(
            SampleEnvelope,
            local.Db,
            CancellationToken.None
        );

        Assert.Null(suggestion);
    }

    [Fact]
    public async Task DisabledCandidateExportField_IsNeverOffered()
    {
        await using var local = await LocalDb.NewAsync();
        await SeedExportAsync(
            local,
            "ci-confirmation",
            1,
            ExportRoot(ExportScalar("guidField", "guid"), ExportScalar("status", "status", enabled: false))
        );

        var suggestion = await ImportDefinitionEndpoints.BuildSuggestionAsync(
            SampleEnvelope,
            local.Db,
            CancellationToken.None
        );

        Assert.NotNull(suggestion);
        Assert.Empty(suggestion.CandidateFields);
    }
}
