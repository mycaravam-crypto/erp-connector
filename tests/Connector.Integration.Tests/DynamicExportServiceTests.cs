using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClosedXML.Excel;
using Connector.Core.DynamicExport;
using Connector.Core.Schema;
using Connector.Infrastructure;

namespace Connector.Integration.Tests;

/// <summary>
/// Tests for DynamicExportService — column selection, renaming, and serialisation.
/// All tests use in-memory record data; no Postgres connection required.
/// </summary>
public sealed class DynamicExportServiceTests
{
    // ── GetColumnNames ────────────────────────────────────────────────────────

    [Fact]
    public void GetColumnNames_ReturnsOnlyEnabledFields_WithTargetNames()
    {
        var cfg = MakeConfig(
            fields:
            [
                new("src_id", "ci_id", Enabled: true),
                new("src_serial", "serial", Enabled: true),
                new("src_hidden", "hidden", Enabled: false),
            ],
            relations: []
        );

        var cols = DynamicExportService.GetColumnNames(cfg);

        Assert.Equal(["ci_id", "serial"], cols);
    }

    [Fact]
    public void GetColumnNames_ExcludesDisabledRelations()
    {
        var cfg = MakeConfig(
            fields: [new("id", "ci_id", Enabled: true)],
            relations:
            [
                MakeRelation("tags", "entity_id", "id", Enabled: true, fields: [new("value", "tag_list", true)]),
                MakeRelation("notes", "entity_id", "id", Enabled: false, fields: [new("value", "note_list", true)]),
            ]
        );

        var cols = DynamicExportService.GetColumnNames(cfg);

        Assert.Equal(["ci_id", "tag_list"], cols);
    }

    [Fact]
    public void GetColumnNames_IncludesEveryEnabledFieldOnARelation()
    {
        var cfg = MakeConfig(
            fields: [new("id", "ci_id", Enabled: true)],
            relations:
            [
                MakeRelation(
                    "maintenance_plan",
                    "system_configuration_id",
                    "id",
                    Enabled: true,
                    fields:
                    [
                        new("status", "plan_status", true),
                        new("allocation_chart_ref", "plan_ref", true),
                        new("hidden_notes", "notes", false),
                    ]
                ),
            ]
        );

        var cols = DynamicExportService.GetColumnNames(cfg);

        Assert.Equal(["ci_id", "plan_status", "plan_ref"], cols);
    }

    [Fact]
    public void GetColumnNames_EmptyWhenAllDisabled()
    {
        var cfg = MakeConfig(fields: [new("id", "ci_id", Enabled: false)], relations: []);

        Assert.Empty(DynamicExportService.GetColumnNames(cfg));
    }

    // ── BuildCsvBytes ─────────────────────────────────────────────────────────

    [Fact]
    public void BuildCsvBytes_HeaderUsesTargetColumnNames()
    {
        var cols = new[] { "asset_id", "model_name", "warranty_start" };
        var records = new List<Dictionary<string, string>>
        {
            new()
            {
                ["asset_id"] = "A1",
                ["model_name"] = "Server X",
                ["warranty_start"] = "2024-01-01",
            },
        };

        var csv = ParseCsv(
            DynamicExportService.BuildCsvBytes(records, cols, ExportSchema.Version, DateTimeOffset.UtcNow)
        );

        Assert.Equal("asset_id", csv.Headers[0]);
        Assert.Equal("model_name", csv.Headers[1]);
        Assert.Equal("warranty_start", csv.Headers[2]);
    }

    [Fact]
    public void BuildCsvBytes_RenamedColumns_ValuesAlignWithTargetNames()
    {
        // Source column "sys_id" is exported as "ci_id"; data keyed by target name.
        var cols = new[] { "ci_id", "part_no" };
        var records = new List<Dictionary<string, string>>
        {
            new() { ["ci_id"] = "UUID-001", ["part_no"] = "PN-42" },
            new() { ["ci_id"] = "UUID-002", ["part_no"] = "PN-99" },
        };

        var csv = ParseCsv(
            DynamicExportService.BuildCsvBytes(records, cols, ExportSchema.Version, DateTimeOffset.UtcNow)
        );

        Assert.Equal(2, csv.Rows.Count);
        Assert.Equal("UUID-001", csv.Rows[0][0]);
        Assert.Equal("PN-42", csv.Rows[0][1]);
        Assert.Equal("UUID-002", csv.Rows[1][0]);
    }

