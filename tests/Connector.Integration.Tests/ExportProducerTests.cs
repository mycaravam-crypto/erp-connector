using Connector.Core.Domain;
using Connector.Infrastructure;

namespace Connector.Integration.Tests;

/// <summary><see cref="AppSettingsStore.GetProducerAsync"/>: the instance id is created once and then reused.</summary>
public sealed class ExportProducerTests : SqliteDbContextTestBase
{
    [Fact]
    public async Task GetProducerAsync_ReturnsSameInstanceIdOnEveryCall()
    {
        var first = await Db.GetProducerAsync();
        var second = await Db.GetProducerAsync();

        Assert.Equal(ExportProducer.ApplicationName, first.Application);
        Assert.True(Guid.TryParse(first.InstanceId, out _));
        Assert.Equal(first.InstanceId, second.InstanceId);
        Assert.False(string.IsNullOrWhiteSpace(first.Version));
    }
}
