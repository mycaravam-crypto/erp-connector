using System.Security.Cryptography;
using System.Text.Json;
using Connector.Core.Domain;
using Connector.Core.DynamicExport;
using Connector.Core.Interfaces;
using Connector.Core.Schema;
using Connector.Infrastructure;
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
                    IExportSink sink,
                    ILogger<Program> logger,
                    AuditService audit,
                    HttpContext httpContext,
                    CancellationToken ct
                ) =>
                {
                    var fmt = format?.ToLowerInvariant() switch
                    {
                        "csv" => "csv",
                        "json" => "json",
                        _ => "xlsx",
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

                    var mappingSetting = await db.AppSettings.FindAsync("export_mapping");
                    if (mappingSetting is null)
                    {
                        run.Status = ExportRunStatus.Failed;
                        await db.SaveChangesAsync(ct);
                        return Results.Problem(
                            detail: "No export mapping configured. Go to Step 3 and save an export mapping first.",
                            statusCode: 400
                        );
                    }
                    var config = JsonSerializer.Deserialize<ExportMappingConfig>(mappingSetting.Value)!;

                    var connSetting = await db.AppSettings.FindAsync("erp_connection");
                    if (connSetting is null)
                    {
                        run.Status = ExportRunStatus.Failed;
                        await db.SaveChangesAsync(ct);
                        return Results.Problem(
                            detail: "No database connection configured. Go to Step 1 and save a connection first.",
                            statusCode: 400
                        );
                    }
                    var connCfg = JsonSerializer.Deserialize<ErpConnectionConfig>(connSetting.Value)!;

                    try
                    {
                        var cols = DynamicExportService.GetColumnNames(config);
                        var extractedAt = DateTimeOffset.UtcNow;
                        var gdprDenylist = await DynamicExportService.GetDeniedFieldsAsync(db);

                        await using var pgConn = new NpgsqlConnection(
                            DynamicExportService.BuildConnectionString(connCfg)
                        );
                        await pgConn.OpenAsync(ct);
                        var records = await DynamicExportService.ExecuteQueryAsync(
                            pgConn,
                            config,
                            ct,
                            gdprDenylist: gdprDenylist
                        );

                        if (records.Count == 0)
                        {
                            run.Status = ExportRunStatus.Failed;
                            await db.SaveChangesAsync(ct);
                            return Results.Problem(
                                detail: "Export aborted: query returned 0 records. Check that your mapping includes the maintenance_plan scope predicate.",
                                statusCode: 400
                            );
                        }

                        byte[] bytes;
                        string fileName;
                        if (fmt == "csv")
                        {
                            bytes = DynamicExportService.BuildCsvBytes(
                                records,
                                cols,
                                ExportSchema.Version,
                                extractedAt
                            );
                            fileName = ExportSchema.BuildFileName(sequenceNo, extractedAt, "csv");
                        }
                        else if (fmt == "json")
                        {
                            bytes = DynamicExportService.BuildJsonBytes(records, ExportSchema.Version, extractedAt);
                            fileName = ExportSchema.BuildFileName(sequenceNo, extractedAt, "json");
                        }
                        else
                        {
                            bytes = DynamicExportService.BuildExcelBytes(
                                records,
                                cols,
                                ExportSchema.Version,
                                extractedAt
                            );
                            fileName = ExportSchema.BuildFileName(sequenceNo, extractedAt);
                        }

                        var checksum = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                        var package = new ExportPackage(
                            new ExportManifest(sequenceNo, ExportSchema.Version, extractedAt, records.Count, checksum),
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

        app.MapGet(
                "/api/pipeline/preview",
                async (ExportLogDbContext db, CancellationToken ct) =>
                {
                    var mappingConfigSetting = await db.AppSettings.FindAsync("export_mapping");
                    if (mappingConfigSetting is null)
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

                    var config = JsonSerializer.Deserialize<ExportMappingConfig>(mappingConfigSetting.Value)!;

                    PreviewResult EmptyResult(string msg) =>
                        new(0, ExportSchema.Version, [], [], "error", config.SourceTable, msg);

                    var connSetting = await db.AppSettings.FindAsync("erp_connection");
                    if (connSetting is null)
                        return Results.Ok(EmptyResult("No database connection configured. Set it up in Step 1."));

                    var connCfg = JsonSerializer.Deserialize<ErpConnectionConfig>(connSetting.Value);
                    if (connCfg is null)
                        return Results.Ok(EmptyResult("Stored connection config could not be read."));

                    try
                    {
                        var gdprDenylist = await DynamicExportService.GetDeniedFieldsAsync(db);
                        await using var conn = new NpgsqlConnection(
                            DynamicExportService.BuildConnectionString(connCfg)
                        );
                        await conn.OpenAsync(ct);
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
