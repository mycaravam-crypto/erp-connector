using Connector.Core.Schema;

namespace Connector.Api.Endpoints;

/// <summary>
/// Read-only reference for the originally negotiated ICD export contract. Decoupled from the
/// live dynamic export mapping (see ExportMappingEndpoints) — this is documentation, not config.
/// </summary>
static class SchemaEndpoints
{
    internal static void MapSchemaEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/api/schema",
                () =>
                {
                    var columns = new SchemaColumnDto[]
                    {
                        new(
                            "guid",
                            "systemconfiguration.id",
                            "UUID text",
                            "Coalesce key — stable PostgreSQL PK; never changes for the entity lifetime",
                            true,
                            null
                        ),
                        new(
                            "serial_number",
                            "systemconfiguration.serial",
                            "Text (explicit)",
                            "Physical unit identity; warranty lookups by humans. Not the coalesce key.",
                            true,
                            null
                        ),
                        new(
                            "part_number",
                            "masterdata.part_number",
                            "Text (explicit)",
                            "Model/part reference — explicit text to prevent numeric coercion",
                            true,
                            null
                        ),
                        new(
                            "parent_serial_number",
                            "articlestructure → systemconfiguration.serial",
                            "Text (explicit)",
                            "BOM parent reference; drives cmdb_rel_ci hierarchy on the vendor side",
                            true,
                            null
                        ),
                        new(
                            "model_reference",
                            "masterdata.article_name",
                            "Text",
                            "Human-readable model name; links to cmdb_model on the vendor side",
                            true,
                            null
                        ),
                        new(
                            "commissioning_date",
                            "systemconfiguration.commission_date",
                            "ISO 8601 date",
                            "Warranty start date (YYYY-MM-DD)",
                            true,
                            null
                        ),
                        new(
                            "maintenance_state",
                            "systemconfiguration.status",
                            "Text (mapped enum)",
                            "CI lifecycle state mapped to vendor install_status values",
                            true,
                            null
                        ),
                    };
                    return Results.Ok(new SchemaDto(ExportSchema.Version, columns));
                }
            )
            .RequireAuthorization();
    }
}
