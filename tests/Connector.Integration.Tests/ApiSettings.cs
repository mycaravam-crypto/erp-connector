using Connector.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Connector.Integration.Tests;

/// <summary>
/// Direct AppSetting read/write against <see cref="ApiFactory"/>'s DB, for HTTP-layer tests that need a
/// specific <c>SettingsKeys</c> value present or absent before making a request. Every HTTP-layer test
/// class in <see cref="ApiCollection"/> shares one DB, so a test that needs "nothing stored yet" (e.g. the
/// no-export-mapping-configured guard in <c>PipelineEndpoints</c>) must actively clear that key itself
/// rather than assume no other test in the collection has set it — xUnit guarantees no ordering, within a
/// class or across classes in the same collection.
/// </summary>
internal static class ApiSettings
{
    internal static async Task ClearAsync(this ApiFactory factory, params string[] keys)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExportLogDbContext>();
        var rows = await db.AppSettings.Where(s => keys.Contains(s.Key)).ToListAsync();
        db.AppSettings.RemoveRange(rows);
        await db.SaveChangesAsync();
    }

    internal static async Task SetAsync<T>(this ApiFactory factory, string key, T value)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExportLogDbContext>();
        await db.SetSettingAsync(key, value);
    }
}
