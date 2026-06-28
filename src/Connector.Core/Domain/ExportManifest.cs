namespace Connector.Core.Domain;

/// <summary>
/// Begleitet jede Export-Datei. Ermöglicht dem Empfänger Integritätsprüfung und Lückenerkennung
/// auch ohne Rückkanal — die Sequenznummer deckt Gaps auf (z.B. Sprung von #41 auf #43).
/// </summary>
public sealed record ExportManifest(
    /// <summary>Monoton steigend, beginnend bei 1. Lücken signalisieren verlorene Exporte.</summary>
    int SequenceNumber,
    /// <summary>Schema-Version im Format MAJOR.MINOR. Breaking Changes erhöhen MAJOR.</summary>
    string SchemaVersion,
    /// <summary>Zeitpunkt des ERP-Laufs (UTC).</summary>
    DateTimeOffset ExtractedAt,
    /// <summary>Anzahl der Datensätze in der Datendatei — muss mit der tatsächlichen Zeilenzahl übereinstimmen.</summary>
    int RecordCount,
    /// <summary>SHA-256 über die Datendatei (Hex, lowercase). Wird vom Gateway vor USB-Freigabe geprüft.</summary>
    string Sha256Checksum);
