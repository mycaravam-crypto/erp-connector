using System.Text.Json;
using Connector.Core.DynamicExport;
using Microsoft.EntityFrameworkCore;

namespace Connector.Infrastructure;

/// <summary>
/// One-time startup converter: reads the legacy AppSetting-backed <c>export_mapping</c>/<c>export_presets</c>
/// blobs and writes one <see cref="ExportDefinitionEntity"/> per legacy config, so Phase 14's N-definitions
/// model starts populated from whatever a deployment already had configured. Idempotent — guarded by "any
/// <see cref="ExportDefinitionEntity"/> row already exists" so a later app restart never re-runs it or
/// duplicates rows. Leaves the AppSettings rows in place and does not touch them again — the legacy
/// <c>/api/export-mapping</c> endpoints remain fully read/write independently of this snapshot (§11's
/// original read-only-after-migration lock was removed; see knowledge/pipeline/export-definitions-2.0.md §11).
/// </summary>
public static class ExportDefinitionMigrator
{
    private const string LegacyMappingName = "Legacy Export";

    public static async Task MigrateLegacyMappingsAsync(ExportLogDbContext db, CancellationToken ct = default)
    {
        if (await db.ExportDefinitions.AnyAsync(ct))
            return; // already migrated (or Phase 14 definitions already exist) — never re-run

        var defaultFormat = await GetDefaultFormatAsync(db);
        var now = DateTimeOffset.UtcNow.ToString("O");
        var definitions = new List<ExportDefinitionEntity>();

        var mappingJson = await db.GetSettingRawAsync(SettingsKeys.ExportMapping);
        if (mappingJson is not null)
        {
            var config = ExportMappingJson.DeserializeConfig(mappingJson);
            if (config is not null)
                definitions.Add(ToDefinition(LegacyMappingName, config, defaultFormat, now));
        }

        var presetsJson = await db.GetSettingRawAsync(SettingsKeys.ExportPresets);
        if (presetsJson is not null)
        {
            var presets = ExportMappingJson.DeserializePresets(presetsJson);
            foreach (var (name, config) in presets)
                definitions.Add(ToDefinition(name, config, defaultFormat, now));
        }

        if (definitions.Count == 0)
            return;

        db.ExportDefinitions.AddRange(definitions);
        await db.SaveChangesAsync(ct);
    }

    // The legacy config has no per-mapping format — CSV/Excel/JSON is a request-time choice at export.
    // Reuse the single global scheduler format (defaulting the same way ExportWorker does) as the
    // migrated definition's starting format; an operator can change it afterwards.
    private static async Task<string> GetDefaultFormatAsync(ExportLogDbContext db)
    {
        var raw = await db.GetSettingRawAsync(SettingsKeys.SchedulerConfig);
        if (raw is null)
            return "json";

        var data = JsonSerializer.Deserialize<SchedulerConfigData>(raw);
        return data?.Format ?? "json";
    }

    private static ExportDefinitionEntity ToDefinition(
        string name,
        ExportMappingConfig config,
        string defaultFormat,
        string timestamp
    ) =>
        new()
        {
            Name = name,
            RootTable = config.SourceTable,
            RootNode = ExportNodeJson.Serialize(ToRootNode(config)),
            // Nesting only ever rendered in the legacy JSON path (DynamicExportService.UsesNestedJson) —
            // preserve that behavior for a migrated definition that has enabled nested groups.
            OutputFormat = config.NestedGroups is { Length: > 0 } ? "json" : defaultFormat,
            IsEnabled = false, // migrated definitions start disabled — an operator opts each one into scheduling
            Schedule = null,
            ConfigVersion = 1,
            CreatedBy = "migration",
            CreatedAt = timestamp,
        };

    private static ExportNode ToRootNode(ExportMappingConfig config)
    {
        var fieldNodes = config.Fields.Select(ToScalarNode);
        var relationNodes = config.Relations.Select(ToRelationNode);
        var nestedGroupNodes = (config.NestedGroups ?? []).Select(ToNestedGroupNode);

        return new ExportNode(
            TargetKey: "root",
            Kind: ExportNodeKind.Root,
            SourceField: null,
            RelatedTable: null,
            JoinKey: null,
            SourceJoinKey: null,
            Filter: null,
            Mapping: null,
            Children: [.. fieldNodes, .. relationNodes, .. nestedGroupNodes],
            Enabled: true
        );
    }

    private static ExportNode ToScalarNode(ExportMappingField field) =>
        new(
            TargetKey: field.TargetName,
            Kind: ExportNodeKind.ScalarField,
            SourceField: field.SourceName,
            RelatedTable: null,
            JoinKey: null,
            SourceJoinKey: null,
            Filter: null,
            Mapping: null,
            Children: [],
            Enabled: field.Enabled
        );

    // Legacy relation flattening (FlattenStrategy/Delimiter merging several related-table fields into one
    // joined column) has no equivalent in the tree model: Phase 14's format writers flatten arbitrary-depth
    // nesting generically instead (the actual new capability), so a relation converts to a plain array node
    // with one scalar-field child per relation field, and the per-relation strategy setting is dropped.
    private static ExportNode ToRelationNode(ExportMappingRelation relation) =>
        new(
            TargetKey: relation.RelatedTable,
            Kind: ExportNodeKind.Array,
            SourceField: null,
            RelatedTable: relation.RelatedTable,
            JoinKey: relation.JoinKey,
            SourceJoinKey: relation.SourceJoinKey,
            Filter: null,
            Mapping: null,
            Children: relation.Fields.Select(ToRelationFieldNode).ToArray(),
            Enabled: relation.Enabled
        );

    private static ExportNode ToRelationFieldNode(ExportMappingRelationField field) =>
        new(
            TargetKey: field.TargetField,
            Kind: ExportNodeKind.ScalarField,
            SourceField: field.SourceField,
            RelatedTable: null,
            JoinKey: null,
            SourceJoinKey: null,
            Filter: null,
            Mapping: null,
            Children: [],
            Enabled: field.Enabled
        );

    private static ExportNode ToNestedGroupNode(ExportMappingNestedGroup group) =>
        new(
            TargetKey: group.TargetKey,
            Kind: group.Kind,
            SourceField: null,
            RelatedTable: group.RelatedTable,
            JoinKey: group.JoinKey,
            SourceJoinKey: group.SourceJoinKey,
            Filter: null,
            Mapping: null,
            Children: [.. group.Fields.Select(ToNestedFieldNode), .. group.Children.Select(ToNestedGroupNode)],
            Enabled: group.Enabled
        );

    private static ExportNode ToNestedFieldNode(ExportMappingNestedField field) =>
        new(
            TargetKey: field.TargetKey,
            Kind: ExportNodeKind.ScalarField,
            SourceField: field.SourceField,
            RelatedTable: null,
            JoinKey: null,
            SourceJoinKey: null,
            Filter: null,
            Mapping: null,
            Children: [],
            Enabled: field.Enabled
        );
}
