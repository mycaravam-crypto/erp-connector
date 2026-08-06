using System.Text.Json;
using Connector.Core.DynamicExport;
using Connector.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Connector.Api.Endpoints;

static class ConnectionEndpoints
{
    internal static void MapConnectionEndpoints(this WebApplication app)
    {
        // Returns the stored connection (host/port/db/user only — password never returned).
        app.MapGet(
                "/api/connection",
                async (ExportLogDbContext db) =>
                {
                    var setting = await db.AppSettings.FindAsync("erp_connection");
                    if (setting is null)
                        return Results.NotFound();

                    var cfg = JsonSerializer.Deserialize<ErpConnectionConfig>(setting.Value);
                    if (cfg is null)
                        return Results.NotFound();

                    return Results.Ok(new ErpConnectionInfo(cfg.Host, cfg.Port, cfg.Database, cfg.Username));
                }
            )
            .RequireAuthorization();

        // Tests the connection, persists it on success, and returns the live source schema.
        app.MapPost(
                "/api/connection",
                async (ErpConnectionConfig request, ExportLogDbContext db, CancellationToken ct) =>
                {
                    if (
                        string.IsNullOrWhiteSpace(request.Host)
                        || string.IsNullOrWhiteSpace(request.Database)
                        || string.IsNullOrWhiteSpace(request.Username)
                    )
                        return Results.BadRequest("Host, Database, and Username are required.");

                    try
                    {
                        await using var conn = new NpgsqlConnection(
                            DynamicExportService.BuildConnectionString(request)
                        );
                        await conn.OpenAsync(ct);

                        var tables = await IntrospectSchemaAsync(conn, ct);

                        var serialized = JsonSerializer.Serialize(request);
                        var setting = await db.AppSettings.FindAsync("erp_connection");
                        if (setting is null)
                            db.AppSettings.Add(new AppSettingEntity { Key = "erp_connection", Value = serialized });
                        else
                            setting.Value = serialized;
                        await db.SaveChangesAsync();

                        return Results.Ok(
                            new SourceSchemaDto($"{request.Host}:{request.Port}/{request.Database}", tables)
                        );
                    }
                    catch (Exception ex)
                    {
                        return Results.BadRequest($"Connection failed: {ex.Message}");
                    }
                }
            )
            .RequireAuthorization();

        // Returns schema from the persisted Postgres connection when one is configured,
        // falling back to the hardcoded demo schema when none is stored or the connection fails.
        app.MapGet(
                "/api/source-schema",
                async (ExportLogDbContext db, CancellationToken ct) =>
                {
                    var connSetting = await db.AppSettings.FindAsync("erp_connection");
                    if (connSetting is not null)
                    {
                        var cfg = JsonSerializer.Deserialize<ErpConnectionConfig>(connSetting.Value);
                        if (cfg is not null)
                        {
                            try
                            {
                                await using var conn = new NpgsqlConnection(
                                    DynamicExportService.BuildConnectionString(cfg)
                                );
                                await conn.OpenAsync(ct);
                                var tables = await IntrospectSchemaAsync(conn, ct);
                                return Results.Ok(new SourceSchemaDto($"{cfg.Host}:{cfg.Port}/{cfg.Database}", tables));
                            }
                            catch
                            {
                                // Fall through to demo schema if stored config is unreachable.
                            }
                        }
                    }

                    return Results.Ok(DemoSourceSchema());
                }
            )
            .RequireAuthorization();
    }

