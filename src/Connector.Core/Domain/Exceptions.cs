namespace Connector.Core.Domain;

/// <summary>Staging-Pfad nicht schreibbar oder Datei-Operation fehlgeschlagen.</summary>
public sealed class ExportSinkException : Exception
{
    public ExportSinkException() { }

    public ExportSinkException(string message)
        : base(message) { }

    public ExportSinkException(string message, Exception innerException)
        : base(message, innerException) { }
}
