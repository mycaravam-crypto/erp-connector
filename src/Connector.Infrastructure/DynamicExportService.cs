using System.Text.Json;
using ClosedXML.Excel;
using Connector.Core.DynamicExport;
using Connector.Core.Schema;
using Npgsql;

namespace Connector.Infrastructure;

public static class DynamicExportService
{
    public static IReadOnlyList<string> GetColumnNames(ExportMappingConfig cfg) =>
        cfg.Fields.Where(f => f.Enabled).Select(f => f.TargetName)
            .Concat(cfg.Relations.Where(r => r.Enabled).Select(r => r.TargetField))
            .ToList();

    public static string BuildConnectionString(ErpConnectionConfig cfg) =>
        $"Host={cfg.Host};Port={cfg.Port};Database={cfg.Database};Username={cfg.Username};Password={cfg.Password};SSL Mode=Prefer;Trust Server Certificate=true;Timeout=5;Command Timeout=10";

    public static async Task<List<Dictionary<string, string>>> ExecuteQueryAsync(
        NpgsqlConnection conn, ExportMappingConfig cfg, CancellationToken ct, int? limit = null)
    {
        var parts = new List<string>();

        foreach (var f in cfg.Fields.Where(x => x.Enabled))
            parts.Add($"s.{QI(f.SourceName)} AS {QI(f.TargetName)}");

        foreach (var r in cfg.Relations.Where(x => x.Enabled))
        {
            var sf = r.StrategyOptions.SourceField;
            var delim = r.StrategyOptions.Delimiter.Replace("'", "''");
            var agg = r.FlattenStrategy == "string_join"
                ? $"string_agg({QI(r.RelatedTable)}.{QI(sf)}::text, '{delim}')"
                : $"array_to_string(array_agg({QI(r.RelatedTable)}.{QI(sf)}::text), ',')";
            parts.Add(
                $"(SELECT {agg} FROM {QI(r.RelatedTable)} " +
                $"WHERE {QI(r.RelatedTable)}.{QI(r.JoinKey)} = s.{QI(r.SourceJoinKey)}) AS {QI(r.TargetField)}"
            );
        }

        if (parts.Count == 0) return [];

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
                row[reader.GetName(i)] = await reader.IsDBNullAsync(i, ct) ? "" : (reader.GetValue(i)?.ToString() ?? "");
            results.Add(row);
        }

        return results;
    }

    public static byte[] BuildCsvBytes(
        IReadOnlyList<Dictionary<string, string>> records,
        IReadOnlyList<string> columns,
        string schemaVersion,
        DateTimeOffset extractedAt)
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
        DateTimeOffset extractedAt)
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
        DateTimeOffset extractedAt)
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
        for (int r = 0; r < records.Count; r++)
            for (int c = 0; c < columns.Count; c++)
                ws.Cell(r + 3, c + 1).Value = records[r].GetValueOrDefault(columns[c], "");
        for (int c = 1; c <= columns.Count; c++)
            ws.Column(c).Style.NumberFormat.NumberFormatId = 49; // "@" = Text
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // Safe SQL identifier quoting — wraps in double quotes and escapes embedded double quotes.
    public static string QI(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return '"' + value.Replace("\"", "\"\"") + '"';
        return value;
    }
}
