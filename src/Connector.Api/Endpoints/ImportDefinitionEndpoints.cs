using System.Text.Json;
using System.Text.RegularExpressions;
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
/// This file also owns the two save-time guardrails the external design review flagged as the primary
/// safety boundary of the whole feature (Open Decisions #9 and #15): <see cref="ValidateRequestAsync"/>
/// rejects a <c>TargetColumn</c> outside <see cref="ImportDefinitionRequest.AllowedWritableColumns"/>, one
/// that doesn't exist on its table per the live introspected ERP schema, or one that's a primary key,
/// identity/computed column, or foreign key — and rejects any node whose <c>OnMissingChild</c> is
/// <c>"insert"</c> outright, since v1 has no real requirement for child-row creation yet.
/// </summary>
static class ImportDefinitionEndpoints
{
    // Same identifier-safety posture as ExportDefinitionEndpoints.SqlIdentifierRegex — deliberately
    // duplicated rather than shared, matching that file's own precedent (it duplicates
    // ExportMappingEndpoints.SqlIdentifierRegex for the same reason: a one-line regex isn't worth a shared
    // helper type across two independent validators).
    private static readonly Regex SqlIdentifierRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

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

                    var (normalizedRoot, validationError) = await ValidateRequestAsync(request, db, ct);
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
                async (
                    int id,
                    ImportDefinitionPreviewRequest request,
                    ExportLogDbContext db,
                    CancellationToken ct
                ) =>
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

    private static ImportDefinitionDto ToDto(ImportDefinitionEntity e) =>
        new(
            e.Id,
            e.Name,
            e.Description,
            e.RootTable,
            e.RootMatchColumn,
            ImportNodeJson.Deserialize(e.RootNode)!,
            JsonSerializer.Deserialize<List<string>>(e.AllowedWritableColumns) ?? [],
            e.UnmatchedRootPolicy,
            e.IsEnabled,
            e.ConfigVersion,
            e.CreatedBy,
            e.CreatedAt,
            e.UpdatedBy,
            e.UpdatedAt
        );

    private static ImportDefinitionSummaryDto ToSummaryDto(ImportDefinitionEntity e) =>
        new(
            e.Id,
            e.Name,
            e.Description,
            e.RootTable,
            e.UnmatchedRootPolicy,
            e.IsEnabled,
            e.ConfigVersion,
            e.CreatedBy,
            e.CreatedAt,
            e.UpdatedBy,
            e.UpdatedAt
        );

