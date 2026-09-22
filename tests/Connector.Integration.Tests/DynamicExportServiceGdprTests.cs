using Connector.Infrastructure;

namespace Connector.Integration.Tests;

/// <summary>
/// Coverage for <see cref="DynamicExportService.GetDeniedFieldsAsync"/> — the fallback-to-hardcoded-defaults
/// and DB-override branches behind <c>GET /api/gdpr-denied-fields</c> — against a known-empty
/// <see cref="LocalDb"/> instance rather than through the shared-state HTTP tests in
/// <c>SettingsEndpointsHttpTests</c>, so "nothing stored yet" is guaranteed rather than order-dependent.
/// </summary>
public sealed class DynamicExportServiceGdprTests
{
    [Fact]
    public async Task GetDeniedFieldsAsync_NothingStored_ReturnsHardcodedDefaults()
    {
        await using var local = await LocalDb.NewAsync();

        var fields = await DynamicExportService.GetDeniedFieldsAsync(local.Db);

        Assert.Equal(DynamicExportService.GdprDeniedFields, fields);
    }

    [Fact]
    public async Task GetDeniedFieldsAsync_CustomListStored_ReturnsStoredListInsteadOfDefaults()
    {
        await using var local = await LocalDb.NewAsync();
        await local.Db.SetSettingAsync(SettingsKeys.GdprDeniedFields, new List<string> { "custom_field" });

        var fields = await DynamicExportService.GetDeniedFieldsAsync(local.Db);

        Assert.Equal(new[] { "custom_field" }, fields);
    }
}
