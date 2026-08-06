using System.Text.Json;
using System.Text.RegularExpressions;
using Connector.Core.DynamicExport;
using Connector.Infrastructure;

namespace Connector.Api.Endpoints;

static class ExportMappingEndpoints
{
    internal static void MapExportMappingEndpoints(this WebApplication app)
    {
        // Returns the stored export mapping config, or 404 if none saved.
        app.MapGet(
                "/api/export-mapping",
                async (ExportLogDbContext db) =>
                {
                    var setting = await db.AppSettings.FindAsync("export_mapping");
                    if (setting is null)
                        return Results.NotFound();
                    var config = ExportMappingJson.DeserializeConfig(setting.Value);
                    return config is null ? Results.NotFound() : Results.Ok(config);
                }
            )
            .RequireAuthorization();

        // Validates and persists a full export mapping config.
        app.MapPut(
                "/api/export-mapping",
                async (
                    ExportMappingConfig config,
                    ExportLogDbContext db,
                    HttpContext httpContext,
                    AuditService audit
                ) =>
                {
                    var validationError = await ValidateConfigAsync(config, db);
                    if (validationError is not null)
                        return Results.BadRequest(validationError);

                    var serialized = JsonSerializer.Serialize(config);
                    var setting = await db.AppSettings.FindAsync("export_mapping");
                    if (setting is null)
                        db.AppSettings.Add(new AppSettingEntity { Key = "export_mapping", Value = serialized });
                    else
                        setting.Value = serialized;

                    await db.SaveChangesAsync();
                    await audit.LogAsync(
                        httpContext.User.Identity!.Name!,
                        "export_mapping_saved",
                        $"table={config.SourceTable}"
                    );
                    return Results.Ok(config);
                }
            )
            .RequireAuthorization();

        // Returns all saved presets as a name→config dictionary.
        app.MapGet(
                "/api/export-mapping/presets",
                async (ExportLogDbContext db) =>
                {
                    var setting = await db.AppSettings.FindAsync("export_presets");
                    if (setting is null)
                        return Results.Ok(new Dictionary<string, ExportMappingConfig>());
                    var presets = ExportMappingJson.DeserializePresets(setting.Value);
                    return Results.Ok(presets);
                }
            )
            .RequireAuthorization();

        // Creates or updates a single named preset.
        app.MapPut(
                "/api/export-mapping/presets/{name}",
                async (
                    string name,
                    ExportMappingConfig config,
                    ExportLogDbContext db,
                    HttpContext httpContext,
                    AuditService audit
                ) =>
                {
                    if (string.IsNullOrWhiteSpace(name))
                        return Results.BadRequest("Preset name is required.");

                    var validationError = await ValidateConfigAsync(config, db);
                    if (validationError is not null)
                        return Results.BadRequest(validationError);

                    var setting = await db.AppSettings.FindAsync("export_presets");
                    var presets = setting is null
                        ? new Dictionary<string, ExportMappingConfig>()
                        : ExportMappingJson.DeserializePresets(setting.Value);

                    presets[name] = config;
                    var serialized = JsonSerializer.Serialize(presets);

                    if (setting is null)
                        db.AppSettings.Add(new AppSettingEntity { Key = "export_presets", Value = serialized });
                    else
                        setting.Value = serialized;

                    await db.SaveChangesAsync();
                    await audit.LogAsync(httpContext.User.Identity!.Name!, "preset_saved", name);
                    return Results.Ok(config);
                }
            )
            .RequireAuthorization();

        // Deletes a single named preset. Returns 404 when the name does not exist.
        app.MapDelete(
                "/api/export-mapping/presets/{name}",
                async (string name, ExportLogDbContext db, HttpContext httpContext, AuditService audit) =>
                {
                    var setting = await db.AppSettings.FindAsync("export_presets");
                    if (setting is null)
                        return Results.NotFound();

                    var presets = ExportMappingJson.DeserializePresets(setting.Value);

                    if (!presets.Remove(name))
                        return Results.NotFound();

                    setting.Value = JsonSerializer.Serialize(presets);
                    await db.SaveChangesAsync();
                    await audit.LogAsync(httpContext.User.Identity!.Name!, "preset_deleted", name);
                    return Results.NoContent();
                }
            )
            .RequireAuthorization();
    }

