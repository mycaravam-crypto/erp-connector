using System.Text.Json.Nodes;
using Connector.Core.DynamicExport;
using Connector.Infrastructure;
using Npgsql;

namespace Connector.Integration.Tests;

/// <summary>
/// Pure-C# coverage for the Phase 14 <see cref="ExportNode"/> engine's non-SQL pieces: column-path
/// derivation, arbitrary-depth CSV/Excel flattening, and field-mapping transforms. All of this operates on
/// hand-built <see cref="JsonObject"/> trees (the shape <see cref="DynamicExportService.ExecuteExportNodeQueryAsync"/>
/// would have produced from Postgres), so unlike <see cref="ExportNodeQueryPostgresTests"/> it needs no
/// database — mirrors the split between <see cref="DynamicExportServiceTests"/> (pure) and
/// <see cref="DynamicExportServiceNestedJsonPostgresTests"/> (real-DB) for the legacy path.
/// </summary>
public sealed class ExportNodeEngineTests
{
    // ── GetExportNodeColumnNames ─────────────────────────────────────────────

    [Fact]
    public void GetExportNodeColumnNames_ScalarFieldsAtRoot_ReturnPlainNames()
    {
        var root = MakeRoot(ScalarField("id", "id"), ScalarField("name", "name"));

        var cols = DynamicExportService.GetExportNodeColumnNames(root);

        Assert.Equal(["id", "name"], cols);
    }

    [Fact]
    public void GetExportNodeColumnNames_NestedObjectAndArray_UseDotPaths()
    {
        var root = MakeRoot(
            ScalarField("id", "id"),
            Node(
                "manufacturer",
                ExportNodeKind.Object,
                relatedTable: "manufacturer",
                children: [ScalarField("name", "name")]
            ),
            Node(
                "addresses",
                ExportNodeKind.Array,
                relatedTable: "manufacturer_address",
                children: [ScalarField("city", "city")]
            )
        );

        var cols = DynamicExportService.GetExportNodeColumnNames(root);

        Assert.Equal(["id", "manufacturer.name", "addresses.city"], cols);
    }

    [Fact]
    public void GetExportNodeColumnNames_DisabledNode_Excluded()
    {
        var root = MakeRoot(ScalarField("id", "id"), ScalarField("hidden", "hidden", enabled: false));

        var cols = DynamicExportService.GetExportNodeColumnNames(root);

        Assert.Equal(["id"], cols);
    }

    [Fact]
    public void GetExportNodeColumnNames_ThreeLevelsDeep_FullPathPreserved()
    {
        var root = MakeRoot(
            Node(
                "manufacturer",
                ExportNodeKind.Object,
                relatedTable: "manufacturer",
                children:
                [
                    Node(
                        "addresses",
                        ExportNodeKind.Array,
                        relatedTable: "manufacturer_address",
                        children: [ScalarField("city", "city")]
                    ),
                ]
            )
        );

        var cols = DynamicExportService.GetExportNodeColumnNames(root);

        Assert.Equal(["manufacturer.addresses.city"], cols);
    }

    // ── FlattenExportNodeRecord ───────────────────────────────────────────────

    [Fact]
    public void FlattenExportNodeRecord_ObjectPath_SingleValue()
    {
        var row = new JsonObject
        {
            ["id"] = "1",
            ["manufacturer"] = new JsonObject { ["name"] = "Acme" },
        };

        var flat = DynamicExportService.FlattenExportNodeRecord(row, ["id", "manufacturer.name"]);

        Assert.Equal("1", flat["id"]);
        Assert.Equal("Acme", flat["manufacturer.name"]);
    }

    [Fact]
    public void FlattenExportNodeRecord_ArrayPath_JoinsValuesWithDelimiter()
    {
        var row = new JsonObject
        {
            ["addresses"] = new JsonArray(
                new JsonObject { ["city"] = "Austin" },
                new JsonObject { ["city"] = "Dallas" }
            ),
        };

        var flat = DynamicExportService.FlattenExportNodeRecord(row, ["addresses.city"]);

        Assert.Equal("Austin, Dallas", flat["addresses.city"]);
    }

