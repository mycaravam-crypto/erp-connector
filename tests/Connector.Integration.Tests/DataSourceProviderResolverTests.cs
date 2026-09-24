using Connector.Core.DataSources;
using Connector.Infrastructure;
using Connector.Infrastructure.DataSources;
using Connector.Infrastructure.DataSources.MariaDb;
using Connector.Infrastructure.DataSources.PostgreSql;

namespace Connector.Integration.Tests;

/// <summary>
/// Pure unit coverage for <see cref="DataSourceProviderResolver"/> — no database required. Covers provider
/// resolution and the error for an unsupported provider.
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

    [Fact]
    public void Resolve_MariaDb_ReturnsTheRegisteredMariaDbProvider()
    {
        var mariaDb = new MariaDbDataSourceProvider();
        var resolver = new DataSourceProviderResolver([new PostgreSqlDataSourceProvider(), mariaDb]);

        Assert.Same(mariaDb, resolver.Resolve(DataSourceType.MariaDb));
    }

    // A type with no registered provider (ServiceNowTableApi/ServiceNowSqlApi today, or MariaDb when it isn't
    // registered) must fail clearly rather than silently returning some other provider.
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

    // A DataSourceConfig can carry a numeric Type value outside every known enum member
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
