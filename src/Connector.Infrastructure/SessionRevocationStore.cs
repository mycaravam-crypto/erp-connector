namespace Connector.Infrastructure;

/// <summary>
/// Security-review finding SR-16: JWTs previously had no revocation mechanism at all — a leaked token, or
/// a user who wants to force out any other still-outstanding session of their own, had no effect until the
/// token's own expiry (default 8h) passed. Stores, per username, the earliest a JWT for that user may have
/// been issued to still be considered valid — every token carries its own issued-at claim (see
/// <c>AuthEndpoints</c>), and <c>Program.cs</c>'s <c>OnTokenValidated</c> handler rejects any token issued
/// before that cutover. Reuses the existing encrypted-at-rest <see cref="AppSettingEntity"/> key/value store
/// rather than a dedicated table — this is a handful of timestamps, not data that needs its own schema.
///
/// Deliberately self-service only for now (a user can revoke their own sessions, see
/// <c>POST /api/auth/revoke-my-sessions</c>): a genuinely admin-triggered "kick this other user out" action
/// needs a way to tell who's allowed to act on someone else's session, which doesn't exist yet — see SR-04
/// (RBAC) tracking issue #134.
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
