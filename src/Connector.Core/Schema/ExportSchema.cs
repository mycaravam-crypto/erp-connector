namespace Connector.Core.Schema;

/// <summary>
/// Einzige Quelle für die Export-Schemadefinition (ICD-Kontrakt mit dem Hersteller).
/// </summary>
/// <remarks>
/// Alle Spaltenbezeichnungen, die in der Excel-Datei erscheinen, sowie alle Typregeln
/// sind hier zentralisiert. Änderungen an Feldern ohne Anpassung von <see cref="Version"/>
/// werden durch einen Unit-Test verhindert.
///
/// Breaking Change = MAJOR erhöhen + mit Hersteller koordinieren.
/// Additive Ergänzung = MINOR erhöhen.
/// </remarks>
public static class ExportSchema
{
    /// <summary>Aktuelle Schema-Version. Reist im Manifest mit jeder Export-Datei mit.</summary>
    public const string Version = "1.0";

    /// <summary>Spaltenköpfe in der Reihenfolge, wie sie in der Excel-Datei erscheinen.</summary>
    public static readonly IReadOnlyList<string> Columns =
    [
        ColumnNames.SerialNumber,
        ColumnNames.PartNumber,
        ColumnNames.ParentSerialNumber,
        ColumnNames.ModelReference,
        ColumnNames.CommissioningDate,
        ColumnNames.MaintenanceState,
    ];

    /// <summary>Spaltennamen als typsichere Konstanten — verhindert Tippfehler bei der Zuordnung.</summary>
    public static class ColumnNames
    {
        public const string SerialNumber = "serial_number";
        public const string PartNumber = "part_number";
        public const string ParentSerialNumber = "parent_serial_number";
        public const string ModelReference = "model_reference";
        public const string CommissioningDate = "commissioning_date";
        public const string MaintenanceState = "maintenance_state";
    }

    /// <summary>
    /// Dateiname-Template. Sequenznummer 4-stellig nullgepuffert, Datum UTC ISO-8601 kompakt.
    /// Beispiel: export_0042_20260628T060000Z.xlsx
    /// </summary>
    public static string BuildFileName(int sequenceNumber, DateTimeOffset extractedAt) =>
        $"export_{sequenceNumber:D4}_{extractedAt:yyyyMMdd'T'HHmmss'Z'}.xlsx";

    /// <summary>Manifest-Dateiname zum zugehörigen Daten-Dateinamen.</summary>
    public static string BuildManifestFileName(string dataFileName) =>
        dataFileName.Replace(".xlsx", ".manifest.json");
}
