namespace Connector.Core.Schema;

/// <summary>
/// Single source of truth for the export schema definition (the ICD contract with the vendor).
/// </summary>
/// <remarks>
/// All column names that appear in the Excel file, and all type rules, are centralized here.
/// A unit test prevents field changes without a matching <see cref="Version"/> bump.
///
/// Breaking change = increment MAJOR + coordinate with the vendor.
/// Additive change = increment MINOR.
/// </remarks>
public static class ExportSchema
{
    /// <summary>Current schema version. Carried in the manifest of every export file.</summary>
    public const string Version = "2.0";

    /// <summary>Column headers in the order they appear in the Excel file.</summary>
    public static readonly IReadOnlyList<string> Columns =
    [
        ColumnNames.Guid,
        ColumnNames.SerialNumber,
        ColumnNames.PartNumber,
        ColumnNames.ParentSerialNumber,
        ColumnNames.ModelReference,
        ColumnNames.CommissioningDate,
        ColumnNames.MaintenanceState,
    ];

    /// <summary>Column names as type-safe constants — prevents typos when mapping.</summary>
    public static class ColumnNames
    {
        public const string Guid = "guid";
        public const string SerialNumber = "serial_number";
        public const string PartNumber = "part_number";
        public const string ParentSerialNumber = "parent_serial_number";
        public const string ModelReference = "model_reference";
        public const string CommissioningDate = "commissioning_date";
        public const string MaintenanceState = "maintenance_state";
    }

    /// <summary>
    /// File name template: sequence number zero-padded to 4 digits, date as compact UTC ISO-8601.
    /// Example: export_0042_20260628T060000Z.xlsx
    /// </summary>
    public static string BuildFileName(int sequenceNumber, DateTimeOffset extractedAt, string extension = "xlsx") =>
        $"export_{sequenceNumber:D4}_{extractedAt:yyyyMMdd'T'HHmmss'Z'}.{extension}";

    /// <summary>Manifest file name for the given data file name.</summary>
    public static string BuildManifestFileName(string dataFileName) =>
        Path.GetFileNameWithoutExtension(dataFileName) + ".manifest.json";
}
