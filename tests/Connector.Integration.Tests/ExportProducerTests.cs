using Connector.Core.Domain;
using Connector.Infrastructure;

namespace Connector.Integration.Tests;

/// <summary><see cref="AppSettingsStore.GetProducerAsync"/>: the instance id is created once and then reused;
/// <see cref="AppSettingsStore.RegenerateInstanceIdAsync"/> replaces it and keeps the old one as this installation's.</summary>
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

    [Fact]
    public async Task RegenerateInstanceIdAsync_ReplacesIdAndKeepsOldOneAsOwn()
    {
        var original = (await Db.GetProducerAsync()).InstanceId;

        var (oldId, newId) = await Db.RegenerateInstanceIdAsync();

        Assert.Equal(original, oldId);
        Assert.NotEqual(oldId, newId);
        Assert.True(Guid.TryParse(newId, out _));
        Assert.Equal(newId, (await Db.GetProducerAsync()).InstanceId);
        Assert.True(await Db.IsOwnInstanceIdAsync(newId));
        Assert.True(await Db.IsOwnInstanceIdAsync(oldId));
    }

    [Fact]
    public async Task RegenerateInstanceIdAsync_Twice_KeepsEveryPreviousId()
    {
        var (first, _) = await Db.RegenerateInstanceIdAsync();
        var (second, current) = await Db.RegenerateInstanceIdAsync();

        Assert.True(await Db.IsOwnInstanceIdAsync(first));
        Assert.True(await Db.IsOwnInstanceIdAsync(second));
        Assert.True(await Db.IsOwnInstanceIdAsync(current));
    }

    [Fact]
    public async Task IsOwnInstanceIdAsync_OtherOrMissingId_ReturnsFalse()
    {
        await Db.GetProducerAsync();

        Assert.False(await Db.IsOwnInstanceIdAsync(Guid.NewGuid().ToString()));
        Assert.False(await Db.IsOwnInstanceIdAsync(null));
    }
}
