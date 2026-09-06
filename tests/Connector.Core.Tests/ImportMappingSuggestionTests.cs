using Connector.Core.DynamicExport;
using Connector.Core.DynamicImport;

namespace Connector.Core.Tests;

/// <summary>
/// Slice 3 coverage (knowledge/pipeline/import-mapping-presets.md §3.4) for
/// <see cref="ImportMappingSuggestion.SuggestFrom"/> — a pure function, so every case here is a plain
/// in-memory value, no database or JSON parsing involved. Mirrors <see cref="ImportPlanBuilderTests"/>'s
/// posture of testing a diff/plan-shaping type in complete isolation from I/O.
/// </summary>
public sealed class ImportMappingSuggestionTests
{
    private static ExportNode Scalar(string targetKey, string sourceField, bool enabled = true) =>
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

    private static ExportNode Object(string targetKey, params ExportNode[] children) =>
        new(
            TargetKey: targetKey,
            Kind: ExportNodeKind.Object,
            SourceField: null,
            RelatedTable: "related_table",
            JoinKey: "id",
            SourceJoinKey: "related_id",
            Filter: null,
            Mapping: null,
            Children: children,
            Enabled: true
        );

    private static ExportNode Root(params ExportNode[] children) =>
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

    private static ExportDefinitionShape Export(
        ExportNode rootNode,
        bool isEnabled = true,
        string? integrationKey = "ci-confirmation",
        int? contractVersion = 1,
        string rootTable = "system_configuration",
        string? correlationKeySourceField = "guid"
    ) => new(isEnabled, integrationKey, contractVersion, rootTable, correlationKeySourceField, rootNode);

    private static ImportSampleShape Sample(
        string? integrationKey = "ci-confirmation",
        int? contractVersion = 1,
        params string[] recordKeys
    ) => new(integrationKey, contractVersion, recordKeys);

    // Convenience overload for the common case (default provenance pair, explicit sample record keys) —
    // avoids Sonar S3878 (array creation for a params parameter) at call sites, which fires even for a
    // named `recordKeys: [...]` argument on the overload above.
    private static ImportSampleShape SampleWithKeys(params string[] recordKeys) => Sample(recordKeys: recordKeys);

    [Fact]
    public void SuggestFrom_ExactMatch_PrefillsRootTableAndMatchColumn()
    {
        var export = Export(Root(Scalar("guid", "guid")));

        var result = ImportMappingSuggestion.SuggestFrom([export], SampleWithKeys("guid"));

        Assert.NotNull(result);
        Assert.Equal("system_configuration", result!.RootTable);
        Assert.Equal("guid", result.RootMatchColumn);
    }

    [Fact]
    public void SuggestFrom_NoProvenanceOnSample_ReturnsNullSilently()
    {
        var export = Export(Root(Scalar("guid", "guid")));

        var result = ImportMappingSuggestion.SuggestFrom([export], Sample(integrationKey: null, contractVersion: null));

        Assert.Null(result);
    }

    [Fact]
    public void SuggestFrom_DifferentIntegrationKey_NoMatch()
    {
        var export = Export(Root(Scalar("guid", "guid")), integrationKey: "other-exchange");

        var result = ImportMappingSuggestion.SuggestFrom([export], Sample());

        Assert.Null(result);
    }

    [Fact]
    public void SuggestFrom_DifferentContractVersion_NoMatch()
    {
        var export = Export(Root(Scalar("guid", "guid")), contractVersion: 2);

        var result = ImportMappingSuggestion.SuggestFrom([export], Sample(contractVersion: 1));

        Assert.Null(result);
    }

    [Fact]
    public void SuggestFrom_MatchingPairButExportDisabled_NoMatch()
    {
        var export = Export(Root(Scalar("guid", "guid")), isEnabled: false);

        var result = ImportMappingSuggestion.SuggestFrom([export], Sample());

        Assert.Null(result);
    }

    [Fact]
    public void SuggestFrom_MatchedExportHasNoCorrelationKeySourceField_NoSuggestion()
    {
        // Slice 1 never required CorrelationKeySourceField to be set alongside IntegrationKey — without it
        // there is no deterministic RootMatchColumn to prefill, so this must degrade to no suggestion.
        var export = Export(Root(Scalar("guid", "guid")), correlationKeySourceField: null);

        var result = ImportMappingSuggestion.SuggestFrom([export], Sample());

        Assert.Null(result);
    }

    [Fact]
    public void SuggestFrom_NoCandidateSharesThePair_ReturnsNull()
    {
        var export = Export(Root(Scalar("guid", "guid")));

        var result = ImportMappingSuggestion.SuggestFrom([export], Sample(integrationKey: "unrelated"));

        Assert.Null(result);
    }

    [Fact]
    public void SuggestFrom_AmbiguousCandidates_NeverThrows_PicksOneDeterministically()
    {
        // Simulates a hypothetical Slice 1 uniqueness-constraint violation: SuggestFrom itself must stay
        // safe (never throw) even if handed a candidate list with more than one match for the same pair.
        var first = Export(Root(Scalar("guid", "guid")), rootTable: "table_one");
        var second = Export(Root(Scalar("guid", "guid")), rootTable: "table_two");

        var result = ImportMappingSuggestion.SuggestFrom([first, second], Sample());

        Assert.NotNull(result);
        Assert.Equal("table_one", result!.RootTable);
    }

    [Fact]
    public void SuggestFrom_OtherEnabledScalarFieldNameMatchingSampleKey_OfferedAsCandidate()
    {
        var export = Export(Root(Scalar("guid", "guid"), Scalar("confirmationStatus", "status")));

        var result = ImportMappingSuggestion.SuggestFrom([export], SampleWithKeys("guid", "confirmationStatus"));

        Assert.NotNull(result);
        var candidate = Assert.Single(result!.CandidateFields);
        Assert.Equal("confirmationStatus", candidate.SourceKey);
        Assert.Equal("status", candidate.TargetColumn);
    }

    [Fact]
    public void SuggestFrom_FieldNameNotPresentInSample_NotOfferedAsCandidate()
    {
        var export = Export(Root(Scalar("guid", "guid"), Scalar("confirmationStatus", "status")));

        var result = ImportMappingSuggestion.SuggestFrom([export], SampleWithKeys("guid"));

        Assert.NotNull(result);
        Assert.Empty(result!.CandidateFields);
    }

    [Fact]
    public void SuggestFrom_DisabledScalarField_NeverOfferedAsCandidateEvenIfNameMatches()
    {
        var export = Export(Root(Scalar("guid", "guid"), Scalar("confirmationStatus", "status", enabled: false)));

        var result = ImportMappingSuggestion.SuggestFrom([export], SampleWithKeys("guid", "confirmationStatus"));

        Assert.NotNull(result);
        Assert.Empty(result!.CandidateFields);
    }

    [Fact]
    public void SuggestFrom_CorrelationKeyFieldItself_NotDuplicatedAsACandidate()
    {
        var export = Export(Root(Scalar("guid", "guid")));

        var result = ImportMappingSuggestion.SuggestFrom([export], SampleWithKeys("guid"));

        Assert.NotNull(result);
        Assert.Empty(result!.CandidateFields);
    }

    [Fact]
    public void SuggestFrom_NestedObjectChildren_NeverWalkedForCandidates()
    {
        var export = Export(Root(Scalar("guid", "guid"), Object("nested", Scalar("innerStatus", "inner_status"))));

        var result = ImportMappingSuggestion.SuggestFrom([export], SampleWithKeys("guid", "innerStatus"));

        Assert.NotNull(result);
        Assert.Empty(result!.CandidateFields);
    }
}