    // Valid SQL identifier: letters/digits/underscore, not starting with a digit. Scoped only to new
    // nested-group inputs (RelatedTable/JoinKey/SourceJoinKey/SourceField) — deliberately not retrofitted
    // onto the pre-existing Fields/Relations inputs, which are already in production protected only by
    // DynamicExportService.QI(); adding a stricter regex there risks breaking an existing saved mapping
    // whose SourceName/RelatedTable happens to contain a character outside this charset on next re-save.
    private static readonly Regex SqlIdentifierRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    // Shared by both the mapping PUT and the preset PUT: blank-field / GDPR / relation-shape checks
    // (unchanged from before this refactor) plus new recursive checks over NestedGroups.
    private static async Task<string?> ValidateConfigAsync(ExportMappingConfig config, ExportLogDbContext db)
    {
        if (string.IsNullOrWhiteSpace(config.SourceTable))
            return "SourceTable is required.";

        var badFields = config
            .Fields.Where(f => f.Enabled && string.IsNullOrWhiteSpace(f.TargetName))
            .Select(f => f.SourceName)
            .ToList();
        if (badFields.Count > 0)
            return $"Enabled fields must have non-empty target names: {string.Join(", ", badFields)}";

        var denylist = await DynamicExportService.GetDeniedFieldsAsync(db);
        var gdprViolations = GetGdprViolations(config, denylist);
        if (gdprViolations.Count > 0)
            return $"GDPR violation: the following fields are personal data and must not be exported "
                + $"(GDPR Art. 5(1)(c) — data minimisation): {string.Join(", ", gdprViolations)}";

        var badRels = ValidateRelations(config);
        if (badRels.Count > 0)
            return "Enabled relations must specify RelatedTable, JoinKey, SourceJoinKey, at least one enabled field, and a non-empty target name for every enabled field.";

        return ValidateNestedGroups(config, denylist);
    }

    // Validates NestedGroups recursively: depth guard, required fields, non-empty groups, GDPR denylist
    // at every depth, identifier-safety for new SQL-facing inputs, and no duplicate export keys among
    // enabled siblings at any single level (a duplicate JSON key silently overwrites data, unlike a
    // duplicate CSV header which is just a cosmetic double column).
    private static string? ValidateNestedGroups(ExportMappingConfig config, IReadOnlySet<string> denylist)
    {
        var topLevelKeys = new HashSet<string>(StringComparer.Ordinal);
        var dupField = config.Fields.FirstOrDefault(f => f.Enabled && !topLevelKeys.Add(f.TargetName));
        if (dupField is not null)
            return $"Duplicate export key '{dupField.TargetName}' at the top level.";

        foreach (var g in (config.NestedGroups ?? []).Where(g => g.Enabled))
        {
            if (!topLevelKeys.Add(g.TargetKey))
                return $"Duplicate export key '{g.TargetKey}' at the top level.";

            var error = ValidateNestedGroup(g, denylist, path: g.TargetKey, depth: 1);
            if (error is not null)
                return error;
        }

        return null;
    }