    /// <summary>
    /// Validates a create/update request end to end and returns the normalized <see cref="ImportNode"/> tree
    /// to persist. `internal` rather than `private` so <c>Connector.Integration.Tests</c> can exercise the
    /// schema-aware AllowedWritableColumns check directly against a real Postgres schema — the acceptance
    /// criteria for this slice require proving each specific rejection reason, not just that *some* 400 comes
    /// back.
    /// </summary>
    internal static async Task<(ImportNode? Root, string? Error)> ValidateRequestAsync(
        ImportDefinitionRequest request,
        ExportLogDbContext db,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return (null, "Name is required.");
        if (string.IsNullOrWhiteSpace(request.RootTable) || !SqlIdentifierRegex.IsMatch(request.RootTable))
            return (null, "RootTable is required and must be a valid identifier.");
        if (
            string.IsNullOrWhiteSpace(request.RootMatchColumn)
            || !SqlIdentifierRegex.IsMatch(request.RootMatchColumn)
        )
            return (null, "RootMatchColumn is required and must be a valid identifier.");
        if (request.UnmatchedRootPolicy is not (UnmatchedRootPolicy.Reject or UnmatchedRootPolicy.Quarantine))
            return (
                null,
                $"UnmatchedRootPolicy must be one of: {UnmatchedRootPolicy.Reject}, {UnmatchedRootPolicy.Quarantine}."
            );
        if (request.RootNode is null)
            return (null, "RootNode is required.");
        if (
            request.AllowedWritableColumns is null
            || request.AllowedWritableColumns.Any(c => string.IsNullOrWhiteSpace(c) || !SqlIdentifierRegex.IsMatch(c))
        )
            return (null, "AllowedWritableColumns must contain only valid, non-empty column identifiers.");

        var deniedFields = await DynamicExportService.GetDeniedFieldsAsync(db);
        var deniedInAllowlist = request.AllowedWritableColumns.Where(deniedFields.Contains).ToList();
        if (deniedInAllowlist.Count > 0)
            return (
                null,
                "AllowedWritableColumns lists GDPR-denied field(s) that can never be writable: "
                    + string.Join(", ", deniedInAllowlist)
            );

        // Round-trips through ImportNodeJson so a hand-built request (omitting "children"/"onMissingChild")
        // gets the same missing-property backfill a persisted tree already gets, before anything below
        // dereferences .Children — same reasoning as ExportDefinitionEndpoints.ValidateRequestAsync.
        var root = ImportNodeJson.Deserialize(ImportNodeJson.Serialize(request.RootNode))!;

        if (root.Kind != ImportNodeKind.Root)
            return (null, $"RootNode.Kind must be \"{ImportNodeKind.Root}\" (got \"{root.Kind}\").");

        var matchField = ImportNodeWalker.FindMatchField(root, request.RootMatchColumn);
        if (matchField is null)
            return (
                null,
                $"RootNode has no enabled scalar-field child mapped to RootMatchColumn '{request.RootMatchColumn}' "
                    + "— the walker would have no way to read each inbound record's correlation key."
            );

        var targets = new List<(string Table, string Column, string Path)>();
        var topKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var child in root.Children.Where(c => c.Enabled))
        {
            if (!topKeys.Add(child.SourceKey))
                return (null, $"Duplicate source key '{child.SourceKey}' at the top level.");

            var error = ValidateNode(child, matchField, request.RootTable, path: child.SourceKey, depth: 1, targets);
            if (error is not null)
                return (null, error);
        }
        if (topKeys.Count == 0)
            return (null, "The import must have at least one enabled field or nested group.");

        var allowedColumns = new HashSet<string>(request.AllowedWritableColumns, StringComparer.OrdinalIgnoreCase);

        var connRaw = await db.GetSettingRawAsync(SettingsKeys.ErpConnection);
        if (connRaw is null)
            return (null, "ERP connection not configured; cannot validate AllowedWritableColumns against the schema.");
        var connCfg = JsonSerializer.Deserialize<ErpConnectionConfig>(connRaw)!;

        SourceTableDto[] schema;
        try
        {
            await using var conn = new NpgsqlConnection(DynamicExportService.BuildConnectionString(connCfg));
            await conn.OpenAsync(ct);
            schema = await ConnectionEndpoints.IntrospectSchemaAsync(conn, ct);
        }
        catch (Exception ex)
        {
            return (null, $"Could not introspect the ERP schema to validate AllowedWritableColumns: {ex.Message}");
        }

