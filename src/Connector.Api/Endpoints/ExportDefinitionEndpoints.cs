using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Connector.Core.DynamicExport;
using Connector.Core.Schema;
using Connector.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Connector.Api.Endpoints;

/// <summary>
/// Phase 14 Slice 3 — CRUD, manual trigger, test, preview, and run history for
/// <see cref="ExportDefinitionEntity"/>: the generic, named, tree-based replacement for the legacy single
/// export mapping (see knowledge/pipeline/export-definitions-2.0.md). This is the surface an external
/// program uses to configure a saved export once and trigger it later via a single authenticated
/// POST — <c>POST /api/export-definitions/{id}/run</c> returns the built artifact bytes directly.
///
/// Deliberately does not reuse <see cref="ExportRunEntity"/>/<see cref="FileSystemExportSink"/>/four-eyes
/// release: those model the legacy CI-to-ServiceNow staging contract (sequence numbers, physical staging
/// folder, approval workflow), which export-definitions-2.0.md §10 explicitly keeps out of scope for
/// generic exports. A run here is synchronous request/response plus one <see cref="ExportDefinitionRunEntity"/>
/// history row — no separate storage or delivery mechanism is introduced.
/// </summary>
static class ExportDefinitionEndpoints
{
    // Fixed per export-definitions-2.0.md §11 decision #3 — not user-configurable in this phase.
    private const int TestRunLimit = 50;
    private const int PreviewLimit = 50;

