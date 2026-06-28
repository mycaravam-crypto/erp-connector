using Connector.Core.Domain;

namespace Connector.Core.Interfaces;

/// <summary>
/// Entfernt alle Felder, die nicht exportiert werden dürfen (DSGVO Art. 5 Abs. 1 lit. c).
/// </summary>
/// <remarks>
/// Minimierung findet im Arbeitsspeicher statt, bevor irgendetwas auf Disk geschrieben wird.
/// Ausgeschlossene Felder (z.B. TechnicianName) werden nicht in Zwischendateien oder Logs persistiert.
/// </remarks>
public interface IDataMinimizer
{
    /// <summary>
    /// Gibt einen <see cref="ExportItem"/> zurück, der ausschließlich exportierbare Felder enthält.
    /// </summary>
    ExportItem Minimize(ErpConfigurationItem source);
}