    [Fact]
    public void FlattenExportNodeRecord_EmptyArray_YieldsEmptyStringNotNull()
    {
        var row = new JsonObject { ["addresses"] = new JsonArray() };

        var flat = DynamicExportService.FlattenExportNodeRecord(row, ["addresses.city"]);

        Assert.Equal("", flat["addresses.city"]);
    }

    [Fact]
    public void FlattenExportNodeRecord_NullObjectNode_YieldsEmptyString()
    {
        var row = new JsonObject { ["manufacturer"] = null };

        var flat = DynamicExportService.FlattenExportNodeRecord(row, ["manufacturer.name"]);

        Assert.Equal("", flat["manufacturer.name"]);
    }

    [Fact]
    public void FlattenExportNodeRecord_MissingPath_YieldsEmptyString()
    {
        var row = new JsonObject { ["id"] = "1" };

        var flat = DynamicExportService.FlattenExportNodeRecord(row, ["id", "not.present"]);

        Assert.Equal("", flat["not.present"]);
    }

    [Fact]
    public void FlattenExportNodeRecord_TwoLevelNestedArrayInsideObject_JoinsCorrectly()
    {
        var row = new JsonObject
        {
            ["manufacturer"] = new JsonObject
            {
                ["addresses"] = new JsonArray(
                    new JsonObject { ["city"] = "Austin" },
                    new JsonObject { ["city"] = "Dallas" }
                ),
            },
        };

        var flat = DynamicExportService.FlattenExportNodeRecord(row, ["manufacturer.addresses.city"]);

        Assert.Equal("Austin, Dallas", flat["manufacturer.addresses.city"]);
    }

    // ── ApplyExportNodeMappingsRecursive / transforms ─────────────────────────

    [Theory]
    [InlineData(FieldTransform.Uppercase, "hello", "HELLO")]
    [InlineData(FieldTransform.Lowercase, "HELLO", "hello")]
    [InlineData(FieldTransform.Trim, "  hi  ", "hi")]
    public void ApplyExportNodeMappingsRecursive_StringTransforms_ApplyToValue(
        string transform,
        string raw,
        string expected
    )
    {
        var root = MakeRoot(
            ScalarField("name", "name", mapping: new FieldMapping(null, transform, null, FieldDataType.String))
        );
        var row = new JsonObject { ["name"] = raw };

        DynamicExportService.ApplyExportNodeMappingsRecursive(row, root);

        Assert.Equal(expected, row["name"]!.GetValue<string>());
    }

    [Fact]
    public void ApplyExportNodeMappingsRecursive_DateFormatTransform_ReformatsIsoDate()
    {
        var root = MakeRoot(
            ScalarField(
                "warrantyStart",
                "warrantyStart",
                mapping: new FieldMapping(null, FieldTransform.DateFormat, "MM/dd/yyyy", FieldDataType.String)
            )
        );
        var row = new JsonObject { ["warrantyStart"] = "2026-08-19" };

        DynamicExportService.ApplyExportNodeMappingsRecursive(row, root);

        Assert.Equal("08/19/2026", row["warrantyStart"]!.GetValue<string>());
    }

    [Fact]
    public void ApplyExportNodeMappingsRecursive_ConstantTransform_ReplacesValueRegardlessOfSource()
    {
        var root = MakeRoot(
            ScalarField(
                "region",
                "region",
                mapping: new FieldMapping(null, FieldTransform.Constant, "EU", FieldDataType.String)
            )
        );
        var row = new JsonObject { ["region"] = "whatever" };

        DynamicExportService.ApplyExportNodeMappingsRecursive(row, root);

        Assert.Equal("EU", row["region"]!.GetValue<string>());
    }

