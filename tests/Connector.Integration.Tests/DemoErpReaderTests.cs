using Connector.Erp.DemoErp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Connector.Integration.Tests;

/// <summary>
/// Testet <see cref="DemoErpReader"/> gegen eine In-Memory-SQLite-Datenbank
/// mit den Seed-Daten aus <see cref="DemoErpSeed"/>.
/// </summary>
public sealed class DemoErpReaderTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DemoErpDbContext _db;

    public DemoErpReaderTests()
    {
        // Eigene Verbindung offen halten — SQLite-In-Memory-DBs leben nur solange die Verbindung offen ist.
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<DemoErpDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new DemoErpDbContext(options);
        _db.Database.EnsureCreated();
        DemoErpSeed.Seed(_db);
    }

    [Fact]
    public async Task Reader_ReturnsOnlyInScopeCIs()
    {
        var sut = new DemoErpReader(_db, NullLogger<DemoErpReader>.Instance);

        var result = await sut.ReadMaintainableCIsAsync(CancellationToken.None);

        Assert.Equal(5, result.Count);
    }

    [Fact]
    public async Task Reader_ExcludesCIWithoutMaintenancePlan()
    {
        var sut = new DemoErpReader(_db, NullLogger<DemoErpReader>.Instance);

        var result = await sut.ReadMaintainableCIsAsync(CancellationToken.None);

        // sc-psu-0002 hat keinen Wartungsplan — darf nicht im Ergebnis erscheinen.
        Assert.DoesNotContain(result, r => r.SerialNumber == DemoErpSeed.Ids.SnPsu2);
    }

    [Fact]
    public async Task Reader_ExcludesCIWithInactiveMaintenancePlan()
    {
        var sut = new DemoErpReader(_db, NullLogger<DemoErpReader>.Instance);

        var result = await sut.ReadMaintainableCIsAsync(CancellationToken.None);

        // sc-rack-0002 hat einen inaktiven Wartungsplan — darf nicht im Ergebnis erscheinen.
        Assert.DoesNotContain(result, r => r.SerialNumber == DemoErpSeed.Ids.SnRack2);
    }

    [Fact]
    public async Task Reader_ReturnedSerials_MatchExpectedInScopeSet()
    {
        var sut = new DemoErpReader(_db, NullLogger<DemoErpReader>.Instance);

        var result = await sut.ReadMaintainableCIsAsync(CancellationToken.None);
        var serials = result.Select(r => r.SerialNumber!).ToHashSet();

        Assert.Equal(DemoErpSeed.Ids.InScopeSerials, serials);
    }

    [Fact]
    public async Task Reader_ChildCI_HasParentSerialNumber()
    {
        var sut = new DemoErpReader(_db, NullLogger<DemoErpReader>.Instance);

        var result = await sut.ReadMaintainableCIsAsync(CancellationToken.None);
        var blade1 = result.Single(r => r.SerialNumber == DemoErpSeed.Ids.SnBlade1);

        Assert.Equal(DemoErpSeed.Ids.SnRack1, blade1.ParentSerialNumber);
    }

    [Fact]
    public async Task Reader_RootCI_HasNullParentSerial()
    {
        var sut = new DemoErpReader(_db, NullLogger<DemoErpReader>.Instance);

        var result = await sut.ReadMaintainableCIsAsync(CancellationToken.None);
        var rack1 = result.Single(r => r.SerialNumber == DemoErpSeed.Ids.SnRack1);

        Assert.Null(rack1.ParentSerialNumber);
    }

    [Fact]
    public async Task Reader_IncludesPersonalData_ForMinimizerToStrip()
    {
        // Der Reader gibt TechnicianName weiter — DataMinimizer ist für die Entfernung zuständig.
        // Dieser Test bestätigt, dass die Rohdaten vollständig ankommen.
        var sut = new DemoErpReader(_db, NullLogger<DemoErpReader>.Instance);

        var result = await sut.ReadMaintainableCIsAsync(CancellationToken.None);

        Assert.All(result, r => Assert.False(string.IsNullOrEmpty(r.TechnicianName)));
    }

    [Fact]
    public async Task Reader_CommissioningDate_IsPopulated()
    {
        var sut = new DemoErpReader(_db, NullLogger<DemoErpReader>.Instance);

        var result = await sut.ReadMaintainableCIsAsync(CancellationToken.None);
        var rack1 = result.Single(r => r.SerialNumber == DemoErpSeed.Ids.SnRack1);

        Assert.Equal(new DateOnly(2023, 3, 1), rack1.CommissioningDate);
    }

    [Fact]
    public async Task Reader_ModelReference_IsPopulated()
    {
        var sut = new DemoErpReader(_db, NullLogger<DemoErpReader>.Instance);

        var result = await sut.ReadMaintainableCIsAsync(CancellationToken.None);
        var rack1 = result.Single(r => r.SerialNumber == DemoErpSeed.Ids.SnRack1);

        Assert.Equal("Industrial Rack System", rack1.ModelReference);
    }

    [Fact]
    public async Task Reader_CancellationToken_PropagatesCorrectly()
    {
        var sut = new DemoErpReader(_db, NullLogger<DemoErpReader>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.ReadMaintainableCIsAsync(cts.Token));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
