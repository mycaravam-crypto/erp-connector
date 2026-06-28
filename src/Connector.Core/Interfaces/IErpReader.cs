using Connector.Core.Domain;

namespace Connector.Core.Interfaces;

/// <summary>
/// Liest den aktuellen Vollstand aller wartbaren Configuration Items aus dem ERP.
/// Iteration 1: immer Vollsnapshot — kein Delta-Parameter.
/// </summary>
/// <remarks>
/// Implementierungen müssen read-only sein: Schreibzugriff auf das ERP ist verboten.
/// Idempotenz ist Pflicht: mehrfache Aufrufe im gleichen Zeitfenster liefern denselben Stand.
/// </remarks>
public interface IErpReader
{
    /// <summary>
    /// Gibt alle wartbaren CIs zurück. Die Liste kann groß sein (Open Point #5: Volumen klären).
    /// </summary>
    /// <param name="ct">Abbruch-Token — Implementierung muss kooperativ abbrechen.</param>
    /// <exception cref="ErpConnectionException">Wenn das ERP nicht erreichbar ist.</exception>
    Task<IReadOnlyList<ErpConfigurationItem>> ReadMaintainableCIsAsync(CancellationToken ct);
}
