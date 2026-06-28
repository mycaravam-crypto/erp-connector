namespace Connector.Core.Domain;

/// <summary>
/// CI-Datensatz nach der Minimierung: enthält ausschließlich Felder, die exportiert werden dürfen.
/// Alle personenbezogenen und nicht entitlierten Felder sind entfernt.
/// </summary>
public sealed record ExportItem(
    string SerialNumber,
    string PartNumber,
    string? ParentSerialNumber,
    string ModelReference,
    DateOnly? CommissioningDate,
    string? MaintenanceState);
