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
        if (string.IsNullOrWhiteSpace(item.SerialNumber))
            throw new InvalidCorrelationKeyException(
                $"SerialNumber fehlt für PartNumber '{item.PartNumber}' — Record kann nicht exportiert werden.");

        return new MappedExportRecord(
            SerialNumber: item.SerialNumber,
            PartNumber: item.PartNumber,
            ParentSerialNumber: item.ParentSerialNumber,
            ModelReference: item.ModelReference,
            // Leerer String statt null, damit Excel-Zellen nicht leer bleiben und Formeln brechen.
            CommissioningDateIso8601: item.CommissioningDate?.ToString("yyyy-MM-dd") ?? string.Empty,
            MaintenanceState: item.MaintenanceState ?? string.Empty);
    }
}