        var schemaByTable = schema.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var (table, column, path) in targets)
        {
            var targetError = ValidateTargetAgainstSchema(table, column, path, allowedColumns, schemaByTable);
            if (targetError is not null)
                return (null, targetError);
        }

        return (root, null);
    }

    // One (table, column) writable-target check — split out of ValidateRequestAsync purely to keep that
    // method's own cognitive complexity down; the four checks are the (a)/(b)/(c) rules Open Decision #9
    // spells out, evaluated in that same order so the first one that fails is the one reported.
    private static string? ValidateTargetAgainstSchema(
        string table,
        string column,
        string path,
        IReadOnlySet<string> allowedColumns,
        IReadOnlyDictionary<string, SourceTableDto> schemaByTable
    )
    {
        if (!allowedColumns.Contains(column))
            return $"Node '{path}': '{column}' is not present in AllowedWritableColumns.";

        if (!schemaByTable.TryGetValue(table, out var tableDto))
            return $"Node '{path}': table '{table}' was not found in the introspected ERP schema.";

        var columnDto = tableDto.Columns.FirstOrDefault(c =>
            string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase)
        );
        if (columnDto is null)
            return $"Node '{path}': column '{column}' does not exist on table '{table}'.";
        if (columnDto.PrimaryKey)
            return $"Node '{path}': '{table}.{column}' is the primary key and cannot be a writable target.";
        if (columnDto.IsIdentity || columnDto.IsGenerated)
            return $"Node '{path}': '{table}.{column}' is an identity/computed column managed by the database "
                + "and cannot be a writable target.";
        if (columnDto.ForeignKeyTable is not null)
            return $"Node '{path}': '{table}.{column}' is a foreign key (references "
                + $"{columnDto.ForeignKeyTable}.{columnDto.ForeignKeyColumn}) — untracked foreign keys "
                + "are not writable in v1.";

        return null;
    }

    // Recursive identifier-safety + shape validator over the ImportNode tree, collecting every enabled
    // scalar-field node's (owning table, TargetColumn) pair into `targets` for the schema-aware pass above —
    // the ImportNode counterpart of ExportDefinitionEndpoints.ValidateNode, generalized for the write-side
    // shape (owning table changes per-branch: a root-level scalar writes RootTable, one nested under an
    // object/array node writes that node's own RelatedTable).
    private static string? ValidateNode(
        ImportNode node,
        ImportNode matchField,
        string ownerTable,
        string path,
        int depth,
        List<(string Table, string Column, string Path)> targets
    )
    {
        if (depth > DynamicExportService.MaxNestedDepth)
            return $"Node '{path}' exceeds the maximum nesting depth of {DynamicExportService.MaxNestedDepth}.";

        if (string.IsNullOrWhiteSpace(node.SourceKey) || ContainsControlCharacters(node.SourceKey))
            return $"Node '{path}': SourceKey must be non-empty and free of control characters.";

        switch (node.Kind)
        {
            case ImportNodeKind.ScalarField:
                if (string.IsNullOrWhiteSpace(node.TargetColumn) || !SqlIdentifierRegex.IsMatch(node.TargetColumn))
                    return $"Node '{path}': TargetColumn is required and must be a valid identifier.";
                if (node.Children is { Length: > 0 })
                    return $"Node '{path}': a scalar field cannot have child nodes.";

                // The match field is read for correlation only and is never itself a write target — same
                // exclusion ImportNodeWalker.ValidateWritableColumns applies at run time.
                if (!ReferenceEquals(node, matchField))
                    targets.Add((ownerTable, node.TargetColumn, path));
                return null;

            case ImportNodeKind.Object:
            case ImportNodeKind.Array:
                if (string.IsNullOrWhiteSpace(node.RelatedTable) || !SqlIdentifierRegex.IsMatch(node.RelatedTable))
                    return $"Node '{path}': RelatedTable is required and must be a valid identifier.";
                if (string.IsNullOrWhiteSpace(node.JoinKey) || !SqlIdentifierRegex.IsMatch(node.JoinKey))
                    return $"Node '{path}': JoinKey is required and must be a valid identifier.";
                if (string.IsNullOrWhiteSpace(node.SourceJoinKey) || !SqlIdentifierRegex.IsMatch(node.SourceJoinKey))
                    return $"Node '{path}': SourceJoinKey is required and must be a valid identifier.";
                if (node.OnMissingChild == OnMissingChildPolicy.Insert)
                    return $"Node '{path}': OnMissingChild = \"insert\" is not permitted in v1 (Open Decision #15).";
                if (node.OnMissingChild != OnMissingChildPolicy.Reject)
                    return $"Node '{path}': OnMissingChild must be \"{OnMissingChildPolicy.Reject}\" "
                        + $"(got \"{node.OnMissingChild}\").";

                var enabledChildren = node.Children.Where(c => c.Enabled).ToList();
                if (enabledChildren.Count == 0)
                    return $"Node '{path}' must have at least one enabled field or nested group.";

                var siblingKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var child in enabledChildren)
                {
                    if (!siblingKeys.Add(child.SourceKey))
                        return $"Duplicate source key '{child.SourceKey}' within '{path}'.";

                    var error = ValidateNode(
                        child,
                        matchField,
                        node.RelatedTable,
                        $"{path}.{child.SourceKey}",
                        depth + 1,
                        targets
                    );
                    if (error is not null)
                        return error;
                }
                return null;

            default:
                return $"Node '{path}': Kind must be \"{ImportNodeKind.ScalarField}\", \"{ImportNodeKind.Object}\", "
                    + $"or \"{ImportNodeKind.Array}\" (got \"{node.Kind}\").";
        }
    }

    private static bool ContainsControlCharacters(string s) => s.Any(char.IsControl);
}
