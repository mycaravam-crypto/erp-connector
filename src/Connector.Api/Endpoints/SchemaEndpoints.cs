using Connector.Core.Schema;
using Connector.Infrastructure;

namespace Connector.Api.Endpoints;

static class SchemaEndpoints
{
    internal static void MapSchemaEndpoints(this WebApplication app)
    {
        // Returns schema columns with the active flag read from persisted preferences.
        app.MapGet(
                "/api/schema",
                async (ExportLogDbContext db) =>
                {
                    var activeSet = new HashSet<string>(
                        await db.GetSettingAsync<string[]>(SettingsKeys.ActiveColumns) ?? ExportSchema.Columns
                    );

                    var mapping =
                        await db.GetSettingAsync<Dictionary<string, string>>(SettingsKeys.ColumnMappings) ?? new();

                    string? ExportName(string n) => mapping.GetValueOrDefault(n);

                    var columns = new SchemaColumnDto[]
                    {
                        new(
                            "guid",
                            "systemconfiguration.id",
                            "UUID text",
                            "Coalesce key — stable PostgreSQL PK; never changes for the entity lifetime",
                            activeSet.Contains("guid"),
                            ExportName("guid")
                        ),
                        new(
                            "serial_number",
                            "systemconfiguration.serial",
                            "Text (explicit)",
                            "Physical unit identity; warranty lookups by humans. Not the coalesce key.",
                            activeSet.Contains("serial_number"),
                            ExportName("serial_number")
                        ),
                        new(
                            "part_number",
                            "masterdata.part_number",
                            "Text (explicit)",
                            "Model/part reference — explicit text to prevent numeric coercion",
                            activeSet.Contains("part_number"),
                            ExportName("part_number")
                        ),
                        new(
                            "parent_serial_number",
                            "articlestructure → systemconfiguration.serial",
                            "Text (explicit)",
                            "BOM parent reference; drives cmdb_rel_ci hierarchy on the vendor side",
                            activeSet.Contains("parent_serial_number"),
                            ExportName("parent_serial_number")
                        ),
                        new(
                            "model_reference",
                            "masterdata.article_name",
                            "Text",
                            "Human-readable model name; links to cmdb_model on the vendor side",
                            activeSet.Contains("model_reference"),
                            ExportName("model_reference")
                        ),
                        new(
                            "commissioning_date",
                            "systemconfiguration.commission_date",
                            "ISO 8601 date",
                            "Warranty start date (YYYY-MM-DD)",
                            activeSet.Contains("commissioning_date"),
                            ExportName("commissioning_date")
                        ),
                        new(
                            "maintenance_state",
                            "systemconfiguration.status",
                            "Text (mapped enum)",
                            "CI lifecycle state mapped to ServiceNow install_status values",
                            activeSet.Contains("maintenance_state"),
                            ExportName("maintenance_state")
                        ),
                    };
                    return Results.Ok(new SchemaDto(ExportSchema.Version, columns));
                }
            )
            .RequireAuthorization();

        // Persists the active column set. Rejects unknown column names; allows partial sets.
        app.MapPatch(
                "/api/schema/columns",
                async (ColumnPatchRequest request, ExportLogDbContext db) =>
                {
                    var valid = request.Columns.Where(c => ExportSchema.Columns.Contains(c)).Distinct().ToArray();

                    await db.SetSettingAsync(SettingsKeys.ActiveColumns, valid);
                    return Results.Ok(valid);
                }
            )
            .RequireAuthorization();

        // Persists per-column export name overrides.
        app.MapPatch(
                "/api/schema/mappings",
                async (MappingPatchRequest request, ExportLogDbContext db) =>
                {
                    var valid = request
                        .Mappings.Where(kvp =>
                            ExportSchema.Columns.Contains(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value)
                        )
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Trim());

                    await db.SetSettingAsync(SettingsKeys.ColumnMappings, valid);
                    return Results.Ok(valid);
                }
            )
            .RequireAuthorization();
    }
}
