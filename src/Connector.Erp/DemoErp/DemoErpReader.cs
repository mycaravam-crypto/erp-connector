using Connector.Core.Domain;
using Connector.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Connector.Erp.DemoErp;

/// <summary>
/// Implementiert <see cref="IErpReader"/> gegen die Demo-SQLite-Datenbank.
/// Steht für den echten PostgreSQL-Reader, bis die ERP-Anbindung geklärt ist.
/// </summary>
/// <remarks>
/// Die Abfrage ist read-only (<c>AsNoTracking</c>) und enthält bewusst alle Felder
/// inkl. ausgeschlossener Daten (TechnicianName, StorageLocation) — deren Entfernung
/// ist Aufgabe von <see cref="Connector.Export.DataMinimizer"/>, nicht des Readers.
/// </remarks>
public sealed class DemoErpReader(
    DemoErpDbContext db,
    ILogger<DemoErpReader> logger) : IErpReader
{
    public async Task<IReadOnlyList<ErpConfigurationItem>> ReadMaintainableCIsAsync(CancellationToken ct)
    {
        try
        {
            var configs = await db.SystemConfigurations
                .AsNoTracking()
                .Include(sc => sc.Article)
                .Include(sc => sc.MaintenancePlans.Where(mp => mp.Status == "Active"))
                .Include(sc => sc.ParentLinks)
                    .ThenInclude(link => link.Parent)
                .Where(sc => sc.MaintenancePlans.Any(mp => mp.Status == "Active"))
                .ToListAsync(ct);

            logger.LogInformation(
                "DemoErpReader: {Count} wartbare CIs mit aktivem Wartungsplan gelesen",
                configs.Count);

            return configs
                .Select(sc => new ErpConfigurationItem(
                    SerialNumber:      sc.Serial,
                    PartNumber:        sc.Article?.PartNumber,
                    ParentSerialNumber: sc.ParentLinks.FirstOrDefault()?.Parent?.Serial,
                    ModelReference:    sc.Article?.ArticleName,
                    CommissioningDate: sc.CommissionDate,
                    MaintenanceState:  sc.Status,
                    TechnicianName:    sc.TechnicianName,
                    StorageLocation:   sc.StorageLocation))
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ErpConnectionException(
                $"Demo-ERP nicht erreichbar oder Abfrage fehlgeschlagen: {ex.Message}", ex);
        }
    }
}
