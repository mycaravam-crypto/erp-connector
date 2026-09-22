using Connector.Core.DataSources;
using Connector.Infrastructure;

namespace Connector.Integration.Tests;

/// <summary>
/// Pure unit coverage for <see cref="DataSourceProviderResolver"/> — no database required. Covers
/// Arbeitsauftrag 2's explicit test list items "Provider-Auflösung" and "Fehlerszenario bei nicht
/// unterstütztem Provider".
/// </summary>
public sealed class DataSourceProviderResolverTests
{
    [Fact]
    public void Resolve_PostgreSql_ReturnsTheRegisteredPostgreSqlProvider()
    {
        var provider = new PostgreSqlDataSourceProvider();
        var resolver = new DataSourceProviderResolver([provider]);

        var resolved = resolver.Resolve(DataSourceType.PostgreSql);

        Assert.Same(provider, resolved);
        Assert.Equal(DataSourceType.PostgreSql, resolved.Type);
    }

    // MariaDb/ServiceNow are deliberately unimplemented (Arbeitsauftrag 2) — no provider is ever registered
    // for either, so resolving them must fail clearly rather than silently returning some other provider.
    [Theory]
    [InlineData(DataSourceType.MariaDb)]
    [InlineData(DataSourceType.ServiceNow)]
    public void Resolve_UnregisteredType_ThrowsUnsupportedDataSourceException(DataSourceType type)
    {
        var resolver = new DataSourceProviderResolver([new PostgreSqlDataSourceProvider()]);

        var ex = Assert.Throws<UnsupportedDataSourceException>(() => resolver.Resolve(type));

        Assert.Equal(type, ex.RequestedType);
    }

    [Fact]
    public void Resolve_NoProvidersRegisteredAtAll_ThrowsForPostgreSqlToo()
    {
        var resolver = new DataSourceProviderResolver([]);

        Assert.Throws<UnsupportedDataSourceException>(() => resolver.Resolve(DataSourceType.PostgreSql));
    }
}
