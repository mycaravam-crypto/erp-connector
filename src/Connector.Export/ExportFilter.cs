using Connector.Core.Domain;
using Connector.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Connector.Export;

/// <summary>
/// Filtert CIs anhand des Scope-Entitlements für Iteration 1.
/// </summary>
/// <remarks>
/// Iteration 1: Ein CI ist im Scope, wenn es wartbar ist (durch den ERP-Reader bereits
/// sichergestellt) und eine nicht-leere Seriennummer hat. CIs ohne Seriennummer werden
/// ausgeschlossen und protokolliert — sie können den Korrelationsschlüssel nicht erfüllen.
/// </remarks>
public sealed class ExportFilter(ILogger<ExportFilter> logger) : IExportFilter
{
    public IReadOnlyList<ErpConfigurationItem> Filter(IReadOnlyList<ErpConfigurationItem> items)
    {
        var result = new List<ErpConfigurationItem>(items.Count);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.SerialNumber))
            {
                // Korrelationsschlüssel fehlt — CI kann auf ServiceNow-Seite keinem Asset zugeordnet werden.
                logger.LogWarning(
                    "CI ausgeschlossen (kein Korrelationsschlüssel): PartNumber={PartNumber}",
                    item.PartNumber);
                continue;
            }

            result.Add(item);
        }

        logger.LogInformation(
            "Filter: {Total} CIs gelesen, {Included} im Scope, {Excluded} ausgeschlossen",
            items.Count, result.Count, items.Count - result.Count);

        return result;
    }
}
