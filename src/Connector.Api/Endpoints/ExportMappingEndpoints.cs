using System.Text.Json;
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
                    var config = JsonSerializer.Deserialize<ExportMappingConfig>(setting.Value);
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
                    if (string.IsNullOrWhiteSpace(config.SourceTable))
                        return Results.BadRequest("SourceTable is required.");

                    var badFields = config
                        .Fields.Where(f => f.Enabled && string.IsNullOrWhiteSpace(f.TargetName))
                        .Select(f => f.SourceName)
                        .ToList();
                    if (badFields.Count > 0)
                        return Results.BadRequest(
                            $"Enabled fields must have non-empty target names: {string.Join(", ", badFields)}"
                        );

                    var activeDenylist = await DynamicExportService.GetDeniedFieldsAsync(db);
                    var gdprViolations = config
                        .Fields.Where(f => f.Enabled && activeDenylist.Contains(f.SourceName))
                        .Select(f => f.SourceName)
                        .ToList();
                    if (gdprViolations.Count > 0)
                        return Results.BadRequest(
                            $"GDPR violation: the following fields are personal data and must not be exported "
                                + $"(GDPR Art. 5(1)(c) — data minimisation): {string.Join(", ", gdprViolations)}"
                        );

                    var badRels = ValidateRelations(config);
                    if (badRels.Count > 0)
                        return Results.BadRequest(
                            "Enabled relations must specify RelatedTable, JoinKey, SourceJoinKey, TargetField, and SourceField."
                        );

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
                    var presets =
                        JsonSerializer.Deserialize<Dictionary<string, ExportMappingConfig>>(setting.Value)
                        ?? new Dictionary<string, ExportMappingConfig>();
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

                    if (string.IsNullOrWhiteSpace(config.SourceTable))
                        return Results.BadRequest("SourceTable is required.");

                    var badFields = config
                        .Fields.Where(f => f.Enabled && string.IsNullOrWhiteSpace(f.TargetName))
                        .Select(f => f.SourceName)
                        .ToList();
                    if (badFields.Count > 0)
                        return Results.BadRequest(
                            $"Enabled fields must have non-empty target names: {string.Join(", ", badFields)}"
                        );

                    var presetDenylist = await DynamicExportService.GetDeniedFieldsAsync(db);
                    var gdprViolations = config
                        .Fields.Where(f => f.Enabled && presetDenylist.Contains(f.SourceName))
                        .Select(f => f.SourceName)
                        .ToList();
                    if (gdprViolations.Count > 0)
                        return Results.BadRequest(
                            $"GDPR violation: the following fields are personal data and must not be exported "
                                + $"(GDPR Art. 5(1)(c) — data minimisation): {string.Join(", ", gdprViolations)}"
                        );

                    var badRels = ValidateRelations(config);
                    if (badRels.Count > 0)
                        return Results.BadRequest(
                            "Enabled relations must specify RelatedTable, JoinKey, SourceJoinKey, TargetField, and SourceField."
                        );

                    var setting = await db.AppSettings.FindAsync("export_presets");
                    var presets = setting is null
                        ? new Dictionary<string, ExportMappingConfig>()
                        : JsonSerializer.Deserialize<Dictionary<string, ExportMappingConfig>>(setting.Value)
                            ?? new Dictionary<string, ExportMappingConfig>();

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

                    var presets =
                        JsonSerializer.Deserialize<Dictionary<string, ExportMappingConfig>>(setting.Value)
                        ?? new Dictionary<string, ExportMappingConfig>();

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

    private static List<object> ValidateRelations(ExportMappingConfig config) =>
        config
            .Relations.Where(r =>
                r.Enabled
                && (
                    string.IsNullOrWhiteSpace(r.RelatedTable)
                    || string.IsNullOrWhiteSpace(r.JoinKey)
                    || string.IsNullOrWhiteSpace(r.SourceJoinKey)
                    || string.IsNullOrWhiteSpace(r.TargetField)
                    || string.IsNullOrWhiteSpace(r.StrategyOptions.SourceField)
                )
            )
            .Cast<object>()
            .ToList();
}
