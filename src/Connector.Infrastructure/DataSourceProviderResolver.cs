using Connector.Core.DataSources;

namespace Connector.Infrastructure;

/// <summary>
/// Resolves every DI-registered <see cref="IDataSourceProvider"/> by its own <see cref="IDataSourceProvider.Type"/>
/// — adding a future MariaDb/ServiceNowTableApi/ServiceNowSqlApi provider (Arbeitsauftrag 2/3 explicitly
/// defer all three) is a DI
/// registration in <c>Program.cs</c>, never a change to this resolver.
/// </summary>
public sealed class DataSourceProviderResolver : IDataSourceProviderResolver
{
    private readonly IReadOnlyDictionary<DataSourceType, IDataSourceProvider> _providers;

    public DataSourceProviderResolver(IEnumerable<IDataSourceProvider> providers) =>
        _providers = providers.ToDictionary(p => p.Type);

    public IDataSourceProvider Resolve(DataSourceType type) =>
        _providers.TryGetValue(type, out var provider) ? provider : throw new UnsupportedDataSourceException(type);
}
