using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Connector.Core.DynamicExport;

namespace Connector.Infrastructure;

// Output-format writers shared by both the legacy flat/nested-JSON path and (for CSV/Excel bytes) the
// ExportNode tree engine's flattened records — everything here is pure serialization, no querying.
public static partial class DynamicExportService
{
    public static byte[] BuildCsvBytes(
        IReadOnlyList<Dictionary<string, string>> records,
        IReadOnlyList<string> columns,
        string schemaVersion,
        DateTimeOffset extractedAt
    )
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# schema_version={schemaVersion},extracted_at={extractedAt:O}");
        sb.AppendLine(string.Join(",", columns.Select(CsvEscape)));
        foreach (var row in records)
            sb.AppendLine(string.Join(",", columns.Select(c => CsvEscape(row.GetValueOrDefault(c, "")))));
        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    public static byte[] BuildJsonBytes(
        IReadOnlyList<Dictionary<string, string>> records,
        string schemaVersion,
        DateTimeOffset extractedAt
    )
    {
        var obj = new
        {
            schema_version = schemaVersion,
            extracted_at = extractedAt.ToString("O"),
            records,
        };
        return JsonSerializer.SerializeToUtf8Bytes(obj, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// JSON-only sibling of <see cref="BuildJsonBytes"/> for nested records produced by
    /// <see cref="ExecuteNestedJsonQueryAsync"/>. <see cref="BuildJsonBytes"/> itself is left completely
    /// untouched, so backward compatibility is structural (an old config never reaches this method at
    /// all via <c>PipelineEndpoints</c>), not just "produces equivalent bytes." When <paramref name="wrapper"/>
    /// is null this still reproduces the exact legacy envelope shape as a defensive fallback.
    /// </summary>
    public static byte[] BuildNestedJsonBytes(
        IReadOnlyList<JsonObject> records,
        ExportJsonWrapperConfig? wrapper,
        string schemaVersion,
        DateTimeOffset extractedAt,
        ExportProvenance? provenance = null
    )
    {
        var itemsArray = new JsonArray(records.Select(r => (JsonNode?)r.DeepClone()).ToArray());

        if (wrapper is null)
        {
            var legacy = new JsonObject
            {
                ["schema_version"] = schemaVersion,
                ["extracted_at"] = extractedAt.ToString("O"),
            };
            // knowledge/pipeline/import-mapping-presets.md §3.2: additive, optional key, omitted entirely
            // when the exporting definition doesn't opt in — zero shape change for every export that
            // doesn't set IntegrationKey. `wrapper is null` is also the legacy single-mapping flow's
            // fallback shape (no ExportDefinitionEntity to source a key from), but that caller never
            // passes provenance, so this stays inert there.
            if (provenance is not null)
                legacy["provenance"] = new JsonObject
                {
                    ["integrationKey"] = provenance.IntegrationKey,
                    ["contractVersion"] = provenance.ContractVersion,
                    ["configVersion"] = provenance.ConfigVersion,
                };
            legacy["records"] = itemsArray;
            return JsonSerializer.SerializeToUtf8Bytes(legacy, new JsonSerializerOptions { WriteIndented = true });
        }

        var itemsKey = string.IsNullOrWhiteSpace(wrapper.ItemsKey) ? "records" : wrapper.ItemsKey;

        var metadata = new JsonObject();
        if (wrapper.MetadataFields is not { Length: > 0 })
        {
            metadata["schema_version"] = schemaVersion;
            metadata["extracted_at"] = extractedAt.ToString("O");
        }
        else
        {
            foreach (var m in wrapper.MetadataFields)
                metadata[m.Key] = m.IsDynamicTimestamp ? extractedAt.ToString("O") : m.Value;
        }

        var inner = new JsonObject();
        if (string.IsNullOrWhiteSpace(wrapper.MetadataKey))
        {
            foreach (var kv in metadata.ToList())
            {
                metadata.Remove(kv.Key);
                inner[kv.Key] = kv.Value;
            }
        }
        else
        {
            inner[wrapper.MetadataKey] = metadata;
        }
        inner[itemsKey] = itemsArray;

        JsonNode root = string.IsNullOrWhiteSpace(wrapper.RootKey)
            ? inner
            : new JsonObject { [wrapper.RootKey] = inner };

        return JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions { WriteIndented = true });
    }

    public static byte[] BuildExcelBytes(
        IReadOnlyList<Dictionary<string, string>> records,
        IReadOnlyList<string> columns,
        string schemaVersion,
        DateTimeOffset extractedAt
    )
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Export");
        ws.Cell(1, 1).Value = $"schema_version={schemaVersion}";
        ws.Cell(1, 2).Value = $"extracted_at={extractedAt:O}";
        ws.Row(1).Style.Font.Bold = true;
        ws.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
        for (int c = 0; c < columns.Count; c++)
            ws.Cell(2, c + 1).Value = columns[c];
        ws.Row(2).Style.Font.Bold = true;

        // Track columns where all non-empty values are ISO dates so we can apply date format.
        var dateColumns = new HashSet<int>();
        for (int r = 0; r < records.Count; r++)
        {
            for (int c = 0; c < columns.Count; c++)
            {
                var val = records[r].GetValueOrDefault(columns[c], "");
                var cell = ws.Cell(r + 3, c + 1);
                if (
                    !string.IsNullOrEmpty(val)
                    && DateOnly.TryParseExact(
                        val,
                        "yyyy-MM-dd",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None,
                        out var date
                    )
                )
                {
                    cell.Value = date.ToDateTime(TimeOnly.MinValue);
                    cell.Style.NumberFormat.Format = "yyyy-mm-dd";
                    dateColumns.Add(c + 1);
                }
                else
                {
                    cell.Value = val;
                    cell.Style.NumberFormat.NumberFormatId = 49; // "@" = Text
                }
            }
        }

        // Text format for all non-date columns (also protects header + metadata rows from auto-conversion).
        for (int c = 1; c <= columns.Count; c++)
            if (!dateColumns.Contains(c))
                ws.Column(c).Style.NumberFormat.NumberFormatId = 49;

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    /// <summary>MIME type for a <see cref="ExportBuildResult.Extension"/> value, for endpoints that hand
    /// the built bytes straight back in an HTTP response (as opposed to writing them to the staging
    /// folder). Shared by every "run and return the file" endpoint so they can't drift on content type.</summary>
    public static string ContentTypeFor(string extension) =>
        extension switch
        {
            "csv" => "text/csv",
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/json",
        };

    /// <summary>Filesystem-safe <c>{slug}_{timestamp}.{extension}</c> download file name for a
    /// free-text name (a preset name, an export definition name — never itself validated as a SQL
    /// identifier, unlike <see cref="QI"/>'s inputs) paired with a build's output extension.</summary>
    public static string BuildNamedFileName(string name, DateTimeOffset extractedAt, string extension)
    {
        var slug = new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        if (slug.Length == 0)
            slug = "export";
        return $"{slug}_{extractedAt:yyyyMMdd'T'HHmmss'Z'}.{extension}";
    }

    // Security-review finding SR-12 (CSV/spreadsheet formula injection, OWASP): a cell opened in Excel/
    // Sheets/LibreOffice is evaluated as a formula if its first character is one of these, regardless of
    // what produced the CSV. Prefixing with an apostrophe is those same applications' own "force text"
    // escape, so it neutralizes the formula without changing what a human sees in the cell.
    private static readonly char[] FormulaInjectionPrefixes = ['=', '+', '-', '@', '\t', '\r'];

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        if (FormulaInjectionPrefixes.Contains(value[0]))
            value = "'" + value;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return '"' + value.Replace("\"", "\"\"") + '"';
        return value;
    }
}
