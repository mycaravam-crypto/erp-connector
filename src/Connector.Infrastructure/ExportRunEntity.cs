namespace Connector.Infrastructure;

/// <summary>
/// EF Core-Entität für die Export-Log-Tabelle.
/// Jede Zeile repräsentiert einen abgeschlossenen oder fehlgeschlagenen Export-Run.
/// </summary>
public sealed class ExportRunEntity
{
    public int Id { get; set; }

    /// <summary>Monotone Sequenznummer — eindeutig und nicht wiederverwendbar.</summary>
    public int SequenceNo { get; set; }

    /// <summary>UTC-Zeitpunkt des ERP-Laufs als ISO-8601-String (SQLite kennt kein DateTimeOffset nativ).</summary>
    public string ExtractedAt { get; set; } = string.Empty;

    /// <summary>Anzahl der exportierten CI-Datensätze. 0 bei Status Failed.</summary>
    public int RecordCount { get; set; }

    /// <summary>SHA-256 der Export-Datei, Hex lowercase. Leer bei Status Failed.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Pending | Released | Failed</summary>
    public string Status { get; set; } = ExportRunStatus.Pending;

    /// <summary>UTC-Zeitpunkt der Vier-Augen-Freigabe. Null wenn noch nicht freigegeben.</summary>
    public string? ReleasedAt { get; set; }

    /// <summary>Benutzername des Operators (Ersteller), der den Export ausgelöst hat.</summary>
    public string? OperatedBy { get; set; }

    /// <summary>Benutzername des Approvers (Freigabe). Muss verschieden von OperatedBy sein.</summary>
    public string? ApprovedBy { get; set; }

    /// <summary>Dateiname der Excel-Datei auf dem Staging-Pfad. Leer bei Status Failed.</summary>
    public string DataFileName { get; set; } = string.Empty;

    // ── Delivery fields (Phase 6.4) ───────────────────────────────────────────
    // Populated after the export package has been physically transferred to the vendor.
    // All nullable — delivery tracking is optional and post-release.

    /// <summary>UTC timestamp of physical handover to vendor. Null = not yet delivered.</summary>
    public string? DeliveredAt { get; set; }

    /// <summary>Username of the person who performed the physical delivery.</summary>
    public string? DeliveredBy { get; set; }

    /// <summary>Number of records the vendor confirmed were imported into ServiceNow. Null = no confirmation.</summary>
    public int? ImportedRecordCount { get; set; }

    /// <summary>Free-text notes from the delivery or import confirmation (medium, handover ref, etc.).</summary>
    public string? DeliveryNotes { get; set; }
}

public static class ExportRunStatus
{
    public const string Pending = "Pending";
    public const string Released = "Released";
    public const string Failed = "Failed";

    /// <summary>Operator explicitly bypassed this run to recover from a permanent failure or sequence gap.</summary>
    public const string Skipped = "Skipped";
}
