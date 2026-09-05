using System.Text.Json;
using System.Text.Json.Nodes;
using Connector.Core.DynamicExport;
using Connector.Core.DynamicImport;
using Npgsql;

namespace Connector.Infrastructure;

/// <summary>
/// The write-side mirror of <see cref="DynamicExportService"/>'s tree walker (import-definitions.md §5):
/// parses an inbound JSON file, walks it alongside a saved <see cref="ImportNode"/> tree, and produces a
/// field-level diff per accepted row plus a rejected-row list — with zero write capability. Every method here
/// only ever issues SQL <c>SELECT</c>s; the eventual <c>UPDATE</c>/<c>INSERT</c> path is Slice 3's job, built
/// against this same walk result so preview and commit can never disagree about what a row means.
/// </summary>
public static class ImportNodeWalker
{
    /// <summary>
    /// Walks <paramref name="inboundJson"/> against <paramref name="root"/>, matching each record's
    /// correlation key against <paramref name="definition"/>'s <c>RootTable</c>/<c>RootMatchColumn</c> and
    /// computing a field-level diff for every match. Throws <see cref="ImportValidationException"/> for a
    /// problem with the saved definition or the inbound file itself (never for one bad record — see that
    /// type's doc comment); an individual record's correlation mismatch is reported via
    /// <see cref="ImportRowResult"/>, not an exception.
    /// </summary>
    public static async Task<ImportWalkResult> WalkAsync(
        NpgsqlConnection conn,
        ImportDefinitionEntity definition,
        ImportNode root,
        string inboundJson,
        CancellationToken ct
    )
    {
        var matchField =
            FindMatchField(root, definition.RootMatchColumn)
            ?? throw new ImportValidationException(
                $"ImportDefinition '{definition.Name}' has no scalar-field child mapped to RootMatchColumn "
                    + $"'{definition.RootMatchColumn}' — the walker cannot tell which inbound JSON property "
                    + "carries the correlation key."
            );

        var allowedColumns = ParseAllowedColumns(definition.AllowedWritableColumns);
        var violations = ValidateWritableColumns(root, matchField, allowedColumns);
        if (violations.Count > 0)
            throw new ImportValidationException(
                $"ImportDefinition '{definition.Name}' targets column(s) outside AllowedWritableColumns "
                    + $"(or on the GDPR denylist): {string.Join(", ", violations)}."
            );

        var records = ParseRecords(inboundJson);

        var rows = new List<ImportRowResult>(records.Count);
        int accepted = 0;
        int rejected = 0;

        foreach (var recordNode in records)
        {
            if (recordNode is not JsonObject record)
            {
                rows.Add(new ImportRowResult(null, ImportRowStatus.Rejected, "Record is not a JSON object.", [], []));
                rejected++;
                continue;
            }

            var correlationValue = ReadScalarValue(record, matchField);
            if (string.IsNullOrEmpty(correlationValue))
            {
                rows.Add(
                    new ImportRowResult(
                        null,
                        RejectStatusFor(definition.UnmatchedRootPolicy),
                        $"Record has no value for correlation field '{matchField.SourceKey}'.",
                        [],
                        []
                    )
                );
                rejected++;
                continue;
            }

            var dbRow = await FetchRootRowAsync(
                conn,
                definition.RootTable,
                definition.RootMatchColumn,
                correlationValue,
                root,
                ct
            );
            if (dbRow is null)
            {
                rows.Add(
                    new ImportRowResult(
                        correlationValue,
                        RejectStatusFor(definition.UnmatchedRootPolicy),
                        $"No {definition.RootTable} row found where {definition.RootMatchColumn} = '{correlationValue}'.",
                        [],
                        []
                    )
                );
                rejected++;
                continue;
            }

            var fields = DiffScalarFields(root, matchField, dbRow, record);

            var children = new List<ImportChildResult>();
            foreach (var childNode in root.Children.Where(c => c.Enabled && IsRelation(c.Kind)))
            {
                var childResult = await ResolveChildAsync(conn, childNode, dbRow, record, ct);
                if (childResult is not null)
                    children.Add(childResult);
            }

            rows.Add(new ImportRowResult(correlationValue, ImportRowStatus.Accepted, null, fields, children));
            accepted++;
        }

        return new ImportWalkResult(records.Count, accepted, rejected, rows);
    }

    private static bool IsRelation(string kind) => kind is ImportNodeKind.Object or ImportNodeKind.Array;

    // ── Definition-level validation (no DB access) ──────────────────────────────

