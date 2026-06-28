using Connector.Core.Domain;

namespace Connector.Core.Interfaces;

/// <summary>
/// Wendet den Scope-Filter an: behält nur CIs, die dem Export-Entitlement entsprechen.
/// </summary>
/// <remarks>
/// Die gefilterten CIs werden mit Begründung protokolliert, sodass Audits nachvollziehen können,
/// warum ein CI nicht im Export erscheint — ohne in Logs zu schauen, die ausgeschlossene Daten enthielten.
/// </remarks>
public interface IExportFilter
{
    /// <summary>
    /// Gibt die Teilmenge zurück, die exportiert werden darf.
    /// Synchron, da keine I/O-Abhängigkeit: die Filterregeln sind im Speicher.
    /// </summary>
    IReadOnlyList<ErpConfigurationItem> Filter(IReadOnlyList<ErpConfigurationItem> items);
}
