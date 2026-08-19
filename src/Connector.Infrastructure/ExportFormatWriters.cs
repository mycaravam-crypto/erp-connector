using System.Text.Json.Nodes;
using Connector.Core.DynamicExport;

namespace Connector.Infrastructure;

/// <summary>
/// OCP seam for output formats (REQUIREMENTS-2.0.md §8): every implementation accepts the exact same
/// tree-shaped records <see cref="DynamicExportService.ExecuteExportNodeQueryAsync"/> produces for any
/// <see cref="ExportNode"/> tree (LSP — none may reject a shape another accepts), so adding a new format
/// is a new class here, never a change to the query engine, <c>ExportDefinitionEndpoints</c>, or the
/// scheduler worker.
/// </summary>
public interface IExportFormatWriter
{
    /// <summary>The <c>OutputFormat</c> discriminator this writer handles (e.g. <c>"csv"</c>).</summary>
    string Format { get; }

    /// <summary>File extension for the generated artifact, without a leading dot.</summary>
    string FileExtension { get; }

    byte[] Write(ExportNode root, IReadOnlyList<JsonObject> records, string schemaVersion, DateTimeOffset extractedAt);
}

/// <summary>JSON honors the tree's nesting natively — records serialize as-is, no flattening needed.</summary>
public sealed class JsonExportFormatWriter : IExportFormatWriter
{
    public string Format => "json";
    public string FileExtension => "json";

    public byte[] Write(
        ExportNode root,
        IReadOnlyList<JsonObject> records,
        string schemaVersion,
        DateTimeOffset extractedAt
    ) => DynamicExportService.BuildNestedJsonBytes(records, wrapper: null, schemaVersion, extractedAt);
}

/// <summary>CSV has no native nesting: every record is flattened to one column per leaf path first
/// (see <see cref="DynamicExportService.FlattenExportNodeRecord"/>).</summary>
public sealed class CsvExportFormatWriter : IExportFormatWriter
{
    public string Format => "csv";
    public string FileExtension => "csv";

    public byte[] Write(
        ExportNode root,
        IReadOnlyList<JsonObject> records,
        string schemaVersion,
        DateTimeOffset extractedAt
    )
    {
        var columns = DynamicExportService.GetExportNodeColumnNames(root);
        var flat = records.Select(r => DynamicExportService.FlattenExportNodeRecord(r, columns)).ToList();
        return DynamicExportService.BuildCsvBytes(flat, columns, schemaVersion, extractedAt);
    }
}

/// <summary>Excel, like CSV, has no native nesting and shares the same flattening.</summary>
public sealed class ExcelExportFormatWriter : IExportFormatWriter
{
    public string Format => "xlsx";
    public string FileExtension => "xlsx";

    public byte[] Write(
        ExportNode root,
        IReadOnlyList<JsonObject> records,
        string schemaVersion,
        DateTimeOffset extractedAt
    )
    {
        var columns = DynamicExportService.GetExportNodeColumnNames(root);
        var flat = records.Select(r => DynamicExportService.FlattenExportNodeRecord(r, columns)).ToList();
        return DynamicExportService.BuildExcelBytes(flat, columns, schemaVersion, extractedAt);
    }
}

/// <summary>
/// Resolves an <c>OutputFormat</c> string to its <see cref="IExportFormatWriter"/>. A plain lookup table
/// rather than DI registration: every other piece of this codebase's format dispatch (e.g. the legacy
/// <see cref="DynamicExportService.BuildExportAsync"/>'s format switch) is a static, stateless mapping too,
/// and these writers hold no state of their own.
/// </summary>
public static class ExportFormatWriterFactory
{
    private static readonly IReadOnlyDictionary<string, IExportFormatWriter> Writers = new Dictionary<
        string,
        IExportFormatWriter
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["csv"] = new CsvExportFormatWriter(),
        ["json"] = new JsonExportFormatWriter(),
        ["xlsx"] = new ExcelExportFormatWriter(),
    };

    /// <summary>Unknown formats fall back to xlsx, matching <see cref="DynamicExportService.BuildExportAsync"/>'s
    /// existing default-case behavior.</summary>
    public static IExportFormatWriter Get(string format) => Writers.GetValueOrDefault(format, Writers["xlsx"]);
}
