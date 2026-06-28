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

    public string DataFileName { get; set; } = string.Empty;
}

public static class ExportRunStatus
{
    public const string Pending = "Pending";
    public const string Released = "Released";
    public const string Failed = "Failed";
}
