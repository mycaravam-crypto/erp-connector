namespace Connector.Core.Domain;

/// <summary>
/// Fertig gemappter Exportdatensatz — alle Felder sind export-bereit formatiert.
/// </summary>
/// <remarks>
/// Alle Datumsfelder als ISO-8601-String, alle Identifikatoren als String (niemals numeric).
/// Excel konvertiert numerisch aussehende Strings sonst lautlos in Zahlen und korrumpiert
/// führende Nullen und lange Seriennummern — der Korrelationsschlüssel würde kaputt gehen.
/// </remarks>
public sealed record MappedExportRecord(
    /// <summary>Interne PostgreSQL-UUID als Text. Coalesce-Feld auf ServiceNow-Seite.</summary>
    string Guid,
    /// <summary>Hersteller-Seriennummer als Text. Identifikationsattribut — kein Coalesce-Schlüssel.</summary>
    string SerialNumber,
    /// <summary>Artikel-/Teilenummer des Modells.</summary>
    string PartNumber,
    /// <summary>Null bei Wurzelelementen der BOM-Hierarchie.</summary>
    string? ParentSerialNumber,
    /// <summary>Referenz auf den Modellartikel (Masterdaten).</summary>
    string ModelReference,
    /// <summary>ISO-8601-Datum (yyyy-MM-dd) oder leerer String wenn nicht erfasst.</summary>
    string CommissioningDateIso8601,
    /// <summary>Wartungsrelevanter Zustand aus dem ERP, z.B. "Active", "InRepair".</summary>
    string MaintenanceState
);
