using System.Text.Json;
using ClosedXML.Excel;
using Connector.Core.DynamicExport;
using Connector.Core.Schema;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Connector.Infrastructure;

public static class DynamicExportService
{
    /// <summary>
    /// ERP field names that must never appear in any export artifact (GDPR Art. 5(1)(c)).
    /// Checked at mapping-save time (API) and stripped at query time as defence-in-depth.
    /// This is the hardcoded fallback; admins can override via the gdpr_denied_fields AppSetting.
    /// </summary>
    public static readonly IReadOnlySet<string> GdprDeniedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "technician_name",
        "technician_id",
        "employee_id",
        "contact_name",
        "contact_email",
        "contact_phone",
        "operator_name",
    };

    /// <summary>
    /// Returns the active GDPR denylist: the DB-stored list if present, else <see cref="GdprDeniedFields"/>.
    /// </summary>
    public static async Task<IReadOnlySet<string>> GetDeniedFieldsAsync(ExportLogDbContext db)
    {
        var setting = await db.AppSettings.FindAsync("gdpr_denied_fields");
        if (setting is not null && !string.IsNullOrWhiteSpace(setting.Value))
        {
            var parsed = JsonSerializer.Deserialize<string[]>(setting.Value);
            if (parsed is { Length: > 0 })
                return new HashSet<string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        return GdprDeniedFields;
    }

    public static IReadOnlyList<string> GetColumnNames(ExportMappingConfig cfg) =>
        cfg
            .Fields.Where(f => f.Enabled)
            .Select(f => f.TargetName)
            .Concat(cfg.Relations.Where(r => r.Enabled).Select(r => r.TargetField))
            .ToList();

    public static string BuildConnectionString(ErpConnectionConfig cfg) =>
        $"Host={cfg.Host};Port={cfg.Port};Database={cfg.Database};Username={cfg.Username};Password={cfg.Password};SSL Mode=Prefer;Trust Server Certificate=true;Timeout=5;Command Timeout=10";

    public static async Task<List<Dictionary<string, string>>> ExecuteQueryAsync(
        NpgsqlConnection conn,
        ExportMappingConfig cfg,
        CancellationToken ct,
        int? limit = null,
        IReadOnlySet<string>? gdprDenylist = null
    )
    {
        var parts = new List<string>();

        foreach (var f in cfg.Fields.Where(x => x.Enabled))
            parts.Add($"s.{QI(f.SourceName)} AS {QI(f.TargetName)}");

        foreach (var r in cfg.Relations.Where(x => x.Enabled))
        {
            var sf = r.StrategyOptions.SourceField;
            var delim = r.StrategyOptions.Delimiter.Replace("'", "''");
            var agg =
                r.FlattenStrategy == "string_join"
                    ? $"string_agg({QI(r.RelatedTable)}.{QI(sf)}::text, '{delim}')"
                    : $"array_to_string(array_agg({QI(r.RelatedTable)}.{QI(sf)}::text), ',')";
            parts.Add(
                $"(SELECT {agg} FROM {QI(r.RelatedTable)} "
                    + $"WHERE {QI(r.RelatedTable)}.{QI(r.JoinKey)} = s.{QI(r.SourceJoinKey)}) AS {QI(r.TargetField)}"
            );
        }

        if (parts.Count == 0)
            return [];

        var sql = $"SELECT {string.Join(", ", parts)} FROM {QI(cfg.SourceTable)} s";
        if (limit.HasValue)
            sql += $" LIMIT {limit.Value}";

        var results = new List<Dictionary<string, string>>();
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 30 };
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            var row = new Dictionary<string, string>(reader.FieldCount);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (await reader.IsDBNullAsync(i, ct))
                {
                    row[reader.GetName(i)] = "";
                    continue;
                }
                // Coerce date/timestamp columns to ISO 8601 (YYYY-MM-DD) regardless of locale.
                var pgType = reader.GetDataTypeName(i);
                if (pgType is "date" or "timestamp" or "timestamptz")
                    row[reader.GetName(i)] = reader.GetDateTime(i).ToString("yyyy-MM-dd");
                else
                    row[reader.GetName(i)] = reader.GetValue(i)?.ToString() ?? "";
            }
            results.Add(row);
        }

        // Strip any GDPR-denied fields that somehow appeared in the result (defence-in-depth).
        var effectiveDenylist = gdprDenylist ?? GdprDeniedFields;
        foreach (var row in results)
        {
            foreach (var denied in effectiveDenylist)
                row.Remove(denied);
        }

        return results;
    }

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

    // Safe SQL identifier quoting — wraps in double quotes and escapes embedded double quotes.
    public static string QI(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return '"' + value.Replace("\"", "\"\"") + '"';
        return value;
    }
}
