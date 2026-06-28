namespace Connector.Core.Domain;

/// <summary>ERP-Verbindung nicht erreichbar oder Abfrage fehlgeschlagen.</summary>
public sealed class ErpConnectionException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Seriennummer fehlt oder ist leer — der Record kann nicht exportiert werden,
/// da der Korrelationsschlüssel auf ServiceNow-Seite sonst einen Duplikat erzeugen würde.
/// </summary>
public sealed class InvalidCorrelationKeyException(string message)
    : Exception(message);

/// <summary>Staging-Pfad nicht schreibbar oder Datei-Operation fehlgeschlagen.</summary>
public sealed class ExportSinkException(string message, Exception? inner = null)
    : Exception(message, inner);
