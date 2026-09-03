using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Connector.Api;
using Connector.Core.Domain;
using Connector.Core.DynamicExport;
using Connector.Core.Schema;
using Connector.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Connector.Api.Endpoints;

static class PipelineEndpoints
{
    internal static void MapPipelineEndpoints(this WebApplication app)
    {
        app.MapPost(
                "/api/pipeline/run",
                async (
                    string? format,
                    ExportLogDbContext db,
                    FileSystemExportSink sink,
                    ILogger<Program> logger,
                    AuditService audit,
                    HttpContext httpContext,
                    CancellationToken ct
                ) =>
                {
                    var fmt = format?.ToLowerInvariant() switch
                    {
                        "csv" => "csv",
                        "xlsx" => "xlsx",
                        _ => "json",
                    };

                    var sequenceNo = (await db.ExportRuns.MaxAsync(r => (int?)r.SequenceNo, ct) ?? 0) + 1;
                    var run = new ExportRunEntity
                    {
                        SequenceNo = sequenceNo,
                        ExtractedAt = DateTimeOffset.UtcNow.ToString("O"),
                        Status = ExportRunStatus.Pending,
                    };
                    db.ExportRuns.Add(run);
                    await db.SaveChangesAsync(ct);

                    var mappingRaw = await db.GetSettingRawAsync(SettingsKeys.ExportMapping);
                    if (mappingRaw is null)
                    {
                        run.Status = ExportRunStatus.Failed;
                        await db.SaveChangesAsync(ct);
                        return Results.Problem(
                            detail: "No export mapping configured. Go to Step 3 and save an export mapping first.",
                            statusCode: 400
                        );
                    }
                    var config = ExportMappingJson.DeserializeConfig(mappingRaw)!;

                    var connRaw = await db.GetSettingRawAsync(SettingsKeys.ErpConnection);
                    if (connRaw is null)
                    {
                        run.Status = ExportRunStatus.Failed;
                        await db.SaveChangesAsync(ct);
                        return Results.Problem(
                            detail: "No database connection configured. Go to Step 1 and save a connection first.",
                            statusCode: 400
                        );
                    }
                    var connCfg = JsonSerializer.Deserialize<ErpConnectionConfig>(connRaw)!;

                    try
                    {
                        var extractedAt = DateTimeOffset.UtcNow;
                        var gdprDenylist = await DynamicExportService.GetDeniedFieldsAsync(db);

                        await using var pgConn = new NpgsqlConnection(
                            DynamicExportService.BuildConnectionString(connCfg)
                        );
                        await pgConn.OpenAsync(ct);

                        var built = await DynamicExportService.BuildExportAsync(
                            pgConn,
                            config,
                            fmt,
                            ExportSchema.Version,
                            extractedAt,
                            ct,
                            gdprDenylist: gdprDenylist
                        );

                        if (built.RecordCount == 0)
                        {
                            run.Status = ExportRunStatus.Failed;
                            await db.SaveChangesAsync(ct);
                            return Results.Problem(
                                detail: "Export aborted: query returned 0 records. Check that your mapping includes the maintenance_plan scope predicate.",
                                statusCode: 400
                            );
                        }

                        var bytes = built.Bytes;
                        var fileName = ExportSchema.BuildFileName(sequenceNo, extractedAt, built.Extension);
                        var recordCount = built.RecordCount;

                        var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                        var package = new ExportPackage(
                            new ExportManifest(sequenceNo, ExportSchema.Version, extractedAt, recordCount, checksum),
                            bytes,
                            fileName
                        );

                        await sink.WriteAsync(package, ct);

                        run.RecordCount = package.Manifest.RecordCount;
                        run.Sha256 = package.Manifest.Sha256Checksum;
                        run.DataFileName = package.DataFileName;
                        await db.SaveChangesAsync(ct);

                        logger.LogInformation(
                            "On-demand export #{Seq} ({Fmt}) completed: {Count} records",
                            sequenceNo,
                            fmt,
                            run.RecordCount
                        );

                        var user = httpContext.User.Identity!.Name!;
                        await audit.LogAsync(
                            user,
                            "export_run_now",
                            $"#{sequenceNo} fmt={fmt} records={run.RecordCount}"
                        );

                        var sha256Short = run.Sha256.Length >= 12 ? run.Sha256[..12] : run.Sha256;
                        return Results.Ok(new RunNowResult(sequenceNo, run.RecordCount, sha256Short));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(ex, "On-demand export #{Seq} failed", sequenceNo);
                        run.Status = ExportRunStatus.Failed;
                        await db.SaveChangesAsync(CancellationToken.None);
                        return Results.Problem(ex.Message, statusCode: 500);
                    }
                }
            )
            .RequireAuthorization();

        // Runs a named Step-3 preset (Save As…) and returns the built file directly in the response —
        // the synchronous, external-system-friendly counterpart to /api/pipeline/run above. Unlike that
        // endpoint this does NOT create an ExportRun, does not write to the staging folder, and is not
        // subject to Four-Eyes Release: those model the legacy CI-to-ServiceNow delivery contract
        // specifically (see knowledge/processes/four-eyes-release.md), which a generic API-triggered pull
        // is out of scope for — same reasoning ExportDefinitionEndpoints' /run already applies. Every call
        // still writes one audit log entry (success or failure), so triggering is never silent.
        // Accepts either a normal user JWT or an X-Api-Key header (ApiKeyAuthenticationHandler) — a
        // dedicated "API user" configured in Auth:ApiKeys does not need to go through interactive login.
        app.MapPost(
                "/api/pipeline/run/{name}",
                async (
                    string name,
                    string? format,
                    ExportLogDbContext db,
                    AuditService audit,
                    HttpContext httpContext,
                    CancellationToken ct
                ) =>
                {
                    var fmt = format?.ToLowerInvariant() switch
                    {
                        "csv" => "csv",
                        "xlsx" => "xlsx",
                        _ => "json",
                    };

                    var presetsRaw = await db.GetSettingRawAsync(SettingsKeys.ExportPresets);
                    var presets = presetsRaw is null
                        ? new Dictionary<string, ExportMappingConfig>()
                        : ExportMappingJson.DeserializePresets(presetsRaw);
                    if (!presets.TryGetValue(name, out var config))
                        return Results.NotFound(
                            $"No saved export preset named '{name}'. Save one from Step 3 (\"Save As…\") first."
                        );

                    var connRaw = await db.GetSettingRawAsync(SettingsKeys.ErpConnection);
                    if (connRaw is null)
                        return Results.Problem(
                            detail: "No database connection configured. Go to Step 1 and save a connection first.",
                            statusCode: 400
                        );
                    var connCfg = JsonSerializer.Deserialize<ErpConnectionConfig>(connRaw)!;

                    var user = httpContext.User.Identity!.Name!;
                    try
                    {
                        var extractedAt = DateTimeOffset.UtcNow;
                        var gdprDenylist = await DynamicExportService.GetDeniedFieldsAsync(db);

                        await using var pgConn = new NpgsqlConnection(
                            DynamicExportService.BuildConnectionString(connCfg)
                        );
                        await pgConn.OpenAsync(ct);

                        var built = await DynamicExportService.BuildExportAsync(
                            pgConn,
                            config,
                            fmt,
                            ExportSchema.Version,
                            extractedAt,
                            ct,
                            gdprDenylist: gdprDenylist
                        );

                        await audit.LogAsync(
                            user,
                            "export_preset_run",
                            $"preset={name} fmt={fmt} records={built.RecordCount}"
                        );

                        httpContext.Response.Headers["X-Record-Count"] = built.RecordCount.ToString();
                        var fileName = DynamicExportService.BuildNamedFileName(name, extractedAt, built.Extension);
                        return Results.File(
                            built.Bytes,
                            DynamicExportService.ContentTypeFor(built.Extension),
                            fileName
                        );
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        await audit.LogAsync(user, "export_preset_run_failed", $"preset={name}: {ex.Message}");
                        return Results.Problem(ex.Message, statusCode: 500);
                    }
                }
            )
            .RequireAuthorization(policy =>
                policy
                    .AddAuthenticationSchemes(
                        JwtBearerDefaults.AuthenticationScheme,
                        ApiKeyAuthenticationHandler.SchemeName
                    )
                    .RequireAuthenticatedUser()
            );

        app.MapGet(
                "/api/pipeline/preview",
                async (ExportLogDbContext db, CancellationToken ct) =>
                {
                    var mappingRaw = await db.GetSettingRawAsync(SettingsKeys.ExportMapping);
                    if (mappingRaw is null)
                        return Results.Ok(
                            new PreviewResult(
                                0,
                                ExportSchema.Version,
                                [],
                                [],
                                "error",
                                null,
                                "No export mapping configured. Set it up in Step 3."
                            )
                        );

                    var config = ExportMappingJson.DeserializeConfig(mappingRaw)!;

                    PreviewResult EmptyResult(string msg) =>
                        new(0, ExportSchema.Version, [], [], "error", config.SourceTable, msg);

                    var connRaw = await db.GetSettingRawAsync(SettingsKeys.ErpConnection);
                    if (connRaw is null)
                        return Results.Ok(EmptyResult("No database connection configured. Set it up in Step 1."));

                    var connCfg = JsonSerializer.Deserialize<ErpConnectionConfig>(connRaw);
                    if (connCfg is null)
                        return Results.Ok(EmptyResult("Stored connection config could not be read."));

                    // Preview reflects exactly what a JSON-format export would produce: same nested-vs-flat
                    // decision DynamicExportService.BuildExportAsync uses for Run Now and the scheduled worker.
                    var previewFormat = DynamicExportService.UsesNestedJson(config, "json") ? "json" : "flat";

                    try
                    {
                        var gdprDenylist = await DynamicExportService.GetDeniedFieldsAsync(db);
                        await using var conn = new NpgsqlConnection(
                            DynamicExportService.BuildConnectionString(connCfg)
                        );
                        await conn.OpenAsync(ct);

                        if (previewFormat == "json")
                        {
                            var nestedRecords = await DynamicExportService.ExecuteNestedJsonQueryAsync(
                                conn,
                                config,
                                ct,
                                limit: 50,
                                gdprDenylist: gdprDenylist
                            );
                            return Results.Ok(
                                new PreviewResult(
                                    nestedRecords.Count,
                                    ExportSchema.Version,
                                    [],
                                    [],
                                    "dynamic-nested",
                                    config.SourceTable,
                                    NestedRecords: new JsonArray(
                                        nestedRecords.Select(r => (JsonNode?)r.DeepClone()).ToArray()
                                    )
                                )
                            );
                        }

                        var cols = DynamicExportService.GetColumnNames(config);
                        if (cols.Count == 0)
                            return Results.Ok(
                                EmptyResult(
                                    "No columns are enabled in the export mapping. Enable at least one column in Step 3."
                                )
                            );
                        var records = await DynamicExportService.ExecuteQueryAsync(
                            conn,
                            config,
                            ct,
                            limit: 50,
                            gdprDenylist: gdprDenylist
                        );
                        return Results.Ok(
                            new PreviewResult(
                                records.Count,
                                ExportSchema.Version,
                                cols,
                                records,
                                "dynamic",
                                config.SourceTable
                            )
                        );
                    }
                    catch (Exception ex)
                    {
                        return Results.Ok(EmptyResult($"Preview query failed: {ex.Message}"));
                    }
                }
            )
            .RequireAuthorization();
    }
}
