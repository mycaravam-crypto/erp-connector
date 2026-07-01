using Connector.Erp.DemoErp;
using Microsoft.EntityFrameworkCore;

namespace Connector.Api.Endpoints;

static class ErpEndpoints
{
    internal static void MapErpEndpoints(this WebApplication app)
    {
        app.MapGet(
                "/api/erp/records",
                async (DemoErpDbContext erpDb, int? limit) =>
                {
                    var cap = limit ?? 500;
                    var total = await erpDb.SystemConfigurations.CountAsync();

                    var configs = await erpDb
                        .SystemConfigurations.AsNoTracking()
                        .Include(sc => sc.Article)
                        .Include(sc => sc.MaintenancePlans)
                        .Include(sc => sc.ParentLinks)
                        .ThenInclude(l => l.Parent)
                        .OrderBy(sc => sc.Id)
                        .Take(cap)
                        .ToListAsync();

                    var records = configs
                        .Select(sc =>
                        {
                            var activePlan = sc.MaintenancePlans.FirstOrDefault(
                                mp => mp.Status == "Active"
                            );
                            var anyPlan = sc.MaintenancePlans.FirstOrDefault();
                            bool inScope = activePlan != null;
                            string? exclusionReason = null;
                            if (!inScope)
                                exclusionReason = sc.MaintenancePlans.Any()
                                    ? "Inactive maintenance plan"
                                    : "No maintenance plan";

                            return new ErpCiRecord(
                                Id: sc.Id,
                                Serial: sc.Serial,
                                Status: sc.Status,
                                CommissionDate: sc.CommissionDate?.ToString("yyyy-MM-dd"),
                                ArticleName: sc.Article?.ArticleName,
                                PartNumber: sc.Article?.PartNumber,
                                Manufacturer: sc.Article?.Manufacturer,
                                MaintenancePlanStatus: activePlan?.Status ?? anyPlan?.Status,
                                AllocationChartRef: activePlan?.AllocationChartRef
                                    ?? anyPlan?.AllocationChartRef,
                                ParentId: sc.ParentLinks.FirstOrDefault()?.ParentId,
                                ParentSerial: sc.ParentLinks.FirstOrDefault()?.Parent?.Serial,
                                InScope: inScope,
                                ExclusionReason: exclusionReason,
                                TechnicianName: sc.TechnicianName,
                                StorageLocation: sc.StorageLocation
                            );
                        })
                        .ToList();

                    return Results.Ok(new ErpRecordsResult(records, total));
                }
            )
            .RequireAuthorization();
    }
}