    // Introspects the public schema of an open Npgsql connection using information_schema views.
    internal static async Task<SourceTableDto[]> IntrospectSchemaAsync(
        NpgsqlConnection conn,
        CancellationToken ct = default
    )
    {
        var sql = """
            SELECT
                c.table_name,
                c.column_name,
                c.data_type,
                c.is_nullable,
                EXISTS (
                    SELECT 1
                    FROM information_schema.table_constraints tc
                    JOIN information_schema.key_column_usage kcu
                        ON kcu.constraint_name = tc.constraint_name
                        AND kcu.table_schema  = tc.table_schema
                        AND kcu.table_name    = tc.table_name
                        AND kcu.column_name   = c.column_name
                    WHERE tc.constraint_type = 'PRIMARY KEY'
                      AND tc.table_schema    = 'public'
                      AND tc.table_name      = c.table_name
                ) AS is_pk,
                fk.foreign_table_name,
                fk.foreign_column_name
            FROM information_schema.columns c
            LEFT JOIN LATERAL (
                SELECT ccu.table_name AS foreign_table_name, ccu.column_name AS foreign_column_name
                FROM information_schema.table_constraints tc
                JOIN information_schema.key_column_usage kcu
                    ON kcu.constraint_name = tc.constraint_name
                    AND kcu.table_schema   = tc.table_schema
                JOIN information_schema.constraint_column_usage ccu
                    ON ccu.constraint_name = tc.constraint_name
                    AND ccu.table_schema   = tc.table_schema
                WHERE tc.constraint_type = 'FOREIGN KEY'
                  AND tc.table_schema    = 'public'
                  AND tc.table_name      = c.table_name
                  AND kcu.column_name    = c.column_name
                LIMIT 1
            ) fk ON true
            WHERE c.table_schema = 'public'
            ORDER BY c.table_name, c.ordinal_position
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var byTable = new Dictionary<string, List<SourceColumnDto>>();
        while (await reader.ReadAsync(ct))
        {
            var table = reader.GetString(0);
            if (!byTable.ContainsKey(table))
                byTable[table] = [];
            byTable[table]
                .Add(
                    new SourceColumnDto(
                        Name: reader.GetString(1),
                        Type: reader.GetString(2),
                        Nullable: reader.GetString(3) == "YES",
                        PrimaryKey: reader.GetBoolean(4),
                        ForeignKeyTable: await reader.IsDBNullAsync(5, ct) ? null : reader.GetString(5),
                        ForeignKeyColumn: await reader.IsDBNullAsync(6, ct) ? null : reader.GetString(6)
                    )
                );
        }

        return byTable.Select(kv => new SourceTableDto(kv.Key, "", kv.Value.ToArray())).OrderBy(t => t.Name).ToArray();
    }

    // Hardcoded demo schema that mirrors what a real production PostgreSQL ERP database would expose.
    internal static SourceSchemaDto DemoSourceSchema() =>
        new(
            "demo-erp (SQLite in dev · PostgreSQL in prod)",
            new SourceTableDto[]
            {
                new(
                    "systemconfiguration",
                    "Installed CI instances — one row per physical unit",
                    new SourceColumnDto[]
                    {
                        new("id", "uuid", Nullable: false, PrimaryKey: true),
                        new("serial", "character varying(100)", Nullable: true, PrimaryKey: false),
                        new(
                            "article_id",
                            "uuid",
                            Nullable: true,
                            PrimaryKey: false,
                            ForeignKeyTable: "masterdata",
                            ForeignKeyColumn: "id"
                        ),
                        new("status", "character varying(50)", Nullable: true, PrimaryKey: false),
                        new("commission_date", "date", Nullable: true, PrimaryKey: false),
                        new("technician_name", "character varying(100)", Nullable: true, PrimaryKey: false),
                        new("storage_location", "character varying(200)", Nullable: true, PrimaryKey: false),
                    }
                ),
                new(
                    "masterdata",
                    "Article/model master records — one row per model type",
                    new SourceColumnDto[]
                    {
                        new("id", "uuid", Nullable: false, PrimaryKey: true),
                        new("article_name", "character varying(200)", Nullable: true, PrimaryKey: false),
                        new("part_number", "character varying(100)", Nullable: true, PrimaryKey: false),
                        new("manufacturer", "character varying(100)", Nullable: true, PrimaryKey: false),
                    }
                ),
                new(
                    "maintenance_plan",
                    "Maintenance plan assignments — drives scope filter",
                    new SourceColumnDto[]
                    {
                        new("id", "uuid", Nullable: false, PrimaryKey: true),
                        new(
                            "system_configuration_id",
                            "uuid",
                            Nullable: false,
                            PrimaryKey: false,
                            ForeignKeyTable: "systemconfiguration",
                            ForeignKeyColumn: "id"
                        ),
                        new("status", "character varying(50)", Nullable: false, PrimaryKey: false),
                        new("allocation_chart_ref", "character varying(100)", Nullable: true, PrimaryKey: false),
                    }
                ),
                new(
                    "articlestructure",
                    "BOM parent–child relationships",
                    new SourceColumnDto[]
                    {
                        new("id", "uuid", Nullable: false, PrimaryKey: true),
                        new(
                            "parent_id",
                            "uuid",
                            Nullable: true,
                            PrimaryKey: false,
                            ForeignKeyTable: "masterdata",
                            ForeignKeyColumn: "id"
                        ),
                        new(
                            "child_id",
                            "uuid",
                            Nullable: true,
                            PrimaryKey: false,
                            ForeignKeyTable: "masterdata",
                            ForeignKeyColumn: "id"
                        ),
                    }
                ),
            }
        );
}
