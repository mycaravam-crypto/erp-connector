using Connector.Core.DynamicExport;

namespace Connector.Core.DynamicImport;

/// <summary>
/// knowledge/pipeline/import-mapping-presets.md §3.1/§3.4 — the subset of a persisted
/// <c>ExportDefinitionEntity</c> (Connector.Infrastructure) that <see cref="ImportMappingSuggestion.SuggestFrom"/>
/// needs. Kept as a small, Core-level shape rather than referencing the EF entity directly: Connector.Core
/// is the innermost layer and has no dependency on Connector.Infrastructure — the same reason
/// <see cref="ExportNode"/>/<see cref="ImportNode"/> themselves are plain records rather than EF entities.
/// The caller (a future Slice 4 API endpoint) loads this from <c>ExportLogDbContext.ExportDefinitions</c>
/// and deserializes <c>RootNode</c> via <see cref="ExportNodeJson"/> first — this type does no I/O of its own.
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
/// <c>ImportEnvelope</c> (§3.3 — null when the sample carries no <c>provenance</c> block at all, which
/// must degrade to no suggestion, never an error) and the top-level JSON key names of one sample record,
/// used only for the best-effort candidate-field scan (§3.4 step 3).
/// </summary>
public sealed record ImportSampleShape(
    string? IntegrationKey,
    int? ContractVersion,
    IReadOnlyCollection<string> RecordKeys
);

/// <summary>
/// One best-effort candidate field offered alongside the deterministic root prefill — unchecked/disabled by
/// default (§3.4 step 3). The operator must explicitly enable it, and it still goes through the existing
/// <c>AllowedWritableColumns</c> schema-aware validator (Open Decision #9) before it can ever be saved as
/// writable; matching a key name back to the same underlying column is a name-collision heuristic, not a
/// proven contract (§0).
/// </summary>
public sealed record ImportMappingCandidateField(string SourceKey, string TargetColumn);

/// <summary>Result of <see cref="ImportMappingSuggestion.SuggestFrom"/>: the deterministic root prefill
/// (§3.4 step 2 — always semantically sound, since both sides are contractually the same field already,
/// Open Decision #4) plus the best-effort candidate list (step 3).</summary>
public sealed record ImportMappingSuggestionResult(
    string RootTable,
    string RootMatchColumn,
    IReadOnlyList<ImportMappingCandidateField> CandidateFields
);

/// <summary>
/// Why <see cref="ImportMappingSuggestion.Evaluate"/> found nothing to suggest — every gate §3.4 step 1
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
/// knowledge/pipeline/import-mapping-presets.md §3.4 — a UI-time authoring convenience that suggests a
/// starting <c>ImportDefinition</c> from a paired, provenance-tagged <c>ExportDefinition</c>. Pure: no I/O,
/// no side effects, unit-testable in complete isolation — the same posture Phase 17 Slice 2's
/// <c>ImportWalkResult</c> shipped with (a diff with zero write capability). Never called by
/// <c>Connector.Infrastructure.ImportNodeWalker</c>/<c>ImportWorker</c> at processing time — per the
/// proposal's Non-Goals (§5), a matched pair only ever saves an operator typing time at authoring time; it
/// is never a trust or routing mechanism.
/// </summary>
public static class ImportMappingSuggestion
{
    /// <summary>
    /// Step 1: finds the (at most one, thanks to the Slice 1 save-time uniqueness constraint) enabled
    /// export among <paramref name="candidates"/> whose (IntegrationKey, ContractVersion) equals
    /// <paramref name="sample"/>'s pair, then builds the suggestion from it (steps 2-4). Returns null,
    /// silently — never an exception — whenever there is nothing to suggest. Thin wrapper over
    /// <see cref="Evaluate"/> that drops the miss reason, kept for the existing unit-test suite and any
    /// caller that only cares about the hit/miss outcome, not why.
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

        // FirstOrDefault rather than Single: the Slice 1 uniqueness constraint means production data never
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
            // Root-level, enabled scalar fields only (step 3) — nested object/array children are never
            // walked, matching v1's AllowedWritableColumns root-only scope (Open Decision #5).
            if (!child.Enabled || child.Kind != ExportNodeKind.ScalarField || child.SourceField is null)
                continue;

            // The correlation key field is already covered by the deterministic RootMatchColumn prefill
            // above ("every OTHER... node", step 3) — it is not offered a second time as a candidate.
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