    [Fact]
    public void BuildCsvBytes_MissingColumnValue_EmptyString()
    {
        var cols = new[] { "ci_id", "optional_field" };
        var records = new List<Dictionary<string, string>>
        {
            new() { ["ci_id"] = "X" }, // optional_field absent
        };

        var csv = ParseCsv(
            DynamicExportService.BuildCsvBytes(records, cols, ExportSchema.Version, DateTimeOffset.UtcNow)
        );

        Assert.Equal("X", csv.Rows[0][0]);
        Assert.Equal("", csv.Rows[0][1]);
    }

    [Fact]
    public void BuildCsvBytes_CommaInValue_IsQuoted()
    {
        var cols = new[] { "name" };
        var records = new List<Dictionary<string, string>> { new() { ["name"] = "Smith, John" } };

        var csv = ParseCsv(
            DynamicExportService.BuildCsvBytes(records, cols, ExportSchema.Version, DateTimeOffset.UtcNow)
        );

        Assert.Equal("Smith, John", csv.Rows[0][0]);
    }

    // ── BuildJsonBytes ────────────────────────────────────────────────────────

    [Fact]
    public void BuildJsonBytes_RecordKeysAreTargetNames()
    {
        var records = new List<Dictionary<string, string>>
        {
            new() { ["asset_id"] = "A1", ["serial_no"] = "SN-001" },
        };

        var doc = JsonDocument.Parse(
            DynamicExportService.BuildJsonBytes(records, ExportSchema.Version, DateTimeOffset.UtcNow)
        );

        var first = doc.RootElement.GetProperty("records")[0];
        Assert.Equal("A1", first.GetProperty("asset_id").GetString());
        Assert.Equal("SN-001", first.GetProperty("serial_no").GetString());
    }

    [Fact]
    public void BuildJsonBytes_ContainsSchemaVersion()
    {
        var doc = JsonDocument.Parse(
            DynamicExportService.BuildJsonBytes([], ExportSchema.Version, DateTimeOffset.UtcNow)
        );

        Assert.Equal(ExportSchema.Version, doc.RootElement.GetProperty("schema_version").GetString());
    }

    [Fact]
    public void BuildJsonBytes_MultipleRecordsWithRenamedColumns()
    {
        var records = new List<Dictionary<string, string>>
        {
            new() { ["ci_identifier"] = "CI-001", ["install_status"] = "active" },
            new() { ["ci_identifier"] = "CI-002", ["install_status"] = "retired" },
        };

        var doc = JsonDocument.Parse(
            DynamicExportService.BuildJsonBytes(records, ExportSchema.Version, DateTimeOffset.UtcNow)
        );

        var arr = doc.RootElement.GetProperty("records");
        Assert.Equal(2, arr.GetArrayLength());
        Assert.Equal("CI-002", arr[1].GetProperty("ci_identifier").GetString());
        Assert.Equal("retired", arr[1].GetProperty("install_status").GetString());
    }

    // ── BuildNestedJsonBytes ──────────────────────────────────────────────────

    [Fact]
    public void BuildNestedJsonBytes_NoWrapper_MatchesLegacyBuildJsonBytesShape()
    {
        var flatRecords = new List<Dictionary<string, string>> { new() { ["asset_id"] = "A1" } };
        var nestedRecords = new List<JsonObject> { new() { ["asset_id"] = "A1" } };
        var extractedAt = DateTimeOffset.UtcNow;

        var legacyDoc = JsonDocument.Parse(
            DynamicExportService.BuildJsonBytes(flatRecords, ExportSchema.Version, extractedAt)
        );
        var nestedDoc = JsonDocument.Parse(
            DynamicExportService.BuildNestedJsonBytes(nestedRecords, null, ExportSchema.Version, extractedAt)
        );

        Assert.Equal(
            legacyDoc.RootElement.GetProperty("schema_version").GetString(),
            nestedDoc.RootElement.GetProperty("schema_version").GetString()
        );
        Assert.Equal(
            legacyDoc.RootElement.GetProperty("extracted_at").GetString(),
            nestedDoc.RootElement.GetProperty("extracted_at").GetString()
        );
        Assert.Equal("A1", nestedDoc.RootElement.GetProperty("records")[0].GetProperty("asset_id").GetString());
    }

    [Fact]
    public void BuildNestedJsonBytes_CustomRootItemsMetadataKeys_ProducesConfiguredEnvelope()
    {
        var records = new List<JsonObject> { new() { ["itemId"] = "ITEM-001" } };
        var wrapper = new ExportJsonWrapperConfig(
            RootKey: "masterData",
            ItemsKey: "items",
            MetadataKey: "metadata",
            MetadataFields: [new ExportJsonMetadataField("version", "1.0", IsDynamicTimestamp: false)]
        );

        var doc = JsonDocument.Parse(
            DynamicExportService.BuildNestedJsonBytes(records, wrapper, ExportSchema.Version, DateTimeOffset.UtcNow)
        );

        var root = doc.RootElement.GetProperty("masterData");
        Assert.Equal("1.0", root.GetProperty("metadata").GetProperty("version").GetString());
        Assert.Equal("ITEM-001", root.GetProperty("items")[0].GetProperty("itemId").GetString());
    }