    internal static void MapExportDefinitionEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/api/export-definitions",
                async (ExportLogDbContext db, CancellationToken ct) =>
                {
                    // Materialize first, map after: ToSummaryDto is a plain C# method, not a translatable
                    // SQL expression, so it can't live inside the IQueryable .Select() itself.
                    var entities = await db.ExportDefinitions.OrderBy(d => d.Name).ToListAsync(ct);
                    return Results.Ok(entities.Select(ToSummaryDto).ToList());
                }
            )
            .RequireAuthorization();

        app.MapPost(
                "/api/export-definitions",
                async (
                    ExportDefinitionRequest request,
                    ExportLogDbContext db,
                    HttpContext httpContext,
                    AuditService audit,
                    CancellationToken ct
                ) =>
                {
                    var (normalizedRoot, validationError) = await ValidateRequestAsync(request, db);
                    if (validationError is not null)
                        return Results.BadRequest(validationError);

                    var now = DateTimeOffset.UtcNow.ToString("O");
                    var entity = new ExportDefinitionEntity
                    {
                        Name = request.Name,
                        Description = request.Description,
                        RootTable = request.RootTable,
                        RootNode = ExportNodeJson.Serialize(normalizedRoot!),
                        OutputFormat = request.OutputFormat,
                        IsEnabled = request.IsEnabled,
                        Schedule = request.Schedule,
                        ConfigVersion = 1,
                        CreatedBy = httpContext.User.Identity!.Name!,
                        CreatedAt = now,
                    };
                    db.ExportDefinitions.Add(entity);
                    await db.SaveChangesAsync(ct);

                    await audit.LogAsync(
                        entity.CreatedBy,
                        "export_definition_created",
                        $"id={entity.Id} name={entity.Name}"
                    );
                    return Results.Created($"/api/export-definitions/{entity.Id}", ToDto(entity));
                }
            )
            .RequireAuthorization();

        app.MapGet(
                "/api/export-definitions/{id:int}",
                async (int id, ExportLogDbContext db, CancellationToken ct) =>
                {
                    var entity = await db.ExportDefinitions.FindAsync([id], ct);
                    return entity is null ? Results.NotFound() : Results.Ok(ToDto(entity));
                }
            )
            .RequireAuthorization();

        app.MapPut(
                "/api/export-definitions/{id:int}",
                async (
                    int id,
                    ExportDefinitionRequest request,
                    ExportLogDbContext db,
                    HttpContext httpContext,
                    AuditService audit,
                    CancellationToken ct
                ) =>
                {
                    var entity = await db.ExportDefinitions.FindAsync([id], ct);
                    if (entity is null)
                        return Results.NotFound();

                    var (normalizedRoot, validationError) = await ValidateRequestAsync(request, db);
                    if (validationError is not null)
                        return Results.BadRequest(validationError);

                    entity.Name = request.Name;
                    entity.Description = request.Description;
                    entity.RootTable = request.RootTable;
                    entity.RootNode = ExportNodeJson.Serialize(normalizedRoot!);
                    entity.OutputFormat = request.OutputFormat;
                    entity.IsEnabled = request.IsEnabled;
                    entity.Schedule = request.Schedule;
                    entity.ConfigVersion++;
                    entity.UpdatedBy = httpContext.User.Identity!.Name!;
                    entity.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
                    await db.SaveChangesAsync(ct);

                    await audit.LogAsync(
                        entity.UpdatedBy,
                        "export_definition_updated",
                        $"id={entity.Id} name={entity.Name} version={entity.ConfigVersion}"
                    );
                    return Results.Ok(ToDto(entity));
                }
            )
            .RequireAuthorization();

        app.MapDelete(
                "/api/export-definitions/{id:int}",
                async (
                    int id,
                    ExportLogDbContext db,
                    HttpContext httpContext,
                    AuditService audit,
                    CancellationToken ct
                ) =>
                {
                    var entity = await db.ExportDefinitions.FindAsync([id], ct);
                    if (entity is null)
                        return Results.NotFound();

                    db.ExportDefinitions.Remove(entity);
                    await db.SaveChangesAsync(ct);

                    await audit.LogAsync(
                        httpContext.User.Identity!.Name!,
                        "export_definition_deleted",
                        $"id={id} name={entity.Name}"
                    );
                    return Results.NoContent();
                }
            )
            .RequireAuthorization();

        app.MapPost(
                "/api/export-definitions/{id:int}/duplicate",
                async (
                    int id,
                    DuplicateExportDefinitionRequest? request,
                    ExportLogDbContext db,
                    HttpContext httpContext,
                    AuditService audit,
                    CancellationToken ct
                ) =>
                {
                    var source = await db.ExportDefinitions.FindAsync([id], ct);
                    if (source is null)
                        return Results.NotFound();

                    var now = DateTimeOffset.UtcNow.ToString("O");
                    var copy = new ExportDefinitionEntity
                    {
                        Name = string.IsNullOrWhiteSpace(request?.Name) ? $"{source.Name} (Copy)" : request!.Name,
                        Description = source.Description,
                        RootTable = source.RootTable,
                        RootNode = source.RootNode,
                        OutputFormat = source.OutputFormat,
                        // A duplicate starts manual-only and disabled, same as a freshly migrated definition —
                        // an operator opts each copy into scheduling explicitly rather than silently doubling
                        // up whatever schedule the original ran on.
                        IsEnabled = false,
                        Schedule = null,
                        ConfigVersion = 1,
                        CreatedBy = httpContext.User.Identity!.Name!,
                        CreatedAt = now,
                    };
                    db.ExportDefinitions.Add(copy);
                    await db.SaveChangesAsync(ct);

                    await audit.LogAsync(
                        copy.CreatedBy,
                        "export_definition_duplicated",
                        $"id={copy.Id} name={copy.Name} from={id}"
                    );
                    return Results.Created($"/api/export-definitions/{copy.Id}", ToDto(copy));
                }
            )
            .RequireAuthorization();

        app.MapPatch(
                "/api/export-definitions/{id:int}/enable",
                async (
                    int id,
                    EnableRequest request,
                    ExportLogDbContext db,
                    HttpContext httpContext,
                    AuditService audit,
                    CancellationToken ct
                ) =>
                {
                    var entity = await db.ExportDefinitions.FindAsync([id], ct);
                    if (entity is null)
                        return Results.NotFound();

                    entity.IsEnabled = request.Enabled;
                    entity.UpdatedBy = httpContext.User.Identity!.Name!;
                    entity.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
                    await db.SaveChangesAsync(ct);

                    await audit.LogAsync(
                        entity.UpdatedBy,
                        request.Enabled ? "export_definition_enabled" : "export_definition_disabled",
                        $"id={id} name={entity.Name}"
                    );
                    return Results.Ok(ToDto(entity));
                }
            )
            .RequireAuthorization();

        app.MapPost(
                "/api/export-definitions/{id:int}/preview",
                async (int id, ExportLogDbContext db, CancellationToken ct) =>
                {
                    var def = await db.ExportDefinitions.FindAsync([id], ct);
                    if (def is null)
                        return Results.NotFound();

                    var root = ExportNodeJson.Deserialize(def.RootNode);
                    if (root is null)
                        return Results.Problem(detail: "Stored export tree could not be read.", statusCode: 500);

                    var connRaw = await db.GetSettingRawAsync(SettingsKeys.ErpConnection);
                    if (connRaw is null)
                        return Results.Ok(new ExportDefinitionPreviewDto(0, []));
                    var connCfg = JsonSerializer.Deserialize<ErpConnectionConfig>(connRaw)!;

                    try
                    {
                        var gdprDenylist = await DynamicExportService.GetDeniedFieldsAsync(db);
                        await using var conn = new NpgsqlConnection(
                            DynamicExportService.BuildConnectionString(connCfg)
                        );
                        await conn.OpenAsync(ct);

                        var records = await DynamicExportService.ExecuteExportNodeQueryAsync(
                            conn,
                            def.RootTable,
                            root,
                            ct,
                            limit: PreviewLimit,
                            gdprDenylist: gdprDenylist
                        );
                        return Results.Ok(
                            new ExportDefinitionPreviewDto(
                                records.Count,
                                new JsonArray(records.Select(r => (JsonNode?)r.DeepClone()).ToArray())
                            )
                        );
                    }
                    catch (Exception ex)
                    {
                        return Results.Problem(detail: $"Preview query failed: {ex.Message}", statusCode: 400);
                    }
                }
            )
            .RequireAuthorization();

        app.MapPost(
                "/api/export-definitions/{id:int}/run",
                async (
                    int id,
                    ExportLogDbContext db,
                    HttpContext httpContext,
                    AuditService audit,
                    CancellationToken ct
                ) =>
                {
                    var def = await db.ExportDefinitions.FindAsync([id], ct);
                    if (def is null)
                        return Results.NotFound();

                    var user = httpContext.User.Identity!.Name!;
                    var (run, built, error) = await ExecuteDefinitionAsync(
                        def,
                        db,
                        triggeredBy: user,
                        isTestRun: false,
                        limit: null,
                        ct
                    );

                    if (built is null)
                    {
                        await audit.LogAsync(user, "export_definition_run_failed", $"id={id} name={def.Name}: {error}");
                        return Results.Problem(detail: error, statusCode: 500);
                    }

                    await audit.LogAsync(
                        user,
                        "export_definition_run",
                        $"id={id} name={def.Name} records={built.Value.RecordCount}"
                    );

                    httpContext.Response.Headers["X-Export-Run-Id"] = run.Id.ToString();
                    httpContext.Response.Headers["X-Record-Count"] = built.Value.RecordCount.ToString();
                    httpContext.Response.Headers["X-Config-Version"] = run.ConfigVersion.ToString();

                    var fileName = BuildRunFileName(def.Name, DateTimeOffset.UtcNow, built.Value.Extension);
                    return Results.File(built.Value.Bytes, ContentTypeFor(built.Value.Extension), fileName);
                }
            )
            .RequireAuthorization();

        app.MapPost(
                "/api/export-definitions/{id:int}/test",
                async (
                    int id,
                    ExportLogDbContext db,
                    HttpContext httpContext,
                    AuditService audit,
                    CancellationToken ct
                ) =>
                {
                    var def = await db.ExportDefinitions.FindAsync([id], ct);
                    if (def is null)
                        return Results.NotFound();

                    var user = httpContext.User.Identity!.Name!;
                    var (run, built, error) = await ExecuteDefinitionAsync(
                        def,
                        db,
                        triggeredBy: user,
                        isTestRun: true,
                        limit: TestRunLimit,
                        ct
                    );

                    await audit.LogAsync(
                        user,
                        built is null ? "export_definition_test_failed" : "export_definition_test",
                        built is null
                            ? $"id={id} name={def.Name}: {error}"
                            : $"id={id} name={def.Name} records={built.Value.RecordCount}"
                    );

                    return Results.Ok(
                        new ExportDefinitionRunResultDto(
                            run.Id,
                            run.Status,
                            run.RecordCount,
                            run.ConfigVersion,
                            run.StartedAt,
                            run.FinishedAt,
                            run.ErrorMessage,
                            run.IsTestRun
                        )
                    );
                }
            )
            .RequireAuthorization();

        app.MapGet(
                "/api/export-definitions/{id:int}/runs",
                async (int id, ExportLogDbContext db, CancellationToken ct) =>
                {
                    if (!await db.ExportDefinitions.AnyAsync(d => d.Id == id, ct))
                        return Results.NotFound();

                    var runs = await db
                        .ExportDefinitionRuns.Where(r => r.ExportDefinitionId == id)
                        .OrderByDescending(r => r.Id)
                        .Take(200)
                        .Select(r => new ExportDefinitionRunDto(
                            r.Id,
                            r.ConfigVersion,
                            r.StartedAt,
                            r.FinishedAt,
                            r.Status,
                            r.RecordCount,
                            r.ErrorMessage,
                            r.TriggeredBy,
                            r.IsTestRun
                        ))
                        .ToListAsync(ct);
                    return Results.Ok(runs);
                }
            )
            .RequireAuthorization();
    }

    // Shared by /run and /test: creates the ExportDefinitionRunEntity row up front (so a crash mid-query
    // still leaves a Failed row, never a silent gap), runs the query+format-writer engine, and updates the
    // row with the outcome. Callers differ only in the limit passed and how they render the result — the
    // execution path itself is identical, matching export-definitions-2.0.md §6's "never a separate
    // untracked code path" requirement for Test.
    private static async Task<(
        ExportDefinitionRunEntity Run,
        DynamicExportService.ExportBuildResult? Built,
        string? Error
    )> ExecuteDefinitionAsync(
        ExportDefinitionEntity def,
        ExportLogDbContext db,
        string triggeredBy,
        bool isTestRun,
        int? limit,
        CancellationToken ct
    )
    {
        var run = new ExportDefinitionRunEntity
        {
            ExportDefinitionId = def.Id,
            ConfigVersion = def.ConfigVersion,
            StartedAt = DateTimeOffset.UtcNow.ToString("O"),
            Status = ExportDefinitionRunStatus.Running,
            TriggeredBy = triggeredBy,
            IsTestRun = isTestRun,
        };
        db.ExportDefinitionRuns.Add(run);
        await db.SaveChangesAsync(ct);

        async Task<(ExportDefinitionRunEntity, DynamicExportService.ExportBuildResult?, string?)> Fail(string message)
        {
            run.Status = ExportDefinitionRunStatus.Failed;
            run.FinishedAt = DateTimeOffset.UtcNow.ToString("O");
            run.ErrorMessage = message;
            await db.SaveChangesAsync(CancellationToken.None);
            return (run, null, message);
        }

        // Everything from here on — including the two deserialize calls, which throw rather than return
        // null on malformed stored JSON — stays inside this try so a corrupt RootNode/connection config
        // still finalizes the run row as Failed instead of leaving it stuck at Running forever.
        try
        {
            var root = ExportNodeJson.Deserialize(def.RootNode);
            if (root is null)
                return await Fail("Stored export tree could not be read.");

            var connRaw = await db.GetSettingRawAsync(SettingsKeys.ErpConnection);
            if (connRaw is null)
                return await Fail("No database connection configured.");
            var connCfg = JsonSerializer.Deserialize<ErpConnectionConfig>(connRaw);
            if (connCfg is null)
                return await Fail("Stored database connection config could not be read.");

            var extractedAt = DateTimeOffset.UtcNow;
            var gdprDenylist = await DynamicExportService.GetDeniedFieldsAsync(db);

            await using var conn = new NpgsqlConnection(DynamicExportService.BuildConnectionString(connCfg));
            await conn.OpenAsync(ct);

            var built = await DynamicExportService.BuildExportNodeAsync(
                conn,
                def.RootTable,
                root,
                def.OutputFormat,
                ExportSchema.Version,
                extractedAt,
                ct,
                limit,
                gdprDenylist
            );

            run.Status = ExportDefinitionRunStatus.Success;
            run.RecordCount = built.RecordCount;
            run.FinishedAt = DateTimeOffset.UtcNow.ToString("O");
            await db.SaveChangesAsync(ct);

            return (run, built, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await Fail(ex.Message);
        }
    }

    private static string ContentTypeFor(string extension) =>
        extension switch
        {
            "csv" => "text/csv",
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/json",
        };

    // Definition names are free text (unlike RootTable/JoinKey/etc., never spliced into SQL), so they need
    // their own filesystem-safe slug rather than the SqlIdentifierRegex used for query-facing inputs below.
    private static string BuildRunFileName(string definitionName, DateTimeOffset extractedAt, string extension)
    {
        var slug = new string(definitionName.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        if (slug.Length == 0)
            slug = "export";
        return $"{slug}_{extractedAt:yyyyMMdd'T'HHmmss'Z'}.{extension}";
    }

    private static ExportDefinitionDto ToDto(ExportDefinitionEntity e) =>
        new(
            e.Id,
            e.Name,
            e.Description,
            e.RootTable,
            ExportNodeJson.Deserialize(e.RootNode)!,
            e.OutputFormat,
            e.IsEnabled,
            e.Schedule,
            e.ConfigVersion,
            e.CreatedBy,
            e.CreatedAt,
            e.UpdatedBy,
            e.UpdatedAt
        );

    private static ExportDefinitionSummaryDto ToSummaryDto(ExportDefinitionEntity e) =>
        new(
            e.Id,
            e.Name,
            e.Description,
            e.RootTable,
            e.OutputFormat,
            e.IsEnabled,
            e.Schedule,
            e.ConfigVersion,
            e.CreatedBy,
            e.CreatedAt,
            e.UpdatedBy,
            e.UpdatedAt
        );

    // Valid SQL identifier: letters/digits/underscore, not starting with a digit — mirrors
    // ExportMappingEndpoints.SqlIdentifierRegex, applied here to every identifier field of an ExportNode tree
    // (RootTable/RelatedTable/JoinKey/SourceJoinKey/SourceField) before it can reach DynamicExportService's
    // QI()-based query builder. Deliberately NOT applied to Filter: that field is a WHERE-clause fragment by
    // design (export-definitions-2.0.md §4), not an identifier, so it only gets the same control-character
    // check every free-text export key gets.
    private static readonly Regex SqlIdentifierRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    // Returns the normalized RootNode on success (null on failure) alongside the error, so callers store
    // exactly the tree that was validated instead of re-normalizing (or re-validating null-prone raw
    // input) a second time.
    private static async Task<(ExportNode? Root, string? Error)> ValidateRequestAsync(
        ExportDefinitionRequest request,
        ExportLogDbContext db
    )
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return (null, "Name is required.");
        if (string.IsNullOrWhiteSpace(request.RootTable) || !SqlIdentifierRegex.IsMatch(request.RootTable))
            return (null, "RootTable is required and must be a valid identifier.");
        if (request.OutputFormat is not ("csv" or "xlsx" or "json"))
            return (null, "OutputFormat must be one of: csv, xlsx, json.");
        if (
            request.Schedule is not null
            && request.Schedule.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != 5
        )
            return (null, "Schedule must be a 5-field cron expression, or null for manual-only.");
        if (request.RootNode is null)
            return (null, "RootNode is required.");

        // The request body is bound by plain System.Text.Json, not ExportNodeJson, so a node that
        // naturally omits "children"/"mapping" (e.g. a hand-written scalar-field node from an external
        // caller) binds those to null rather than []/defaults. Round-tripping through ExportNodeJson here
        // applies the same missing-property backfill every persisted tree already gets, before anything
        // below dereferences .Children.
        var root = ExportNodeJson.Deserialize(ExportNodeJson.Serialize(request.RootNode))!;

        if (root.Kind != ExportNodeKind.Root)
            return (null, $"RootNode.Kind must be \"{ExportNodeKind.Root}\" (got \"{root.Kind}\").");

        var denylist = await DynamicExportService.GetDeniedFieldsAsync(db);
        var topLevelKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in root.Children.Where(c => c.Enabled))
        {
            if (!topLevelKeys.Add(child.TargetKey))
                return (null, $"Duplicate export key '{child.TargetKey}' at the top level.");

            var error = ValidateNode(child, denylist, path: child.TargetKey, depth: 1);
            if (error is not null)
                return (null, error);
        }

        return topLevelKeys.Count == 0
            ? (null, "The export must have at least one enabled field or nested group.")
            : (root, null);
    }

    // Recursive validator over the ExportNode tree: depth guard, identifier-safety, GDPR denylist, and
    // duplicate-key checks at every depth — the ExportNode counterpart of
    // ExportMappingEndpoints.ValidateNestedGroup, generalized for the unified scalar-field/object/array shape.
    private static string? ValidateNode(ExportNode node, IReadOnlySet<string> denylist, string path, int depth)
    {
        if (depth > DynamicExportService.MaxNestedDepth)
            return $"Node '{path}' exceeds the maximum nesting depth of {DynamicExportService.MaxNestedDepth}.";

        if (string.IsNullOrWhiteSpace(node.TargetKey) || ContainsControlCharacters(node.TargetKey))
            return $"Node '{path}': TargetKey must be non-empty and free of control characters.";

        switch (node.Kind)
        {
            case ExportNodeKind.ScalarField:
                if (string.IsNullOrWhiteSpace(node.SourceField) || !SqlIdentifierRegex.IsMatch(node.SourceField))
                    return $"Node '{path}': SourceField is required and must be a valid identifier.";
                if (node.Children is { Length: > 0 })
                    return $"Node '{path}': a scalar field cannot have child nodes.";
                if (denylist.Contains(node.SourceField))
                    return $"GDPR violation at '{path}': '{node.SourceField}' is personal data and must not be "
                        + "exported (GDPR Art. 5(1)(c) — data minimisation).";
                return null;

            case ExportNodeKind.Object:
            case ExportNodeKind.Array:
                if (string.IsNullOrWhiteSpace(node.RelatedTable) || !SqlIdentifierRegex.IsMatch(node.RelatedTable))
                    return $"Node '{path}': RelatedTable is required and must be a valid identifier.";
                if (string.IsNullOrWhiteSpace(node.JoinKey) || !SqlIdentifierRegex.IsMatch(node.JoinKey))
                    return $"Node '{path}': JoinKey is required and must be a valid identifier.";
                if (string.IsNullOrWhiteSpace(node.SourceJoinKey) || !SqlIdentifierRegex.IsMatch(node.SourceJoinKey))
                    return $"Node '{path}': SourceJoinKey is required and must be a valid identifier.";

                var enabledChildren = node.Children.Where(c => c.Enabled).ToList();
                if (enabledChildren.Count == 0)
                    return $"Node '{path}' must have at least one enabled field or nested group.";

                var siblingKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var child in enabledChildren)
                {
                    if (!siblingKeys.Add(child.TargetKey))
                        return $"Duplicate export key '{child.TargetKey}' within '{path}'.";

                    var error = ValidateNode(child, denylist, $"{path}.{child.TargetKey}", depth + 1);
                    if (error is not null)
                        return error;
                }
                return null;

            default:
                return $"Node '{path}': Kind must be \"{ExportNodeKind.ScalarField}\", \"{ExportNodeKind.Object}\", "
                    + $"or \"{ExportNodeKind.Array}\" (got \"{node.Kind}\").";
        }
    }

    private static bool ContainsControlCharacters(string s) => s.Any(char.IsControl);
}
