using Connector.Core.DataSources;
using Connector.Infrastructure.DataSources;

namespace Connector.Integration.Tests;

/// <summary><see cref="MeteredDataSourceProvider"/> (Arbeitsauftrag 13): counts queries and rows, forwards
/// everything else.</summary>
public sealed class MeteredDataSourceProviderTests
{
    [Fact]
    public async Task CountsQueriesAndRows_AndForwardsCapabilities()
    {
        var inner = FakeServiceNow.WithExportFixture().CreateProvider();
        var metered = new MeteredDataSourceProvider(inner);

        await metered.ExecuteAsync(
            FakeServiceNow.Config,
            new SourceQuery { RootTable = "export_order" },
            CancellationToken.None
        );
        await metered.ExecuteAsync(
            FakeServiceNow.Config,
            new SourceQuery { RootTable = "export_customer", Limit = 1 },
            CancellationToken.None
        );

        Assert.Equal(2, metered.Metrics.QueryCount);
        Assert.Equal(4, metered.Metrics.RecordsRead);
        Assert.Same(inner.Capabilities, metered.Capabilities);
        Assert.Throws<UnsupportedDataSourceException>(() => metered.Dialect);
    }
}