    /// <summary>Finds the root's own scalar-field child mapped to <paramref name="rootMatchColumn"/> — the
    /// JSON property the walker reads to get each record's correlation value. Only ever looked up among the
    /// root's DIRECT children: the correlation key names a column on <c>RootTable</c> itself, never a nested
    /// related table.</summary>
    private static ImportNode? FindMatchField(ImportNode root, string rootMatchColumn) =>
        root.Children.FirstOrDefault(c => c.Kind == ImportNodeKind.ScalarField && c.TargetColumn == rootMatchColumn);

    /// <summary>
    /// Checks every scalar-field node in the tree (root and nested, excluding <paramref name="matchField"/>,
    /// which is read for matching only and is never written) against <paramref name="allowedColumns"/> and,
    /// defensively, the GDPR denylist (Open Decision #7) — a saved definition that somehow targets a column
    /// outside its own allowlist must never be trusted silently (import-definitions.md §3 step 4). Returns the
    /// offending column names rather than throwing directly, so the caller can build one aggregate error
    /// message instead of failing on only the first violation found.
    /// </summary>
    public static IReadOnlyList<string> ValidateWritableColumns(
        ImportNode root,
        ImportNode matchField,
        IReadOnlySet<string> allowedColumns
    )
    {
        var violations = new List<string>();

        void Walk(ImportNode node)
        {
            if (
                node.Kind == ImportNodeKind.ScalarField
                && node.Enabled
                && !ReferenceEquals(node, matchField)
                && (
                    node.TargetColumn is null
                    || !allowedColumns.Contains(node.TargetColumn)
                    || DynamicExportService.GdprDeniedFields.Contains(node.TargetColumn)
                )
            )
            {
                violations.Add(node.TargetColumn ?? node.SourceKey);
            }

            foreach (var child in node.Children)
                Walk(child);
        }

        foreach (var child in root.Children)
            Walk(child);

        return violations;
    }

    private static HashSet<string> ParseAllowedColumns(string allowedWritableColumnsJson)
    {
        var parsed = JsonSerializer.Deserialize<string[]>(
            string.IsNullOrWhiteSpace(allowedWritableColumnsJson) ? "[]" : allowedWritableColumnsJson
        );
        return new HashSet<string>(parsed ?? [], StringComparer.OrdinalIgnoreCase);
    }

    // ── Inbound JSON parsing ─────────────────────────────────────────────────────