    [Fact]
    public void BuildNestedJsonBytes_DynamicTimestampMetadataField_UsesExtractedAt()
    {
        var extractedAt = new DateTimeOffset(2026, 8, 6, 14, 35, 0, TimeSpan.Zero);
        var wrapper = new ExportJsonWrapperConfig(
            RootKey: "",
            ItemsKey: "items",
            MetadataKey: "metadata",
            MetadataFields: [new ExportJsonMetadataField("lastUpdated", "ignored", IsDynamicTimestamp: true)]
        );

        var doc = JsonDocument.Parse(
            DynamicExportService.BuildNestedJsonBytes([], wrapper, ExportSchema.Version, extractedAt)
        );

        Assert.Equal(
            extractedAt.ToString("O"),
            doc.RootElement.GetProperty("metadata").GetProperty("lastUpdated").GetString()
        );
    }

    [Fact]
    public void BuildNestedJsonBytes_BlankMetadataKey_FlattensMetadataAsSiblingsOfItems()
    {
        var wrapper = new ExportJsonWrapperConfig(
            RootKey: "",
            ItemsKey: "items",
            MetadataKey: "",
            MetadataFields: [new ExportJsonMetadataField("version", "2.0", IsDynamicTimestamp: false)]
        );

        var doc = JsonDocument.Parse(
            DynamicExportService.BuildNestedJsonBytes([], wrapper, ExportSchema.Version, DateTimeOffset.UtcNow)
        );

        Assert.Equal("2.0", doc.RootElement.GetProperty("version").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void BuildNestedJsonBytes_BlankRootKey_OmitsOuterWrapper()
    {
        var wrapper = new ExportJsonWrapperConfig("", "items", "metadata", []);

        var doc = JsonDocument.Parse(
            DynamicExportService.BuildNestedJsonBytes([], wrapper, ExportSchema.Version, DateTimeOffset.UtcNow)
        );

        // No extra wrapper key: "items"/"metadata" sit directly at the document root.
        Assert.True(doc.RootElement.TryGetProperty("items", out _));
        Assert.True(doc.RootElement.TryGetProperty("metadata", out _));
    }

    [Fact]
    public void BuildNestedJsonBytes_NestedArrayInRecord_StaysJsonArray_NotDoubleStringified()
    {
        var record = new JsonObject
        {
            ["manufacturer"] = new JsonObject
            {
                ["name"] = "Acme",
                ["addresses"] = new JsonArray(new JsonObject { ["city"] = "Austin" }),
            },
        };

        var doc = JsonDocument.Parse(
            DynamicExportService.BuildNestedJsonBytes([record], null, ExportSchema.Version, DateTimeOffset.UtcNow)
        );

        var addresses = doc.RootElement.GetProperty("records")[0].GetProperty("manufacturer").GetProperty("addresses");
        Assert.Equal(JsonValueKind.Array, addresses.ValueKind);
        Assert.Equal("Austin", addresses[0].GetProperty("city").GetString());
    }

    [Fact]
    public void BuildNestedJsonBytes_EmptyRecords_ItemsArrayIsEmptyNotNull()
    {
        var doc = JsonDocument.Parse(
            DynamicExportService.BuildNestedJsonBytes([], null, ExportSchema.Version, DateTimeOffset.UtcNow)
        );

        Assert.Equal(JsonValueKind.Array, doc.RootElement.GetProperty("records").ValueKind);
        Assert.Equal(0, doc.RootElement.GetProperty("records").GetArrayLength());
    }

    // ── BuildExcelBytes ───────────────────────────────────────────────────────

    [Fact]
    public void BuildExcelBytes_HeaderRowUsesTargetNames()
    {
        var cols = new[] { "asset_id", "model_name" };
        var records = new List<Dictionary<string, string>>
        {
            new() { ["asset_id"] = "A1", ["model_name"] = "Server X" },
        };

        var ws = OpenExcelSheet(
            DynamicExportService.BuildExcelBytes(records, cols, ExportSchema.Version, DateTimeOffset.UtcNow)
        );

        // Row 1 = metadata, Row 2 = column headers
        Assert.Equal("asset_id", ws.Cell(2, 1).GetString());
        Assert.Equal("model_name", ws.Cell(2, 2).GetString());
    }

    [Fact]
    public void BuildExcelBytes_DataAppearsInRow3Onwards()
    {
        var cols = new[] { "ci_id", "part_no" };
        var records = new List<Dictionary<string, string>>
        {
            new() { ["ci_id"] = "UUID-001", ["part_no"] = "PN-42" },
            new() { ["ci_id"] = "UUID-002", ["part_no"] = "PN-99" },
        };

        var ws = OpenExcelSheet(
            DynamicExportService.BuildExcelBytes(records, cols, ExportSchema.Version, DateTimeOffset.UtcNow)
        );

        Assert.Equal("UUID-001", ws.Cell(3, 1).GetString());
        Assert.Equal("PN-42", ws.Cell(3, 2).GetString());
        Assert.Equal("UUID-002", ws.Cell(4, 1).GetString());
    }

    [Fact]
    public void BuildExcelBytes_RenamedColumns_HeaderMatchesTargetNotSource()
    {
        // Mapping: source "sys_serial" → target "serial_number"
        var cols = new[] { "serial_number" };
        var records = new List<Dictionary<string, string>> { new() { ["serial_number"] = "SN-RACK-001" } };

        var ws = OpenExcelSheet(
            DynamicExportService.BuildExcelBytes(records, cols, ExportSchema.Version, DateTimeOffset.UtcNow)
        );

        Assert.Equal("serial_number", ws.Cell(2, 1).GetString());
        Assert.Equal("SN-RACK-001", ws.Cell(3, 1).GetString());
    }

    // ── Full mapping round-trip ───────────────────────────────────────────────

    [Fact]
    public void FullMapping_EnabledSubsetWithRenames_CsvRoundTrip()
    {
        // Simulate: source has 4 columns, export mapping enables 2 with renamed targets.
        var cfg = MakeConfig(
            fields:
            [
                new("sys_id", "asset_id", Enabled: true),
                new("sys_serial", "serial_number", Enabled: true),
                new("sys_hidden_cost", "cost", Enabled: false),
                new("technician_name", "technician", Enabled: false),
            ],
            relations: []
        );

        var cols = DynamicExportService.GetColumnNames(cfg);

        // Simulate what ExecuteQueryAsync would return: keys are the SQL aliases (= TargetName).
        var records = new List<Dictionary<string, string>>
        {
            new() { ["asset_id"] = "UUID-A", ["serial_number"] = "SN-001" },
            new() { ["asset_id"] = "UUID-B", ["serial_number"] = "SN-002" },
        };

        var csv = ParseCsv(
            DynamicExportService.BuildCsvBytes(records, cols, ExportSchema.Version, DateTimeOffset.UtcNow)
        );

        // Only 2 enabled columns appear; source names and disabled columns are absent.
        Assert.Equal(2, csv.Headers.Count);
        Assert.Equal("asset_id", csv.Headers[0]);
        Assert.Equal("serial_number", csv.Headers[1]);
        Assert.DoesNotContain("cost", csv.Headers);
        Assert.DoesNotContain("technician", csv.Headers);
        Assert.DoesNotContain("sys_id", csv.Headers);
        Assert.DoesNotContain("sys_serial", csv.Headers);

        Assert.Equal(2, csv.Rows.Count);
        Assert.Equal("UUID-A", csv.Rows[0][0]);
        Assert.Equal("SN-001", csv.Rows[0][1]);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ExportMappingConfig MakeConfig(ExportMappingField[] fields, ExportMappingRelation[] relations) =>
        new("test_table", fields, relations);

    private static ExportMappingRelation MakeRelation(
        string table,
        string joinKey,
        string sourceJoinKey,
        bool Enabled,
        ExportMappingRelationField[] fields
    ) => new(table, joinKey, sourceJoinKey, Enabled, "string_join", ",", fields);

    private static IXLWorksheet OpenExcelSheet(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        var wb = new XLWorkbook(ms);
        return wb.Worksheet(1);
    }

    private record CsvResult(List<string> Headers, List<List<string>> Rows);

    private static CsvResult ParseCsv(byte[] bytes)
    {
        var lines = Encoding
            .UTF8.GetString(bytes)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.StartsWith('#'))
            .ToList();

        var headers = SplitCsvLine(lines[0]);
        var rows = lines.Skip(1).Select(SplitCsvLine).ToList();
        return new CsvResult(headers, rows);
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        foreach (var ch in line.Trim())
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (ch == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
                continue;
            }
            sb.Append(ch);
        }
        result.Add(sb.ToString());
        return result;
    }
}
