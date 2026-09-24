using Connector.Core.DynamicExport;

namespace Connector.Core.DynamicImport;

/// <summary>
/// The subset of a persisted <c>ExportDefinitionEntity</c> (Connector.Infrastructure) that
/// <see cref="ImportMappingSuggestion.SuggestFrom"/> needs. A plain Core-level record because Connector.Core
/// has no dependency on Connector.Infrastructure. The caller loads it from
/// <c>ExportLogDbContext.ExportDefinitions</c> and deserializes <c>RootNode</c> via <see cref="ExportNodeJson"/>
/// first — this type does no I/O of its own.
/// </summary>
public sealed record ExportDefinitionShape(
    bool IsEnabled,
    string? IntegrationKey,
    int? ContractVersion,
    string RootTable,
    string? CorrelationKeySourceField,
    ExportNode RootNode
);

/// <summary>
/// The inbound sample a suggestion is built from: the provenance pair read off the sample
/// <c>ImportEnvelope</c> (null when the sample carries no <c>provenance</c> block, which yields no
/// suggestion, never an error) and the top-level JSON key names of one sample record, used only for the
/// best-effort candidate-field scan.
/// </summary>
public sealed record ImportSampleShape(
    string? IntegrationKey,
    int? ContractVersion,
    IReadOnlyCollection<string> RecordKeys
);

/// <summary>
/// One best-effort candidate field offered alongside the deterministic root prefill — unchecked/disabled by
/// default. The operator must explicitly enable it, and it still goes through the schema-aware
/// <c>AllowedWritableColumns</c> validator before it can be saved as writable; matching a key name back to
/// the same underlying column is a name-based heuristic, not a proven contract.
/// </summary>
public sealed record ImportMappingCandidateField(string SourceKey, string TargetColumn);

/// <summary>Result of <see cref="ImportMappingSuggestion.SuggestFrom"/>: the deterministic root prefill
/// (the export's correlation key, so both sides are the same field by contract) plus the best-effort
/// candidate list.</summary>
public sealed record ImportMappingSuggestionResult(
    string RootTable,
    string RootMatchColumn,
    IReadOnlyList<ImportMappingCandidateField> CandidateFields
);

/// <summary>
/// Why <see cref="ImportMappingSuggestion.Evaluate"/> found nothing to suggest — every gate the lookup
/// can silently fail on, named so the API layer (which has the entity `Name`s <see cref="Evaluate"/>
/// itself never sees, per Core's no-Infrastructure-dependency rule) can turn a miss into an operator-facing
/// explanation instead of a flat "no match." Ordered by how far the sample got.
/// </summary>
public enum ImportMappingSuggestionMiss
{
    /// <summary>The sample carries no <c>provenance.integrationKey</c> at all.</summary>
    NoProvenance,

    /// <summary>No candidate — enabled or not — claims this (IntegrationKey, ContractVersion) pair.</summary>
    NoDefinitionForPair,

    /// <summary>A candidate claims the pair, but every one that does is disabled.</summary>
    DefinitionDisabled,

    /// <summary>An enabled candidate claims the pair but has no CorrelationKeySourceField set, so there is
    /// no deterministic RootMatchColumn to prefill.</summary>
    MissingCorrelationKeySourceField,
}

/// <summary>Full outcome of <see cref="ImportMappingSuggestion.Evaluate"/>: exactly one of
/// <see cref="Result"/>/<see cref="Miss"/> is set.</summary>
public sealed record ImportMappingSuggestionOutcome(
    ImportMappingSuggestionResult? Result,
    ImportMappingSuggestionMiss? Miss
);

/// <summary>
/// An authoring convenience that suggests a starting <c>ImportDefinition</c> from a paired,
/// provenance-tagged <c>ExportDefinition</c> (see knowledge/pipeline/import-mapping-presets.md). Pure: no
/// I/O, no side effects. Never called by <c>Connector.Infrastructure.ImportNodeWalker</c>/<c>ImportWorker</c>
/// at processing time — a matched pair only saves the operator typing at authoring time; it is never a
/// trust or routing mechanism.
/// </summary>
public static class ImportMappingSuggestion
{
    /// <summary>
    /// Finds the (at most one, per the save-time uniqueness constraint) enabled export among
    /// <paramref name="candidates"/> whose (IntegrationKey, ContractVersion) equals <paramref name="sample"/>'s
    /// pair, then builds the suggestion from it. Returns null — never throws — whenever there is nothing to
    /// suggest. Thin wrapper over <see cref="Evaluate"/> that drops the miss reason.
    /// </summary>
    public static ImportMappingSuggestionResult? SuggestFrom(
        IReadOnlyList<ExportDefinitionShape> candidates,
        ImportSampleShape sample
    ) => Evaluate(candidates, sample).Result;

    /// <summary>
    /// Same lookup as <see cref="SuggestFrom"/>, but on a miss names *which* gate stopped it
    /// (<see cref="ImportMappingSuggestionMiss"/>) instead of collapsing every reason into a bare null —
    /// the API layer uses this to turn "no match found" into an operator-facing explanation (a candidate
    /// exists but is disabled, or exists but has no CorrelationKeySourceField, vs. no candidate at all).
    /// </summary>
    public static ImportMappingSuggestionOutcome Evaluate(
        IReadOnlyList<ExportDefinitionShape> candidates,
        ImportSampleShape sample
    )
    {
        if (sample.IntegrationKey is null)
            return new ImportMappingSuggestionOutcome(null, ImportMappingSuggestionMiss.NoProvenance);

        // Every candidate claiming this pair, enabled or not — lets a disabled match be reported
        // specifically rather than folded into "nothing claims this pair at all."
        var pairMatches = candidates
            .Where(c => c.IntegrationKey == sample.IntegrationKey && c.ContractVersion == sample.ContractVersion)
            .ToList();
        if (pairMatches.Count == 0)
            return new ImportMappingSuggestionOutcome(null, ImportMappingSuggestionMiss.NoDefinitionForPair);

        // FirstOrDefault rather than Single: the save-time uniqueness constraint means production data never
        // has more than one *enabled* match for a given pair, but this stays safe — never throws — even if
        // a caller ever hands it a candidate list that violates that invariant.
        var matched = pairMatches.FirstOrDefault(c => c.IsEnabled);
        if (matched is null)
            return new ImportMappingSuggestionOutcome(null, ImportMappingSuggestionMiss.DefinitionDisabled);
        if (matched.CorrelationKeySourceField is null)
            return new ImportMappingSuggestionOutcome(
                null,
                ImportMappingSuggestionMiss.MissingCorrelationKeySourceField
            );

        var candidateFields = new List<ImportMappingCandidateField>();
        foreach (var child in matched.RootNode.Children)
        {
            // Root-level, enabled scalar fields only — nested object/array children are never walked,
            // matching the root-only AllowedWritableColumns scope.
            if (!child.Enabled || child.Kind != ExportNodeKind.ScalarField || child.SourceField is null)
                continue;

            // The correlation key field is already covered by the deterministic RootMatchColumn prefill
            // above — it is not offered a second time as a candidate.
            if (child.SourceField == matched.CorrelationKeySourceField)
                continue;

            if (sample.RecordKeys.Contains(child.TargetKey))
                candidateFields.Add(new ImportMappingCandidateField(child.TargetKey, child.SourceField));
        }

        var result = new ImportMappingSuggestionResult(
            matched.RootTable,
            matched.CorrelationKeySourceField,
            candidateFields
        );
        return new ImportMappingSuggestionOutcome(result, null);
    }
}
