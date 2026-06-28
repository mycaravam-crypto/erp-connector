using Connector.Core.Domain;
using Connector.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Connector.Export;

/// <summary>
/// Filtert CIs anhand des Scope-Entitlements für Iteration 1.
/// </summary>
/// <remarks>
/// Iteration 1: Ein CI ist im Scope, wenn es wartbar ist (durch den ERP-Reader bereits
/// sichergestellt) und eine nicht-leere GUID hat. CIs ohne GUID werden ausgeschlossen und
/// protokolliert — sie können den Coalesce-Schlüssel auf ServiceNow-Seite nicht erfüllen.
/// Eine fehlende Seriennummer blockiert den Export nicht.
/// </remarks>
public sealed class ExportFilter(ILogger<ExportFilter> logger) : IExportFilter
{
    public IReadOnlyList<ErpConfigurationItem> Filter(IReadOnlyList<ErpConfigurationItem> items)
    {
        var result = new List<ErpConfigurationItem>(items.Count);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Guid))
            {
                // Coalesce-Schlüssel fehlt — CI kann auf ServiceNow-Seite keinem Asset zugeordnet werden.
                logger.LogWarning(
                    "CI ausgeschlossen (keine GUID): PartNumber={PartNumber}, Serial={Serial}",
                    item.PartNumber,
                    item.SerialNumber
                );
                continue;
            }

            result.Add(item);
        }

        logger.LogInformation(
            "Filter: {Total} CIs gelesen, {Included} im Scope, {Excluded} ausgeschlossen",
            items.Count,
            result.Count,
            items.Count - result.Count
        );

        return result;
    }
}