    [Fact]
    public void ApplyExportNodeMappingsRecursive_NullValueWithDefault_UsesDefaultValue()
    {
        var root = MakeRoot(
            ScalarField(
                "email",
                "email",
                mapping: new FieldMapping("unknown@example.com", FieldTransform.None, null, FieldDataType.String)
            )
        );
        var row = new JsonObject { ["email"] = null };

        DynamicExportService.ApplyExportNodeMappingsRecursive(row, root);

        Assert.Equal("unknown@example.com", row["email"]!.GetValue<string>());
    }

    [Fact]
    public void ApplyExportNodeMappingsRecursive_NumberDataType_CoercesToJsonNumber()
    {
        var root = MakeRoot(
            ScalarField("qty", "qty", mapping: new FieldMapping(null, FieldTransform.None, null, FieldDataType.Number))
        );
        var row = new JsonObject { ["qty"] = "42" };

        DynamicExportService.ApplyExportNodeMappingsRecursive(row, root);

        Assert.Equal(System.Text.Json.JsonValueKind.Number, row["qty"]!.GetValueKind());
        Assert.Equal(42, row["qty"]!.GetValue<decimal>());
    }

    [Fact]
    public void ApplyExportNodeMappingsRecursive_UnparseableNumber_FallsBackToStringInsteadOfThrowing()
    {
        var root = MakeRoot(
            ScalarField("qty", "qty", mapping: new FieldMapping(null, FieldTransform.None, null, FieldDataType.Number))
        );
        var row = new JsonObject { ["qty"] = "not-a-number" };

        DynamicExportService.ApplyExportNodeMappingsRecursive(row, root);

        Assert.Equal("not-a-number", row["qty"]!.GetValue<string>());
    }

    [Fact]
    public void ApplyExportNodeMappingsRecursive_BooleanDataType_CoercesToJsonBoolean()
    {
        var root = MakeRoot(
            ScalarField(
                "active",
                "active",
                mapping: new FieldMapping(null, FieldTransform.None, null, FieldDataType.Boolean)
            )
        );
        var row = new JsonObject { ["active"] = "true" };

        DynamicExportService.ApplyExportNodeMappingsRecursive(row, root);

        Assert.Equal(System.Text.Json.JsonValueKind.True, row["active"]!.GetValueKind());
    }

    [Fact]
    public void ApplyExportNodeMappingsRecursive_NoMapping_ValueUnchanged()
    {
        var root = MakeRoot(ScalarField("id", "id"));
        var row = new JsonObject { ["id"] = "unchanged" };

        DynamicExportService.ApplyExportNodeMappingsRecursive(row, root);

        Assert.Equal("unchanged", row["id"]!.GetValue<string>());
    }

