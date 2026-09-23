namespace Connector.Core.DataSources;

/// <summary>
/// Provider-agnostic wrapper for a query execution failure that a caller needs to inspect, not just log or
/// surface verbatim — e.g. to recognize a specific SQL error without referencing <c>Npgsql.PostgresException</c>/
/// <c>SqlState</c> directly. <see cref="ErrorCode"/> is the provider's own error-code string verbatim (the
/// Postgres SQLSTATE, for <c>PostgreSqlDataSourceProvider</c>) — opaque to callers unless they know the
/// specific code they're looking for.
/// </summary>
public sealed class DataSourceQueryException : Exception
{
    public DataSourceQueryException() { }

    public DataSourceQueryException(string message)
        : base(message) { }

    public DataSourceQueryException(string message, Exception innerException)
        : base(message, innerException) { }

    public DataSourceQueryException(string? errorCode, string message, Exception? innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string? ErrorCode { get; init; }
}