    /// <summary>Accepts either a bare top-level JSON array of records, or an object with a top-level
    /// <c>"records"</c> array — the same envelope <see cref="DynamicExportService.BuildNestedJsonBytes"/> emits
    /// by default, so a vendor confirmation file can reuse the shape they already receive. Anything else is a
    /// definition-level problem (there's no per-record recovery from unparseable JSON), so it throws rather
    /// than producing an empty result.</summary>
    private static List<JsonNode?> ParseRecords(string inboundJson)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(inboundJson);
        }
        catch (JsonException ex)
        {
            throw new ImportValidationException($"Inbound file is not valid JSON: {ex.Message}", ex);
        }

        return parsed switch
        {
            JsonArray arr => [.. arr],
            JsonObject obj when obj.TryGetPropertyValue("records", out var recs) && recs is JsonArray recsArr => [.. recsArr],
            _ => throw new ImportValidationException(
                "Inbound JSON must be a top-level array of records, or an object with a top-level \"records\" array."
            ),
        };
    }

    // ── Root-row matching + field diff ───────────────────────────────────────────

    private static ImportRowStatus RejectStatusFor(string unmatchedRootPolicy) =>
        unmatchedRootPolicy == Connector.Core.DynamicImport.UnmatchedRootPolicy.Quarantine
            ? ImportRowStatus.Quarantined
            : ImportRowStatus.Rejected;

    private static async Task<Dictionary<string, string?>?> FetchRootRowAsync(
        NpgsqlConnection conn,
        string rootTable,
        string rootMatchColumn,
        string correlationValue,
        ImportNode root,
        CancellationToken ct
    )
    {
        var columns = CollectSelectColumns(root, rootMatchColumn);
        var sql =
            $"SELECT {string.Join(", ", columns.Select(DynamicExportService.QI))} FROM {DynamicExportService.QI(rootTable)} "
            + $"WHERE {DynamicExportService.QI(rootMatchColumn)}::text = @val LIMIT 1";

        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 10 };
        cmd.Parameters.AddWithValue("val", correlationValue);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return await ReadRowAsync(reader, columns, ct);
    }

    /// <summary>Column list for a root-row (or child-row) fetch: the match/join column itself, every direct
    /// scalar-field <c>TargetColumn</c> (so the diff has an "old value" to compare against), and every direct
    /// relation child's <c>SourceJoinKey</c> (so a grandchild's own fetch has the parent value it needs) —
    /// de-duplicated since the same column can legitimately serve more than one of those roles.</summary>
    private static List<string> CollectSelectColumns(ImportNode node, string ownMatchColumn)
    {
        var columns = new List<string> { ownMatchColumn };
        columns.AddRange(
            node.Children.Where(c => c.Enabled && c.Kind == ImportNodeKind.ScalarField).Select(c => c.TargetColumn!)
        );
        columns.AddRange(node.Children.Where(c => c.Enabled && IsRelation(c.Kind)).Select(c => c.SourceJoinKey!));
        return columns.Distinct(StringComparer.Ordinal).ToList();
    }

    private static async Task<Dictionary<string, string?>> ReadRowAsync(
        NpgsqlDataReader reader,
        IReadOnlyList<string> columns,
        CancellationToken ct
    )
    {
        var row = new Dictionary<string, string?>(columns.Count);
        for (int i = 0; i < columns.Count; i++)
            row[columns[i]] = await ReadColumnAsStringAsync(reader, i, ct);
        return row;
    }

    // Same DBNull/date-coercion contract as DynamicExportService.ExecuteQueryAsync's row loop, so an old value
    // read here compares fairly against the ISO-8601 form FieldMapping/CoerceToDataType would produce for a
    // new value of the same column.
    private static async Task<string?> ReadColumnAsStringAsync(NpgsqlDataReader reader, int i, CancellationToken ct)
    {
        if (await reader.IsDBNullAsync(i, ct))
            return null;

        var pgType = reader.GetDataTypeName(i);
        if (pgType is "date" or "timestamp" or "timestamptz")
            return reader.GetDateTime(i).ToString("yyyy-MM-dd");

        return reader.GetValue(i)?.ToString();
    }

    private static List<ImportFieldDiff> DiffScalarFields(
        ImportNode node,
        ImportNode? matchField,
        Dictionary<string, string?> dbRow,
        JsonObject record
    )
    {
        var fields = new List<ImportFieldDiff>();
        foreach (
            var child in node.Children.Where(c =>
                c.Enabled && c.Kind == ImportNodeKind.ScalarField && !ReferenceEquals(c, matchField)
            )
        )
        {
            var newValue = ReadScalarValue(record, child);
            var oldValue = dbRow.GetValueOrDefault(child.TargetColumn!);
            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
                fields.Add(new ImportFieldDiff(child.TargetColumn!, oldValue, newValue));
        }
        return fields;
    }

    /// <summary>Reads <paramref name="scalarNode"/>'s raw inbound value and applies its <see cref="FieldMapping"/>
    /// (transform/default/data-type coercion), reusing <see cref="DynamicExportService.ApplyFieldMapping"/>
    /// verbatim (import-definitions.md §5) rather than re-implementing transform semantics for the write
    /// direction.</summary>
    private static string? ReadScalarValue(JsonObject record, ImportNode scalarNode)
    {
        record.TryGetPropertyValue(scalarNode.SourceKey, out var raw);
        var mapped = DynamicExportService.ApplyFieldMapping(raw, scalarNode.Mapping);
        return JsonValueToString(mapped);
    }

    private static string? JsonValueToString(JsonNode? value) =>
        value switch
        {
            null => null,
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            JsonValue v => v.ToJsonString(),
            _ => value.ToJsonString(),
        };

    // ── Object/array child resolution ────────────────────────────────────────────

    /// <summary>
    /// Resolves one <see cref="ImportNodeKind.Object"/>/<see cref="ImportNodeKind.Array"/> child against
    /// <paramref name="parentRow"/>'s already-fetched <c>SourceJoinKey</c> value. Returns <c>null</c> when the
    /// inbound record simply doesn't carry this child this time (a legitimately partial vendor payload, not an
    /// error); otherwise resolves whether the <c>JoinKey</c> matches an existing related row per
    /// import-definitions.md §3 step 4, applying <see cref="ImportNode.OnMissingChild"/> when it doesn't.
    /// </summary>
    private static async Task<ImportChildResult?> ResolveChildAsync(
        NpgsqlConnection conn,
        ImportNode node,
        Dictionary<string, string?> parentRow,
        JsonObject parentRecord,
        CancellationToken ct
    )
    {
        if (!parentRecord.TryGetPropertyValue(node.SourceKey, out var childJson) || childJson is null)
            return null;

        if (node.Kind == ImportNodeKind.Object && childJson is not JsonObject)
            return new ImportChildResult(
                node.SourceKey,
                node.RelatedTable!,
                false,
                $"Expected a JSON object for '{node.SourceKey}'.",
                [],
                []
            );
        if (node.Kind == ImportNodeKind.Array && childJson is not JsonArray)
            return new ImportChildResult(
                node.SourceKey,
                node.RelatedTable!,
                false,
                $"Expected a JSON array for '{node.SourceKey}'.",
                [],
                []
            );

        if (!parentRow.TryGetValue(node.SourceJoinKey!, out var parentJoinValue))
            throw new ImportValidationException(
                $"ImportNode '{node.SourceKey}': SourceJoinKey '{node.SourceJoinKey}' was not selected from the "
                    + "parent row — this indicates a bug in the walker's own column collection, not a data problem."
            );
        if (parentJoinValue is null)
            return new ImportChildResult(
                node.SourceKey,
                node.RelatedTable!,
                false,
                $"Parent row has no value for SourceJoinKey '{node.SourceJoinKey}'.",
                [],
                []
            );

        var columns = CollectSelectColumns(node, node.JoinKey!);
        var sql =
            $"SELECT {string.Join(", ", columns.Select(DynamicExportService.QI))} FROM {DynamicExportService.QI(node.RelatedTable!)} "
            + $"WHERE {DynamicExportService.QI(node.JoinKey!)}::text = @val";

        var matches = new List<Dictionary<string, string?>>();
        await using (var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 10 })
        {
            cmd.Parameters.AddWithValue("val", parentJoinValue);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                matches.Add(await ReadRowAsync(reader, columns, ct));
        }

        return node.Kind == ImportNodeKind.Object
            ? await ResolveObjectChildAsync(conn, node, (JsonObject)childJson, matches, parentJoinValue, ct)
            : ResolveArrayChild(node, matches, parentJoinValue);
    }

    private static async Task<ImportChildResult> ResolveObjectChildAsync(
        NpgsqlConnection conn,
        ImportNode node,
        JsonObject childRecord,
        List<Dictionary<string, string?>> matches,
        string parentJoinValue,
        CancellationToken ct
    )
    {
        if (matches.Count > 1)
            throw new ImportValidationException(
                $"ImportNode '{node.SourceKey}': more than one {node.RelatedTable} row matched {node.JoinKey} = "
                    + $"'{parentJoinValue}'. An \"object\" node assumes at most one match — use \"array\" instead "
                    + "if this relation is genuinely 1:N."
            );

        if (matches.Count == 0)
        {
            if (node.OnMissingChild != OnMissingChildPolicy.Insert)
                return new ImportChildResult(
                    node.SourceKey,
                    node.RelatedTable!,
                    false,
                    $"No {node.RelatedTable} row found where {node.JoinKey} = '{parentJoinValue}'.",
                    [],
                    []
                );

            var insertFields = node
                .Children.Where(c => c.Enabled && c.Kind == ImportNodeKind.ScalarField)
                .Select(c => new ImportFieldDiff(c.TargetColumn!, null, ReadScalarValue(childRecord, c)))
                .ToList();
            return new ImportChildResult(node.SourceKey, node.RelatedTable!, true, null, insertFields, []);
        }

        var dbRow = matches[0];
        var fields = DiffScalarFields(node, matchField: null, dbRow, childRecord);

        var grandchildren = new List<ImportChildResult>();
        foreach (var grandchild in node.Children.Where(c => c.Enabled && IsRelation(c.Kind)))
        {
            var result = await ResolveChildAsync(conn, grandchild, dbRow, childRecord, ct);
            if (result is not null)
                grandchildren.Add(result);
        }

        return new ImportChildResult(node.SourceKey, node.RelatedTable!, true, null, fields, grandchildren);
    }

    // Array-kind children only get the existence check in v1 — see ImportChildResult's doc comment for why
    // per-item field diffing is deferred rather than guessed at.
    private static ImportChildResult ResolveArrayChild(
        ImportNode node,
        List<Dictionary<string, string?>> matches,
        string parentJoinValue
    )
    {
        if (matches.Count > 0)
            return new ImportChildResult(node.SourceKey, node.RelatedTable!, true, null, [], []);

        return node.OnMissingChild == OnMissingChildPolicy.Insert
            ? new ImportChildResult(node.SourceKey, node.RelatedTable!, true, null, [], [])
            : new ImportChildResult(
                node.SourceKey,
                node.RelatedTable!,
                false,
                $"No {node.RelatedTable} row found where {node.JoinKey} = '{parentJoinValue}'.",
                [],
                []
            );
    }
}
