namespace Connector.Core.Domain;

/// <summary>ERP-Verbindung nicht erreichbar oder Abfrage fehlgeschlagen.</summary>
public sealed class ErpConnectionException : Exception
{
    public ErpConnectionException() { }

    public ErpConnectionException(string message)
        : base(message) { }

    public ErpConnectionException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Seriennummer fehlt oder ist leer — der Record kann nicht exportiert werden,
/// da der Korrelationsschlüssel auf ServiceNow-Seite sonst einen Duplikat erzeugen würde.
/// </summary>
public sealed class InvalidCorrelationKeyException : Exception
{
    public InvalidCorrelationKeyException() { }

    public InvalidCorrelationKeyException(string message)
        : base(message) { }

    public InvalidCorrelationKeyException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>Staging-Pfad nicht schreibbar oder Datei-Operation fehlgeschlagen.</summary>
public sealed class ExportSinkException : Exception
{
    public ExportSinkException() { }

    public ExportSinkException(string message)
        : base(message) { }

    public ExportSinkException(string message, Exception innerException)
        : base(message, innerException) { }
}
