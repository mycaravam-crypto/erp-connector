namespace Connector.Core.DataSources;

/// <summary>
/// Resolves a <see cref="DataSourceType"/> to the <see cref="IDataSourceProvider"/> that implements it. The
/// single seam a caller (e.g. <c>Connector.Infrastructure.DynamicExportService</c>, <c>ConnectionEndpoints</c>)
/// goes through instead of constructing a concrete provider itself.
/// </summary>
public interface IDataSourceProviderResolver
{
    /// <summary>Returns the registered provider for <paramref name="type"/>, or throws
    /// <see cref="UnsupportedDataSourceException"/> if none is registered (Arbeitsauftrag 2: only
    /// <see cref="DataSourceType.PostgreSql"/> is implemented today).</summary>
    IDataSourceProvider Resolve(DataSourceType type);
}
