using Connector.Core.Domain;
using Connector.Core.Interfaces;

namespace Connector.Export;

/// <summary>
/// Erzeugt einen <see cref="ExportItem"/> aus einem <see cref="ErpConfigurationItem"/>,
/// indem alle nicht-exportierbaren Felder weggelassen werden.
/// </summary>
/// <remarks>
/// Die Zuordnung "welches Feld darf raus" ist hier einmalig definiert und nicht konfigurierbar —
/// sie ergibt sich direkt aus dem Zweck (Garantieverwaltung + Produktverbesserung) und
/// den DSGVO-Grundsätzen. Wer die Liste ändern will, muss diese Klasse ändern und einen Review auslösen.
/// </remarks>
public sealed class DataMinimizer : IDataMinimizer
{
    public ExportItem Minimize(ErpConfigurationItem source) =>
        new(
            Guid: source.Guid!, // Filter hat null bereits ausgeschlossen
            SerialNumber: source.SerialNumber, // Kann fehlen — blockiert Export nicht
            PartNumber: source.PartNumber ?? string.Empty,
            ParentSerialNumber: source.ParentSerialNumber,
            ModelReference: source.ModelReference ?? string.Empty,
            CommissioningDate: source.CommissioningDate,
            MaintenanceState: source.MaintenanceState
        );
    // TechnicianName: nicht übertragen (DSGVO)
    // StorageLocation: nicht übertragen (Open Point #4 ungeklärt)
}
