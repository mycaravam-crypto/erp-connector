using System.Text.Json;
using System.Text.RegularExpressions;
using Connector.Core.DynamicExport;
using Connector.Core.DynamicImport;
using Connector.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Connector.Api.Endpoints;

// Request validation for ImportDefinitionEndpoints: the save-time guardrails described in this class's
// main file (Open Decisions #9 and #15), plus the IntegrationKey/ContractVersion uniqueness check shared
// with the /enable endpoint.
static partial class ImportDefinitionEndpoints
{
    // Same identifier-safety posture as ExportDefinitionEndpoints.SqlIdentifierRegex — deliberately
    // duplicated rather than shared, matching that file's own precedent (it duplicates
    // ExportMappingEndpoints.SqlIdentifierRegex for the same reason: a one-line regex isn't worth a shared
    // helper type across two independent validators).
    private static readonly Regex SqlIdentifierRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

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
        CancellationToken ct,
        int? excludeId = null
    )
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return (null, "Name is required.");
        if (string.IsNullOrWhiteSpace(request.RootTable) || !SqlIdentifierRegex.IsMatch(request.RootTable))
            return (null, "RootTable is required and must be a valid identifier.");
        if (string.IsNullOrWhiteSpace(request.RootMatchColumn) || !SqlIdentifierRegex.IsMatch(request.RootMatchColumn))
            return (null, "RootMatchColumn is required and must be a valid identifier.");
        if (request.UnmatchedRootPolicy is not (UnmatchedRootPolicy.Reject or UnmatchedRootPolicy.Quarantine))
            return (
                null,
                $"UnmatchedRootPolicy must be one of: {UnmatchedRootPolicy.Reject}, {UnmatchedRootPolicy.Quarantine}."
            );
        if (request.RootNode is null)
            return (null, "RootNode is required.");

        // knowledge/pipeline/import-mapping-presets.md §3.1: IntegrationKey/ContractVersion are set
        // together or not at all, and at most one *enabled* definition may ever claim a given pair.
        if ((request.IntegrationKey is null) != (request.ContractVersion is null))
            return (null, "IntegrationKey and ContractVersion must be set together, or not at all.");
        if (request.IntegrationKey is not null)
        {
            if (string.IsNullOrWhiteSpace(request.IntegrationKey) || ContainsControlCharacters(request.IntegrationKey))
                return (null, "IntegrationKey must be non-empty and free of control characters.");
            if (request.ContractVersion is < 1)
                return (null, "ContractVersion must be a positive integer.");
        }
        if (request.IsEnabled)
        {
            var pairError = await ValidateIntegrationKeyPairEnabledAsync(
                db,
                request.IntegrationKey,
                request.ContractVersion,
                excludeId,
                ct
            );
            if (pairError is not null)
                return (null, pairError);
        }
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
            return (
                null,
                $"Could not introspect the ERP schema to validate AllowedWritableColumns: {ErrorSanitizer.Detail(ex)}"
            );
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

    // Enforces knowledge/pipeline/import-mapping-presets.md §3.1's uniqueness rule: at most one *enabled*
    // ImportDefinition may ever claim a given (IntegrationKey, ContractVersion) pair, so Slice 3's
    // suggestion lookup is always an exact match, never a ranking. Shared between ValidateRequestAsync
    // (create/update) and the /enable endpoint, since either path can turn a definition enabled. Mirrors
    // ExportDefinitionEndpoints' own copy of this check, duplicated rather than shared per this file's own
    // precedent for SqlIdentifierRegex.
    internal static async Task<string?> ValidateIntegrationKeyPairEnabledAsync(
        ExportLogDbContext db,
        string? integrationKey,
        int? contractVersion,
        int? excludeId,
        CancellationToken ct
    )
    {
        if (integrationKey is null)
            return null;

        var conflict = await db.ImportDefinitions.AnyAsync(
            d =>
                (excludeId == null || d.Id != excludeId.Value)
                && d.IsEnabled
                && d.IntegrationKey == integrationKey
                && d.ContractVersion == contractVersion,
            ct
        );
        return conflict
            ? $"Another enabled import definition already uses IntegrationKey '{integrationKey}' v{contractVersion}."
            : null;
    }
}
