namespace Connector.Infrastructure;

/// <summary>
/// JWT revocation: stores, per username, the earliest a JWT for that user may have been issued to still be
/// considered valid. Every token carries its own issued-at claim (see <c>AuthEndpoints</c>), and
/// <c>Program.cs</c>'s <c>OnTokenValidated</c> handler rejects any token issued before that cutover. Backed by
/// the encrypted-at-rest <see cref="AppSettingEntity"/> key/value store rather than a dedicated table.
///
/// Self-service only: a user can revoke their own sessions (<c>POST /api/auth/revoke-my-sessions</c>). There
/// is no admin action to revoke another user's sessions, since there is no role model to authorize it.
/// </summary>
public static class SessionRevocationStore
{
    private static string KeyFor(string username) => $"session_revoked_before:{username.ToLowerInvariant()}";

    /// <summary>Invalidates every JWT issued for <paramref name="username"/> before now — the next request
    /// bearing any of those tokens fails authentication, forcing a fresh login.</summary>
    public static Task RevokeAllSessionsAsync(this ExportLogDbContext db, string username) =>
        db.SetSettingAsync(KeyFor(username), DateTimeOffset.UtcNow);

    /// <summary>Returns the revocation cutover for <paramref name="username"/>, or null if that user has
    /// never revoked their sessions (every token stays valid until its own expiry).</summary>
    public static Task<DateTimeOffset?> GetRevokedBeforeAsync(this ExportLogDbContext db, string username) =>
        db.GetSettingAsync<DateTimeOffset?>(KeyFor(username));
}
