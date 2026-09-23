using Connector.Core.DataSources;
using Connector.Infrastructure;
using Connector.Infrastructure.DataSources;
using Connector.Infrastructure.DataSources.PostgreSql;

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

    // MariaDb/ServiceNowTableApi/ServiceNowSqlApi are deliberately unimplemented (Arbeitsauftrag 2/3) — no
    // provider is ever registered for any of them, so resolving them must fail clearly rather than silently
    // returning some other provider.
    [Theory]
    [InlineData(DataSourceType.MariaDb)]
    [InlineData(DataSourceType.ServiceNowTableApi)]
    [InlineData(DataSourceType.ServiceNowSqlApi)]
    public void Resolve_UnregisteredType_ThrowsUnsupportedDataSourceException(DataSourceType type)
    {
        var resolver = new DataSourceProviderResolver([new PostgreSqlDataSourceProvider()]);

        var ex = Assert.Throws<UnsupportedDataSourceException>(() => resolver.Resolve(type));

        Assert.Equal(type, ex.RequestedType);
    }

    // Arbeitsauftrag 3: a DataSourceConfig can carry a numeric Type value outside every known enum member
    // (e.g. deserialized from a future/foreign payload) — Resolve must reject it the same way as any other
    // unregistered type, not throw an unrelated error or silently match PostgreSql via a bad default.
    [Fact]
    public void Resolve_OutOfRangeType_ThrowsUnsupportedDataSourceException()
    {
        var resolver = new DataSourceProviderResolver([new PostgreSqlDataSourceProvider()]);
        var unknownType = (DataSourceType)999;

        var ex = Assert.Throws<UnsupportedDataSourceException>(() => resolver.Resolve(unknownType));

        Assert.Equal(unknownType, ex.RequestedType);
    }

    [Fact]
    public void Resolve_NoProvidersRegisteredAtAll_ThrowsForPostgreSqlToo()
    {
        var resolver = new DataSourceProviderResolver([]);

        Assert.Throws<UnsupportedDataSourceException>(() => resolver.Resolve(DataSourceType.PostgreSql));
    }
}
