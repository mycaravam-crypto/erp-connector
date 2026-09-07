using System.Text.Json;
using System.Text.Json.Nodes;
using Connector.Core.DynamicExport;
using Connector.Core.DynamicImport;
using Connector.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Connector.Api.Endpoints;

// Entity/DTO mapping for ImportDefinitionEndpoints, plus the "create from export" mapping-suggestion
// lookup (knowledge/pipeline/import-mapping-presets.md §3.4/§4).
static partial class ImportDefinitionEndpoints
{
    private static ImportDefinitionDto ToDto(ImportDefinitionEntity e) =>
        new(
            e.Id,
            e.Name,
            e.Description,
            e.RootTable,
            e.RootMatchColumn,
            ImportNodeJson.Deserialize(e.RootNode)!,
            JsonSerializer.Deserialize<List<string>>(e.AllowedWritableColumns) ?? [],
            e.UnmatchedRootPolicy,
            e.IsEnabled,
            e.ConfigVersion,
            e.CreatedBy,
            e.CreatedAt,
            e.UpdatedBy,
            e.UpdatedAt,
            e.IntegrationKey,
            e.ContractVersion
        );

    private static ImportDefinitionSummaryDto ToSummaryDto(ImportDefinitionEntity e) =>
        new(
            e.Id,
            e.Name,
            e.Description,
            e.RootTable,
            e.UnmatchedRootPolicy,
            e.IsEnabled,
            e.ConfigVersion,
            e.CreatedBy,
            e.CreatedAt,
            e.UpdatedBy,
            e.UpdatedAt
        );

    /// <summary>
    /// Loads every provenance-tagged <see cref="ExportDefinitionEntity"/> — enabled or not, so a disabled
    /// match can be named specifically rather than folded into "nothing matches" — extracts the sample's
    /// provenance pair and first record's field names from <paramref name="inboundJson"/>, and delegates to
    /// <see cref="ImportMappingSuggestion.Evaluate"/>. `internal` (rather than `private`) so a unit test can
    /// exercise it directly against the in-memory Sqlite <c>LocalDb</c> fixture — this never opens an ERP
    /// connection, unlike <see cref="ValidateRequestAsync"/>, so it needs no Postgres testdb.
    /// </summary>
    internal static async Task<ImportMappingSuggestionCheckResult> BuildSuggestionAsync(
        string inboundJson,
        ExportLogDbContext db,
        CancellationToken ct
    )
    {
        var sample = TryParseSample(inboundJson);
        if (sample is null)
            return new ImportMappingSuggestionCheckResult(
                null,
                "This sample has no readable provenance.integrationKey. Paste the complete exported JSON "
                    + "file — including its top-level \"provenance\" block — not just an excerpt or the "
                    + "records array alone."
            );

        var candidates = await db.ExportDefinitions.Where(e => e.IntegrationKey != null).ToListAsync(ct);

        var shapes = new List<(ExportDefinitionEntity Entity, ExportDefinitionShape Shape)>();
        foreach (var entity in candidates)
        {
            var rootNode = ExportNodeJson.Deserialize(entity.RootNode);
            if (rootNode is null)
                continue;
            shapes.Add(
                (
                    entity,
                    new ExportDefinitionShape(
                        entity.IsEnabled,
                        entity.IntegrationKey,
                        entity.ContractVersion,
                        entity.RootTable,
                        entity.CorrelationKeySourceField,
                        rootNode
                    )
                )
            );
        }

        var outcome = ImportMappingSuggestion.Evaluate([.. shapes.Select(s => s.Shape)], sample);

        if (outcome.Result is null)
        {
            // The same (IntegrationKey, ContractVersion) pair Evaluate looked for — present whenever the
            // miss is DefinitionDisabled/MissingCorrelationKeySourceField (Evaluate only returns those once
            // it has found a claiming shape), absent for NoDefinitionForPair.
            var pairMatch = shapes.FirstOrDefault(s =>
                s.Entity.IntegrationKey == sample.IntegrationKey && s.Entity.ContractVersion == sample.ContractVersion
            );

            var pairMatchName = pairMatch.Entity?.Name ?? "that export";
            var reason = outcome.Miss switch
            {
                ImportMappingSuggestionMiss.NoDefinitionForPair =>
                    $"No export definition is tagged with integration key \"{sample.IntegrationKey}\" "
                        + $"v{sample.ContractVersion?.ToString() ?? "?"}. Check the key and contract version "
                        + "on the export you meant to pair this with.",
                ImportMappingSuggestionMiss.DefinitionDisabled =>
                    $"Export \"{pairMatchName}\" is tagged with this integration key and version, but it's "
                        + "disabled, so it can never be suggested. Enable it to use this pairing.",
                ImportMappingSuggestionMiss.MissingCorrelationKeySourceField =>
                    $"Export \"{pairMatchName}\" matches this integration key and version, but has no "
                        + "Correlation key field set. Add one under that export's \"Integration tagging\" "
                        + "section, then check this sample again.",
                _ => "No matching export found for this sample.",
            };
            return new ImportMappingSuggestionCheckResult(null, reason);
        }

        var result = outcome.Result;

        // Guaranteed to exist: `outcome.Result` is only non-null when Evaluate found a shape built from one
        // of these exact entities, matching the same (IntegrationKey, ContractVersion) pair as `sample`. The
        // Slice 1 uniqueness constraint means there is never more than one enabled one.
        var matched = shapes.First(s =>
            s.Entity.IsEnabled
            && s.Entity.IntegrationKey == sample.IntegrationKey
            && s.Entity.ContractVersion == sample.ContractVersion
        );

        // The deterministic RootMatchColumn prefill is a column name, not the JSON key an inbound file
        // actually carries it under — resolve the matched export's own TargetKey for that field so the
        // suggested ImportNode's SourceKey reads the real inbound key, not a same-name-column guess.
        var matchFieldTargetKey = matched
            .Shape.RootNode.Children.FirstOrDefault(c =>
                c.Enabled
                && c.Kind == ExportNodeKind.ScalarField
                && c.SourceField == matched.Entity.CorrelationKeySourceField
            )
            ?.TargetKey;

        var dto = new ImportMappingSuggestionDto(
            matched.Entity.Id,
            matched.Entity.Name,
            matched.Entity.IntegrationKey!,
            matched.Entity.ContractVersion!.Value,
            result.RootTable,
            result.RootMatchColumn,
            matchFieldTargetKey ?? result.RootMatchColumn,
            [
                .. result.CandidateFields.Select(f => new ImportMappingSuggestionCandidateFieldDto(
                    f.SourceKey,
                    f.TargetColumn
                )),
            ]
        );
        return new ImportMappingSuggestionCheckResult(dto, null);
    }