    private static string? ValidateNestedGroup(
        ExportMappingNestedGroup g,
        IReadOnlySet<string> denylist,
        string path,
        int depth
    )
    {
        if (depth > DynamicExportService.MaxNestedDepth)
            return $"Nested group '{path}' exceeds the maximum nesting depth of {DynamicExportService.MaxNestedDepth}.";

        if (
            string.IsNullOrWhiteSpace(g.RelatedTable)
            || string.IsNullOrWhiteSpace(g.JoinKey)
            || string.IsNullOrWhiteSpace(g.SourceJoinKey)
            || string.IsNullOrWhiteSpace(g.TargetKey)
        )
            return $"Nested group '{path}' must specify RelatedTable, JoinKey, SourceJoinKey, and TargetKey.";

        if (g.Kind is not ("object" or "array"))
            return $"Nested group '{path}' must have Kind \"object\" or \"array\" (got \"{g.Kind}\").";

        if (
            !SqlIdentifierRegex.IsMatch(g.RelatedTable)
            || !SqlIdentifierRegex.IsMatch(g.JoinKey)
            || !SqlIdentifierRegex.IsMatch(g.SourceJoinKey)
        )
            return $"Nested group '{path}': RelatedTable, JoinKey, and SourceJoinKey must be valid identifiers (letters, digits, underscore; not starting with a digit).";

        if (ContainsControlCharacters(g.TargetKey))
            return $"Nested group '{path}': TargetKey contains invalid control characters.";

        var enabledFields = (g.Fields ?? []).Where(f => f.Enabled).ToList();
        var enabledChildren = (g.Children ?? []).Where(c => c.Enabled).ToList();
        if (enabledFields.Count == 0 && enabledChildren.Count == 0)
            return $"Nested group '{path}' must have at least one enabled field or nested group.";

        var siblingKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in enabledFields)
        {
            if (string.IsNullOrWhiteSpace(f.SourceField) || string.IsNullOrWhiteSpace(f.TargetKey))
                return $"Nested group '{path}': every enabled field needs a non-empty SourceField and TargetKey.";
            if (!SqlIdentifierRegex.IsMatch(f.SourceField))
                return $"Nested group '{path}': field SourceField '{f.SourceField}' is not a valid identifier.";
            if (ContainsControlCharacters(f.TargetKey))
                return $"Nested group '{path}': field TargetKey '{f.TargetKey}' contains invalid control characters.";
            if (denylist.Contains(f.SourceField))
                return $"GDPR violation at '{path}.{f.TargetKey}': '{f.SourceField}' is personal data and must not be exported (GDPR Art. 5(1)(c) — data minimisation).";
            if (!siblingKeys.Add(f.TargetKey))
                return $"Duplicate export key '{f.TargetKey}' within '{path}'.";
        }
        var dupChild = enabledChildren.FirstOrDefault(child => !siblingKeys.Add(child.TargetKey));
        if (dupChild is not null)
            return $"Duplicate export key '{dupChild.TargetKey}' within '{path}'.";

        foreach (var child in enabledChildren)
        {
            var error = ValidateNestedGroup(child, denylist, $"{path}.{child.TargetKey}", depth + 1);
            if (error is not null)
                return error;
        }

        return null;
    }

    private static bool ContainsControlCharacters(string s) => s.Any(char.IsControl);

    private static List<object> ValidateRelations(ExportMappingConfig config) =>
        config
            .Relations.Where(r =>
                r.Enabled
                && (
                    string.IsNullOrWhiteSpace(r.RelatedTable)
                    || string.IsNullOrWhiteSpace(r.JoinKey)
                    || string.IsNullOrWhiteSpace(r.SourceJoinKey)
                    || !(r.Fields ?? []).Any(f => f.Enabled)
                    || (r.Fields ?? []).Any(f => f.Enabled && string.IsNullOrWhiteSpace(f.TargetField))
                )
            )
            .Cast<object>()
            .ToList();

    private static List<string> GetGdprViolations(ExportMappingConfig config, IReadOnlySet<string> denylist) =>
        config
            .Fields.Where(f => f.Enabled && denylist.Contains(f.SourceName))
            .Select(f => f.SourceName)
            .Concat(
                config
                    .Relations.Where(r => r.Enabled)
                    .SelectMany(r => r.Fields ?? [])
                    .Where(f => f.Enabled && denylist.Contains(f.SourceField))
                    .Select(f => f.SourceField)
            )
            .Distinct()
            .ToList();
}
