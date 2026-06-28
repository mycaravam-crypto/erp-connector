using Connector.Core.Domain;

namespace Connector.Core.Interfaces;

/// <summary>
/// Transformiert ERP-Feldnamen und -Formate auf das vereinbarte Export-Schema (ICD).
/// </summary>
/// <remarks>
/// Das Schema ist in <see cref="Schema.ExportSchema"/> versioniert. Bruchänderungen
/// erhöhen die MAJOR-Version und müssen vor Deployment mit dem Hersteller koordiniert werden.
/// </remarks>
public interface ISchemaMapper
{
    /// <summary>
    /// Gibt einen vollständig formatierten <see cref="MappedExportRecord"/> zurück.
    /// </summary>
    /// <exception cref="InvalidCorrelationKeyException">
    /// Wenn SerialNumber null oder leer ist — solche Records dürfen das System nicht verlassen.
    /// </exception>
    MappedExportRecord Map(ExportItem item);
}
