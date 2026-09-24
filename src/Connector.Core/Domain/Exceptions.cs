namespace Connector.Core.Domain;

/// <summary>The staging path is not writable or a file operation failed.</summary>
public sealed class ExportSinkException : Exception
{
    public ExportSinkException() { }

    public ExportSinkException(string message)
        : base(message) { }

    public ExportSinkException(string message, Exception innerException)
        : base(message, innerException) { }
}
