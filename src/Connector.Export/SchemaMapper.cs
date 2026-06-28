using Connector.Core.Domain;
using Connector.Core.Interfaces;

namespace Connector.Export;

/// <summary>
/// Transformiert <see cref="ExportItem"/> in einen <see cref="MappedExportRecord"/>
/// gemäß ICD-Schema Version <see cref="Schema.ExportSchema.Version"/>.
/// </summary>
public sealed class SchemaMapper : ISchemaMapper
{
    public MappedExportRecord Map(ExportItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Guid))
            throw new InvalidCorrelationKeyException(
                $"GUID fehlt für PartNumber '{item.PartNumber}' — Record kann nicht exportiert werden."
            );

        return new MappedExportRecord(
            Guid: item.Guid,
            // Leerer String statt null, damit Excel-Zellen nicht leer bleiben und Formeln brechen.
            SerialNumber: item.SerialNumber ?? string.Empty,
            PartNumber: item.PartNumber,
            ParentSerialNumber: item.ParentSerialNumber,
            ModelReference: item.ModelReference,
            CommissioningDateIso8601: item.CommissioningDate?.ToString("yyyy-MM-dd") ?? string.Empty,
            MaintenanceState: item.MaintenanceState ?? string.Empty
        );
    }
}