    /// <summary>
    /// Parses <paramref name="inboundJson"/> as an <c>ImportEnvelope</c> and pulls out just what
    /// <see cref="ImportMappingSuggestion.SuggestFrom"/> needs: the <c>provenance</c> pair and the first
    /// record's top-level key names. Never throws — anything short of a well-formed envelope carrying a
    /// <c>provenance.integrationKey</c> degrades to <c>null</c> ("nothing to suggest"), matching the
    /// pure function's own silent-degrade contract (knowledge/pipeline/import-mapping-presets.md §3.4 step
    /// 1). Deliberately independent of <c>ImportNodeWalker.ParseRecords</c> — that parser must never read
    /// <c>provenance</c> at all (Slice 3's core guardrail), so this stays a separate, UI-only reader.
    /// </summary>
    private static ImportSampleShape? TryParseSample(string inboundJson)
    {
        try
        {
            if (JsonNode.Parse(inboundJson) is not JsonObject envelope)
                return null;
            if (envelope["provenance"] is not JsonObject provenance)
                return null;

            var integrationKey = provenance["integrationKey"]?.GetValue<string>();
            if (string.IsNullOrEmpty(integrationKey))
                return null;
            var contractVersion = provenance["contractVersion"]?.GetValue<int>();

            var recordKeys = new List<string>();
            if (envelope["records"] is JsonArray { Count: > 0 } records && records[0] is JsonObject firstRecord)
                recordKeys.AddRange(firstRecord.Select(kv => kv.Key));

            return new ImportSampleShape(integrationKey, contractVersion, recordKeys);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
