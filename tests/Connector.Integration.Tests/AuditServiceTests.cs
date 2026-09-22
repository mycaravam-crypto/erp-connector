using Connector.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Connector.Integration.Tests;

/// <summary>
/// Coverage for <see cref="AuditService.LogAsync"/>: the happy path that appends a row, and the "audit must
/// never interrupt business logic" contract on its doc comment — a write failure is logged and swallowed,
/// never rethrown to the caller.
/// </summary>
public sealed class AuditServiceTests
{
    [Fact]
    public async Task LogAsync_ValidInputs_PersistsEntryWithProvidedFields()
    {
        await using var local = await LocalDb.NewAsync();
        var audit = new AuditService(local.Db, NullLogger<AuditService>.Instance);

        await audit.LogAsync("alice", "export_definition_created", "id=42");

        var entry = Assert.Single(local.Db.AuditLog);
        Assert.Equal("alice", entry.Username);
        Assert.Equal("export_definition_created", entry.Action);
        Assert.Equal("id=42", entry.Detail);
        Assert.False(string.IsNullOrWhiteSpace(entry.Timestamp));
    }

    [Fact]
    public async Task LogAsync_DetailOmitted_PersistsNullDetail()
    {
        await using var local = await LocalDb.NewAsync();
        var audit = new AuditService(local.Db, NullLogger<AuditService>.Instance);

        await audit.LogAsync("bob", "login");

        var entry = Assert.Single(local.Db.AuditLog);
        Assert.Null(entry.Detail);
    }

    [Fact]
    public async Task LogAsync_MultipleCalls_EachAppendsItsOwnRow()
    {
        await using var local = await LocalDb.NewAsync();
        var audit = new AuditService(local.Db, NullLogger<AuditService>.Instance);

        await audit.LogAsync("alice", "action_one");
        await audit.LogAsync("bob", "action_two");

        Assert.Equal(2, local.Db.AuditLog.Count());
    }

    [Fact]
    public async Task LogAsync_WriteFails_ExceptionIsSwallowedNotThrown()
    {
        await using var local = await LocalDb.NewAsync();
        var audit = new AuditService(local.Db, NullLogger<AuditService>.Instance);

        // Closing the connection drops the in-memory Sqlite database out from under the DbContext, so the
        // next SaveChangesAsync fails — audit_log writes must never propagate that failure to the caller.
        await local.Connection.CloseAsync();

        var thrown = await Record.ExceptionAsync(() => audit.LogAsync("alice", "export_definition_created", "id=42"));

        Assert.Null(thrown);
    }
}
