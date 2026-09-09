using Connector.Infrastructure;

namespace Connector.Integration.Tests;

/// <summary>
/// Coverage for <see cref="SessionRevocationStore"/> (security-review finding SR-16: a JWT previously had
/// no way to be invalidated before its own expiry). Runs entirely against the in-memory Sqlite
/// <see cref="LocalDb"/> fixture — no Postgres testdb required.
/// </summary>
public sealed class SessionRevocationStoreTests
{
    [Fact]
    public async Task GetRevokedBeforeAsync_NeverRevoked_ReturnsNull()
    {
        await using var local = await LocalDb.NewAsync();

        Assert.Null(await local.Db.GetRevokedBeforeAsync("alice"));
    }

    [Fact]
    public async Task RevokeAllSessionsAsync_ThenGetRevokedBefore_ReturnsRecentCutover()
    {
        await using var local = await LocalDb.NewAsync();
        var before = DateTimeOffset.UtcNow;

        await local.Db.RevokeAllSessionsAsync("alice");
        var revokedBefore = await local.Db.GetRevokedBeforeAsync("alice");

        Assert.NotNull(revokedBefore);
        Assert.InRange(revokedBefore.Value, before, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task RevokeAllSessionsAsync_IsCaseInsensitiveAndScopedToOneUser()
    {
        await using var local = await LocalDb.NewAsync();

        await local.Db.RevokeAllSessionsAsync("Alice");

        Assert.NotNull(await local.Db.GetRevokedBeforeAsync("alice"));
        Assert.NotNull(await local.Db.GetRevokedBeforeAsync("ALICE"));
        // A different user's sessions are untouched by someone else's revocation.
        Assert.Null(await local.Db.GetRevokedBeforeAsync("bob"));
    }
}
