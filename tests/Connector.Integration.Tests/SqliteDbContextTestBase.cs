using Connector.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Connector.Integration.Tests;

/// <summary>
/// Base for tests exercising <see cref="ExportLogDbContext"/> logic against a fresh in-memory Sqlite
/// database — no live ERP/testdb connection needed. A Sqlite in-memory database only lives as long as its
/// connection stays open, so the connection (not the context) owns the database's lifetime across a test's
/// several SaveChanges calls. xunit constructs a fresh instance of the test class (and so a fresh
/// connection/context pair) per test method.
/// </summary>
internal abstract class SqliteDbContextTestBase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    protected ExportLogDbContext Db { get; }

    protected SqliteDbContextTestBase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ExportLogDbContext>().UseSqlite(_connection).Options;
        Db = new ExportLogDbContext(options);
        Db.Database.EnsureCreated();
    }

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
