namespace Connector.Core.Domain;

/// <summary>
/// CI-Datensatz nach der Minimierung: enthält ausschließlich Felder, die exportiert werden dürfen.
/// Alle personenbezogenen und nicht entitlierten Felder sind entfernt.
/// </summary>
/// <remarks>
/// Dieser Typ ist die Sicherheitsgrenze innerhalb der Pipeline: nur was hier steht, kann je
/// in den Export gelangen. <see cref="ErpConfigurationItem"/> enthält noch TechnicianName und
/// StorageLocation — dieser Record nicht. Die Minimierungsregel ist damit im Typsystem verankert,
/// nicht nur in der Laufzeitlogik von <c>DataMinimizer</c>.
/// </remarks>
public sealed record ExportItem(
    string SerialNumber,
    string PartNumber,
    string? ParentSerialNumber,
    string ModelReference,
    DateOnly? CommissioningDate,
    string? MaintenanceState);
