namespace Connector.Core.DataSources;

/// <summary>
/// Thrown by <see cref="SourceQueryValidator.Validate"/> (and so by any provider compiling a
/// <see cref="SourceQuery"/>) when the query names a table or column the schema doesn't know, or is otherwise
/// malformed. Raised before anything reaches the data source.
/// </summary>
public sealed class InvalidSourceQueryException : Exception
{
    public InvalidSourceQueryException() { }

    public InvalidSourceQueryException(string message)
        : base(message) { }

    public InvalidSourceQueryException(string message, Exception innerException)
        : base(message, innerException) { }
}
