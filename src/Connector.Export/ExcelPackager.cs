using System.Security.Cryptography;
using ClosedXML.Excel;
using Connector.Core.Domain;
using Connector.Core.Interfaces;
using Connector.Core.Schema;

namespace Connector.Export;

/// <summary>
/// Erzeugt eine Excel-Datei (.xlsx) aus den gemappten Records und berechnet das Manifest.
/// Iteration 1-Format; Iteration 2 tauscht diese Klasse gegen einen JSON/CSV-Packager aus.
/// </summary>
public sealed class ExcelPackager : IPackager
{
    public Task<ExportPackage> PackageAsync(
        IReadOnlyList<MappedExportRecord> records,
        int sequenceNumber,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? exportNameOverrides = null
    )
    {
        ct.ThrowIfCancellationRequested();

        var extractedAt = DateTimeOffset.UtcNow;
        var dataFileName = ExportSchema.BuildFileName(sequenceNumber, extractedAt);

        var fileBytes = BuildExcelFile(records, extractedAt, exportNameOverrides);
        var checksum = ComputeSha256Hex(fileBytes);

        var manifest = new ExportManifest(
            SequenceNumber: sequenceNumber,
            SchemaVersion: ExportSchema.Version,
            ExtractedAt: extractedAt,
            RecordCount: records.Count,
            Sha256Checksum: checksum
        );

        return Task.FromResult(new ExportPackage(manifest, fileBytes, dataFileName));
    }

    private static byte[] BuildExcelFile(IReadOnlyList<MappedExportRecord> records, DateTimeOffset extractedAt, IReadOnlyDictionary<string, string>? nameOverrides)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Export");

        WriteMetadataRow(sheet, extractedAt);
        WriteHeaderRow(sheet, nameOverrides);
        WriteDataRows(sheet, records);

        // Alle Identifikatoren-Spalten als Text erzwingen — verhindert Excel-Autokonvertierung
        // von langen Nummern in Scientific Notation und Verlust führender Nullen.
        ForceTextFormat(sheet, ExportSchema.Columns.Count);

        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteMetadataRow(IXLWorksheet sheet, DateTimeOffset extractedAt)
    {
        // Zeile 1: Metadaten für den Hersteller-Transform-Map, damit er Schema-Versionen erkennen kann.
        sheet.Cell(1, 1).Value = $"schema_version={ExportSchema.Version}";
        sheet.Cell(1, 2).Value = $"extracted_at={extractedAt:O}";
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
    }

    private static void WriteHeaderRow(IXLWorksheet sheet, IReadOnlyDictionary<string, string>? nameOverrides)
    {
        for (int col = 0; col < ExportSchema.Columns.Count; col++)
        {
            var canonical = ExportSchema.Columns[col];
            sheet.Cell(2, col + 1).Value = nameOverrides?.GetValueOrDefault(canonical) ?? canonical;
        }

        sheet.Row(2).Style.Font.Bold = true;
    }

    private static void WriteDataRows(IXLWorksheet sheet, IReadOnlyList<MappedExportRecord> records)
    {
        for (int row = 0; row < records.Count; row++)
        {
            var r = records[row];
            var excelRow = row + 3; // Zeile 1 = Metadaten, Zeile 2 = Header
            sheet.Cell(excelRow, 1).Value = r.Guid;
            sheet.Cell(excelRow, 2).Value = r.SerialNumber;
            sheet.Cell(excelRow, 3).Value = r.PartNumber;
            sheet.Cell(excelRow, 4).Value = r.ParentSerialNumber ?? string.Empty;
            sheet.Cell(excelRow, 5).Value = r.ModelReference;
            sheet.Cell(excelRow, 6).Value = r.CommissioningDateIso8601;
            sheet.Cell(excelRow, 7).Value = r.MaintenanceState;
        }
    }

    private static void ForceTextFormat(IXLWorksheet sheet, int columnCount)
    {
        // Nur Identifikatoren-Spalten als Text: serial, part, parent, model.
        // Datum-Spalte bleibt als Text (ISO-String), State ebenfalls — alles sicher.
        for (int col = 1; col <= columnCount; col++)
            sheet.Column(col).Style.NumberFormat.NumberFormatId = 49; // "@" = Text
    }

    private static string ComputeSha256Hex(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
