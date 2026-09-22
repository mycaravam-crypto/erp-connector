namespace Connector.Core.DataSources;

/// <summary>
/// Thrown by <see cref="IDataSourceProviderResolver.Resolve"/> when no <see cref="IDataSourceProvider"/> is
/// registered for the requested <see cref="DataSourceType"/> — e.g. <see cref="DataSourceType.MariaDb"/>/
/// <see cref="DataSourceType.ServiceNowTableApi"/>/<see cref="DataSourceType.ServiceNowSqlApi"/>, which
/// Arbeitsauftrag 2/3 explicitly defer, or any other value with no registered provider.
/// </summary>
public sealed class UnsupportedDataSourceException : NotSupportedException
{
    public UnsupportedDataSourceException() { }

    public UnsupportedDataSourceException(string message)
        : base(message) { }

    public UnsupportedDataSourceException(string message, Exception innerException)
        : base(message, innerException) { }

    public UnsupportedDataSourceException(DataSourceType type)
        : base($"No IDataSourceProvider is registered for data source type '{type}'.")
    {
        RequestedType = type;
    }

    public DataSourceType? RequestedType { get; init; }
}
