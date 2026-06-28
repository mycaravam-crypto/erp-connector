using Connector.Core.Domain;

namespace Connector.Core.Interfaces;

/// <summary>
/// Schreibt die gemappten Records in das Ausgabeformat und erstellt das Manifest.
/// </summary>
/// <remarks>
/// Iteration 1: Excel (.xlsx). Das Interface isoliert das Format — Iteration 2 tauscht
/// nur die Implementierung aus, ohne andere Pipeline-Stufen zu berühren.
/// </remarks>
public interface IPackager
{
    /// <summary>
    /// Erzeugt ein <see cref="ExportPackage"/> mit Datei-Bytes und Manifest inkl. SHA-256.
    /// </summary>
    /// <param name="records">Bereits gemappte, minimierte Records.</param>
    /// <param name="sequenceNumber">Monotone Sequenznummer für diesen Export-Run.</param>
    /// <param name="ct">Abbruch-Token.</param>
    Task<ExportPackage> PackageAsync(
        IReadOnlyList<MappedExportRecord> records,
        int sequenceNumber,
        CancellationToken ct
    );
}