    [Fact]
    public void ApplyExportNodeMappingsRecursive_TransformInsideNestedObject_AppliesAtDepth()
    {
        var root = MakeRoot(
            Node(
                "manufacturer",
                ExportNodeKind.Object,
                relatedTable: "manufacturer",
                children:
                [
                    ScalarField(
                        "name",
                        "name",
                        mapping: new FieldMapping(null, FieldTransform.Uppercase, null, FieldDataType.String)
                    ),
                ]
            )
        );
        var row = new JsonObject { ["manufacturer"] = new JsonObject { ["name"] = "acme" } };

        DynamicExportService.ApplyExportNodeMappingsRecursive(row, root);

        Assert.Equal("ACME", row["manufacturer"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void ApplyExportNodeMappingsRecursive_TransformInsideArrayElements_AppliesToEachItem()
    {
        var root = MakeRoot(
            Node(
                "addresses",
                ExportNodeKind.Array,
                relatedTable: "manufacturer_address",
                children:
                [
                    ScalarField(
                        "city",
                        "city",
                        mapping: new FieldMapping(null, FieldTransform.Uppercase, null, FieldDataType.String)
                    ),
                ]
            )
        );
        var row = new JsonObject
        {
            ["addresses"] = new JsonArray(
                new JsonObject { ["city"] = "austin" },
                new JsonObject { ["city"] = "dallas" }
            ),
        };

        DynamicExportService.ApplyExportNodeMappingsRecursive(row, root);

        var addresses = row["addresses"]!.AsArray();
        Assert.Equal("AUSTIN", addresses[0]!["city"]!.GetValue<string>());
        Assert.Equal("DALLAS", addresses[1]!["city"]!.GetValue<string>());
    }

    // ── Depth guard ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteExportNodeQueryAsync_ExceedsMaxNestedDepth_ThrowsBeforeAnyDbAccess()
    {
        // The depth check fires while building the SQL text, before the connection is ever touched — so
        // this can run against an unopened connection object rather than needing the live testdb fixture.
        var deep = ScalarField("leaf", "leaf");
        for (var i = 0; i < DynamicExportService.MaxNestedDepth + 2; i++)
            deep = Node($"level{i}", ExportNodeKind.Object, relatedTable: "t", children: [deep]);
        var root = MakeRoot(deep);

        using var conn = new NpgsqlConnection("Host=unused;Timeout=1");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DynamicExportService.ExecuteExportNodeQueryAsync(conn, "masterdata", root, CancellationToken.None)
        );
    }

    // ── ExportFormatWriterFactory ─────────────────────────────────────────────

    [Theory]
    [InlineData("csv", typeof(CsvExportFormatWriter))]
    [InlineData("json", typeof(JsonExportFormatWriter))]
    [InlineData("xlsx", typeof(ExcelExportFormatWriter))]
    [InlineData("CSV", typeof(CsvExportFormatWriter))] // case-insensitive
    [InlineData("unknown-format", typeof(JsonExportFormatWriter))] // defaults to json
    public void Get_ReturnsExpectedWriterType(string format, Type expected)
    {
        var writer = ExportFormatWriterFactory.Get(format);

        Assert.IsType(expected, writer);
    }

    [Fact]
    public void JsonExportFormatWriter_Write_PreservesNesting()
    {
        var root = MakeRoot(
            ScalarField("id", "id"),
            Node(
                "manufacturer",
                ExportNodeKind.Object,
                relatedTable: "manufacturer",
                children: [ScalarField("name", "name")]
            )
        );
        var records = new List<JsonObject>
        {
            new()
            {
                ["id"] = "1",
                ["manufacturer"] = new JsonObject { ["name"] = "Acme" },
            },
        };

        var bytes = new JsonExportFormatWriter().Write(root, records, "v1", DateTimeOffset.UtcNow);

        var doc = System.Text.Json.JsonDocument.Parse(bytes);
        var manufacturer = doc.RootElement.GetProperty("records")[0].GetProperty("manufacturer");
        Assert.Equal("Acme", manufacturer.GetProperty("name").GetString());
    }

    [Fact]
    public void CsvExportFormatWriter_Write_FlattensNestedArrayIntoJoinedColumn()
    {
        var root = MakeRoot(
            ScalarField("id", "id"),
            Node(
                "addresses",
                ExportNodeKind.Array,
                relatedTable: "manufacturer_address",
                children: [ScalarField("city", "city")]
            )
        );
        var records = new List<JsonObject>
        {
            new()
            {
                ["id"] = "1",
                ["addresses"] = new JsonArray(
                    new JsonObject { ["city"] = "Austin" },
                    new JsonObject { ["city"] = "Dallas" }
                ),
            },
        };

        var bytes = new CsvExportFormatWriter().Write(root, records, "v1", DateTimeOffset.UtcNow);
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.Contains("addresses.city", text);
        Assert.Contains("Austin, Dallas", text);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static ExportNode MakeRoot(params ExportNode[] children) =>
        new("root", ExportNodeKind.Root, null, null, null, null, null, null, children, true);

    private static ExportNode ScalarField(
        string targetKey,
        string sourceField,
        bool enabled = true,
        FieldMapping? mapping = null
    ) => new(targetKey, ExportNodeKind.ScalarField, sourceField, null, null, null, null, mapping, [], enabled);

    private static ExportNode Node(
        string targetKey,
        string kind,
        string relatedTable,
        ExportNode[] children,
        bool enabled = true
    ) => new(targetKey, kind, null, relatedTable, "id", "id", null, null, children, enabled);
}
