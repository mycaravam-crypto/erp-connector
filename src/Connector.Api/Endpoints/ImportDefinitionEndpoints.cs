using System.Text.Json;
using Connector.Core.DynamicExport;
using Connector.Core.DynamicImport;
using Connector.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Connector.Api.Endpoints;

/// <summary>
/// Phase 17 Slice 5 (import-definitions.md §5): CRUD, preview, and run history for
/// <see cref="ImportDefinitionEntity"/> — the API surface Slice 6's frontend and any external caller use to
/// configure an inbound mapping and inspect what it would do, without waiting on Slice 4's file watcher.
/// The four-eyes commit/reject endpoints (<c>POST /api/import-runs/{id}/release</c> and <c>.../reject</c>)
/// already shipped in Slice 3 as <see cref="ImportRunEndpoints"/> and aren't duplicated here.
///
/// This file (the route registrations) is the entry point; request validation lives in
/// <c>ImportDefinitionEndpoints.Validation.cs</c> and entity/DTO mapping plus the "create from export"
/// suggestion in <c>ImportDefinitionEndpoints.Mapping.cs</c> — split across files, kept as one `partial`
/// class rather than separate classes so the `internal` methods each already exposes to
/// <c>Connector.Integration.Tests</c> keep working unchanged. This file also owns the two save-time
/// guardrails the external design review flagged as the primary safety boundary of the whole feature (Open
/// Decisions #9 and #15): <see cref="ValidateRequestAsync"/> rejects a <c>TargetColumn</c> outside
/// <see cref="ImportDefinitionRequest.AllowedWritableColumns"/>, one that doesn't exist on its table per the
/// live introspected ERP schema, or one that's a primary key, identity/computed column, or foreign key —
/// and rejects any node whose <c>OnMissingChild</c> is <c>"insert"</c> outright, since v1 has no real
/// requirement for child-row creation yet.
/// </summary>
static partial class ImportDefinitionEndpoints
{
    internal static void MapImportDefinitionEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/api/import-definitions",
                async (ExportLogDbContext db, CancellationToken ct) =>
                {
                    var entities = await db.ImportDefinitions.OrderBy(d => d.Name).ToListAsync(ct);
                    return Results.Ok(entities.Select(ToSummaryDto).ToList());
                }
            )
            .RequireAuthorization();

        app.MapPost(
                "/api/import-definitions",
                async (
                    ImportDefinitionRequest request,
                    ExportLogDbContext db,
                    HttpContext httpContext,
                    AuditService audit,
                    CancellationToken ct
                ) =>
                {
                    var (normalizedRoot, validationError) = await ValidateRequestAsync(request, db, ct);
                    if (validationError is not null)
                        return Results.BadRequest(validationError);

                    var now = DateTimeOffset.UtcNow.ToString("O");
                    var entity = new ImportDefinitionEntity
                    {
                        Name = request.Name,
                        Description = request.Description,
                        RootTable = request.RootTable,
                        RootMatchColumn = request.RootMatchColumn,
                        RootNode = ImportNodeJson.Serialize(normalizedRoot!),
                        AllowedWritableColumns = JsonSerializer.Serialize(request.AllowedWritableColumns),
                        UnmatchedRootPolicy = request.UnmatchedRootPolicy,
                        IsEnabled = request.IsEnabled,
                        ConfigVersion = 1,
                        CreatedBy = httpContext.User.Identity!.Name!,
                        CreatedAt = now,
                        IntegrationKey = request.IntegrationKey,
                        ContractVersion = request.ContractVersion,
                    };
                    db.ImportDefinitions.Add(entity);
                    await db.SaveChangesAsync(ct);

                    await audit.LogAsync(
                        entity.CreatedBy,
                        "import_definition_created",
                        $"id={entity.Id} name={entity.Name}"
                    );
                    return Results.Created($"/api/import-definitions/{entity.Id}", ToDto(entity));
                }
            )
            .RequireAuthorization();

        app.MapGet(
                "/api/import-definitions/{id:int}",
                async (int id, ExportLogDbContext db, CancellationToken ct) =>
                {
                    var entity = await db.ImportDefinitions.FindAsync([id], ct);
                    return entity is null ? Results.NotFound() : Results.Ok(ToDto(entity));
                }
            )
            .RequireAuthorization();

        app.MapPut(
                "/api/import-definitions/{id:int}",
                async (
                    int id,
                    ImportDefinitionRequest request,
                    ExportLogDbContext db,
                    HttpContext httpContext,
                    AuditService audit,
                    CancellationToken ct
                ) =>
                {
                    var entity = await db.ImportDefinitions.FindAsync([id], ct);
                    if (entity is null)
                        return Results.NotFound();

                    var (normalizedRoot, validationError) = await ValidateRequestAsync(request, db, ct, excludeId: id);
                    if (validationError is not null)
                        return Results.BadRequest(validationError);

                    entity.Name = request.Name;
                    entity.Description = request.Description;
                    entity.RootTable = request.RootTable;
                    entity.RootMatchColumn = request.RootMatchColumn;
                    entity.RootNode = ImportNodeJson.Serialize(normalizedRoot!);
                    entity.AllowedWritableColumns = JsonSerializer.Serialize(request.AllowedWritableColumns);
                    entity.UnmatchedRootPolicy = request.UnmatchedRootPolicy;
                    entity.IsEnabled = request.IsEnabled;
                    entity.ConfigVersion++;
                    entity.UpdatedBy = httpContext.User.Identity!.Name!;
                    entity.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
                    entity.IntegrationKey = request.IntegrationKey;
                    entity.ContractVersion = request.ContractVersion;
                    await db.SaveChangesAsync(ct);

                    await audit.LogAsync(
                        entity.UpdatedBy,
                        "import_definition_updated",
                        $"id={entity.Id} name={entity.Name} version={entity.ConfigVersion}"
                    );
                    return Results.Ok(ToDto(entity));
                }
            )
            .RequireAuthorization();

        app.MapDelete(
                "/api/import-definitions/{id:int}",
                async (
                    int id,
                    ExportLogDbContext db,
                    HttpContext httpContext,
                    AuditService audit,
                    CancellationToken ct
                ) =>
                {
                    var entity = await db.ImportDefinitions.FindAsync([id], ct);
                    if (entity is null)
                        return Results.NotFound();

                    db.ImportDefinitions.Remove(entity);
                    await db.SaveChangesAsync(ct);

                    await audit.LogAsync(
                        httpContext.User.Identity!.Name!,
                        "import_definition_deleted",
                        $"id={id} name={entity.Name}"
                    );
                    return Results.NoContent();
                }
            )
            .RequireAuthorization();

        app.MapPost(
                "/api/import-definitions/{id:int}/duplicate",
                async (
                    int id,
                    DuplicateImportDefinitionRequest? request,
                    ExportLogDbContext db,
                    HttpContext httpContext,
                    AuditService audit,
                    CancellationToken ct
                ) =>
                {
                    var source = await db.ImportDefinitions.FindAsync([id], ct);
                    if (source is null)
                        return Results.NotFound();

                    var now = DateTimeOffset.UtcNow.ToString("O");
                    var copy = new ImportDefinitionEntity
                    {
                        Name = string.IsNullOrWhiteSpace(request?.Name) ? $"{source.Name} (Copy)" : request!.Name,
                        Description = source.Description,
                        RootTable = source.RootTable,
                        RootMatchColumn = source.RootMatchColumn,
                        RootNode = source.RootNode,
                        AllowedWritableColumns = source.AllowedWritableColumns,
                        UnmatchedRootPolicy = source.UnmatchedRootPolicy,
                        // A duplicate starts disabled, same as ExportDefinitionEndpoints' own duplicate — an
                        // operator opts each copy back in explicitly rather than silently doubling up writes.
                        IsEnabled = false,
                        ConfigVersion = 1,
                        CreatedBy = httpContext.User.Identity!.Name!,
                        CreatedAt = now,
                    };
                    db.ImportDefinitions.Add(copy);
                    await db.SaveChangesAsync(ct);

                    await audit.LogAsync(
                        copy.CreatedBy,
                        "import_definition_duplicated",
                        $"id={copy.Id} name={copy.Name} from={id}"
                    );
                    return Results.Created($"/api/import-definitions/{copy.Id}", ToDto(copy));
                }
            )
            .RequireAuthorization();

        app.MapPatch(
                "/api/import-definitions/{id:int}/enable",
                async (
                    int id,
                    EnableRequest request,
                    ExportLogDbContext db,
                    HttpContext httpContext,
                    AuditService audit,
                    CancellationToken ct
                ) =>
                {
                    var entity = await db.ImportDefinitions.FindAsync([id], ct);
                    if (entity is null)
                        return Results.NotFound();

                    if (request.Enabled)
                    {
                        var pairError = await ValidateIntegrationKeyPairEnabledAsync(
                            db,
                            entity.IntegrationKey,
                            entity.ContractVersion,
                            excludeId: id,
                            ct
                        );
                        if (pairError is not null)
                            return Results.BadRequest(pairError);
                    }

                    entity.IsEnabled = request.Enabled;
                    entity.UpdatedBy = httpContext.User.Identity!.Name!;
                    entity.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
                    await db.SaveChangesAsync(ct);

                    await audit.LogAsync(
                        entity.UpdatedBy,
                        request.Enabled ? "import_definition_enabled" : "import_definition_disabled",
                        $"id={id} name={entity.Name}"
                    );
                    return Results.Ok(ToDto(entity));
                }
            )
            .RequireAuthorization();

        // Parses + walks + plans an inbound file against a saved definition with zero persistence: no
        // ImportRunEntity row is created and nothing is written to the ERP (mirrors ExportDefinitionEndpoints'
        // own untracked preview). The operator supplies the file content directly since Slice 4's inbound/
        // folder watcher doesn't exist yet.
        app.MapPost(
                "/api/import-definitions/{id:int}/preview",
                async (int id, ImportDefinitionPreviewRequest request, ExportLogDbContext db, CancellationToken ct) =>
                {
                    var def = await db.ImportDefinitions.FindAsync([id], ct);
                    if (def is null)
                        return Results.NotFound();

                    var root = ImportNodeJson.Deserialize(def.RootNode);
                    if (root is null)
                        return Results.Problem(detail: "Stored import tree could not be read.", statusCode: 500);

                    var connRaw = await db.GetSettingRawAsync(SettingsKeys.ErpConnection);
                    if (connRaw is null)
                        return Results.Problem(detail: "No database connection configured.", statusCode: 400);
                    var connCfg = JsonSerializer.Deserialize<ErpConnectionConfig>(connRaw)!;

                    try
                    {
                        await using var conn = new NpgsqlConnection(
                            DynamicExportService.BuildConnectionString(connCfg)
                        );
                        await conn.OpenAsync(ct);

                        var walkResult = await ImportNodeWalker.WalkAsync(conn, def, root, request.InboundJson, ct);
                        var plan = ImportPlanBuilder.Build(walkResult, def.RootTable, def.RootMatchColumn);
                        return Results.Ok(plan);
                    }
                    catch (ImportValidationException ex)
                    {
                        return Results.BadRequest(ex.Message);
                    }
                    catch (Exception ex)
                    {
                        return Results.Problem(detail: $"Preview failed: {ex.Message}", statusCode: 400);
                    }
                }
            )
            .RequireAuthorization();

        // Slice 4 (knowledge/pipeline/import-mapping-presets.md §3.4/§4): the "Create from export"
        // suggestion the New Import Definition flow offers. Takes the same sample ImportEnvelope JSON the
        // preview panel already accepts and looks for an ExportDefinition whose provenance pair matches it.
        // No persistence, no ERP connection — this only ever reads ExportDefinitions. Always 200 OK — a
        // miss is a normal lookup outcome, never a 4xx — but unlike Slice 3's pure
        // ImportMappingSuggestion.SuggestFrom, a miss here always carries a `Reason` explaining which gate
        // stopped it (malformed/no-provenance sample, no export tagged with that key/version, one tagged
        // but disabled, or one tagged and enabled but missing CorrelationKeySourceField) instead of a flat
        // null, so the operator isn't left guessing why a provenance-carrying paste "wasn't recognized."
        app.MapPost(
                "/api/import-definitions/suggest-from-export",
                async (ImportMappingSuggestionRequest request, ExportLogDbContext db, CancellationToken ct) =>
                    Results.Ok(await BuildSuggestionAsync(request.InboundJson, db, ct))
            )
            .RequireAuthorization();

        app.MapGet(
                "/api/import-definitions/{id:int}/runs",
                async (int id, ExportLogDbContext db, CancellationToken ct) =>
                {
                    if (!await db.ImportDefinitions.AnyAsync(d => d.Id == id, ct))
                        return Results.NotFound();

                    var runs = await db
                        .ImportRuns.Where(r => r.ImportDefinitionId == id)
                        .OrderByDescending(r => r.Id)
                        .Take(200)
                        .Select(r => new ImportDefinitionRunDto(
                            r.Id,
                            r.ConfigVersion,
                            r.StartedAt,
                            r.FinishedAt,
                            r.Status,
                            r.RecordCount,
                            r.MatchedCount,
                            r.ChangedCount,
                            r.UnchangedCount,
                            r.RejectedCount,
                            r.ConflictCount,
                            r.InvalidCount,
                            r.ErrorMessage,
                            r.TriggeredBy,
                            r.OperatedBy,
                            r.ApprovedBy,
                            r.ReleasedAt
                        ))
                        .ToListAsync(ct);
                    return Results.Ok(runs);
                }
            )
            .RequireAuthorization();
    }
}
