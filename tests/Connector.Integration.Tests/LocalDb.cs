using Connector.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Connector.Integration.Tests;

/// <summary>
/// Bundles an in-memory Sqlite connection with the <see cref="ExportLogDbContext"/> built on top of it so a
/// test can dispose both through one <c>await using</c>, without EF's connection-ownership rules leaving the
/// raw <see cref="SqliteConnection"/> to leak. <see cref="NewAsync"/> pre-seeds the ERP connection setting
/// that <c>GetSettingRawAsync</c> callers (<c>ImportRunReleaser.ReleaseAsync</c>,
/// <c>ImportDefinitionEndpoints.ValidateRequestAsync</c>, <c>ImportWorker</c>) read.
/// </summary>
internal sealed record LocalDb(ExportLogDbContext Db, SqliteConnection Connection) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await Connection.DisposeAsync();
    }

    internal static async Task<LocalDb> NewAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ExportLogDbContext>().UseSqlite(connection).Options;
        var db = new ExportLogDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await db.SetSettingAsync(SettingsKeys.ErpConnection, ErpTestFixture.Config);
        return new LocalDb(db, connection);
    }
}
