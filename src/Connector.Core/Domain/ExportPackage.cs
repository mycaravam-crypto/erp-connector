namespace Connector.Core.Domain;

/// <summary>
/// Vollständiges Exportpaket: Datendatei-Bytes + Manifest.
/// Beide Teile werden atomar auf den Staging-Pfad geschrieben — erst wenn beide
/// vollständig liegen, gilt der Export als bereit für die Vier-Augen-Freigabe.
/// </summary>
public sealed record ExportPackage(
    ExportManifest Manifest,
    /// <summary>Inhalt der Datendatei (Iteration 1: .xlsx).</summary>
    byte[] DataFileBytes,
    /// <summary>Dateiname ohne Pfad, z.B. "export_0042_20260628T060000Z.xlsx".</summary>
    string DataFileName
);
