namespace Connector.Core.Domain;

/// <summary>
/// Rohdatensatz eines wartbaren Configuration Items, wie er aus dem ERP gelesen wird.
/// Felder sind noch ungeprüft und können personenbezogene oder ausgeschlossene Daten enthalten.
/// </summary>
/// <remarks>
/// Dieser Typ verlässt niemals die Pipeline — er wird nach der Minimierung verworfen.
/// Nur <see cref="ExportItem"/> darf persistiert oder weitergegeben werden.
/// </remarks>
public sealed record ErpConfigurationItem(
    /// <summary>Interne PostgreSQL-UUID aus systemconfiguration.id. Coalesce-Schlüssel auf ServiceNow-Seite.</summary>
    string? Guid,
    /// <summary>Hersteller-Seriennummer / Equipmentnummer. Identifikationsattribut — kein Coalesce-Schlüssel.</summary>
    string? SerialNumber,
    /// <summary>Artikel- / Teilenummer des Modells.</summary>
    string? PartNumber,
    /// <summary>Seriennummer des übergeordneten CI in der BOM-Hierarchie. Null bei Wurzelelementen.</summary>
    string? ParentSerialNumber,
    /// <summary>Referenz auf den Modellartikel (Masterdaten).</summary>
    string? ModelReference,
    /// <summary>Inbetriebnahme- oder Installationsdatum. Kann null sein, wenn noch nicht erfasst.</summary>
    DateOnly? CommissioningDate,
    /// <summary>Wartungsrelevanter Zustand aus dem ERP.</summary>
    string? MaintenanceState,
    // Felder, die durch IDataMinimizer ausgeschlossen werden:
    /// <summary>Techniker-Name — wird durch Minimierung entfernt (DSGVO Art. 5 Abs. 1 lit. c).</summary>
    string? TechnicianName,
    /// <summary>Lagerort — Aufnahme in Scope ist noch offen (Open Point #4).</summary>
    string? StorageLocation
);
