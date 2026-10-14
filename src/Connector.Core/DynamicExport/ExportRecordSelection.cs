namespace Connector.Core.DynamicExport;

/// <summary>
/// Optional, server-derived root selection: the configured correlation column and caller-supplied values.
/// The SQL compiler binds the values as parameters and intersects them with the saved root filter.
/// </summary>
public sealed record ExportRecordSelection(string Column, string[] RecordIds);
