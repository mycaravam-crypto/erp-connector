using Connector.Infrastructure;

namespace Connector.Api;

/// <summary>
/// Provides hard-coded dev users and an API key so Development mode works out of the box without
/// pre-computing BCrypt hashes or a SHA-256 key hash. Do not call in Production.
/// </summary>
internal static class DevAuthSeed
{
    /// <summary>Raw dev-only API key value — send as the <c>X-Api-Key</c> header. Logged at startup so it
    /// doesn't need to be re-derived from <see cref="CreateApiKeys"/>'s hash.</summary>
    // Sonar S6418 ("hard-coded secret") false-positives on the "Key"-suffixed name — this is a published,
    // Development-only fixture value (same category as the hard-coded alice123/bob123 dev passwords
    // above), never a real credential.
#pragma warning disable S6418
    internal const string DevApiKey = "dev-local-api-key";
#pragma warning restore S6418

    /// <summary>alice / alice123 and bob / bob123 (Admins, so the four-eyes flows work out of the box) and
    /// carol / carol123 (User, for trying out permissions), with BCrypt work-factor 4 (fast for dev).</summary>
    internal static List<SeedUser> CreateUsers() =>
        [
            new("alice", BCrypt.Net.BCrypt.HashPassword("alice123", workFactor: 4), UserRoles.Admin),
            new("bob", BCrypt.Net.BCrypt.HashPassword("bob123", workFactor: 4), UserRoles.Admin),
            new("carol", BCrypt.Net.BCrypt.HashPassword("carol123", workFactor: 4), UserRoles.User),
        ];

    internal static List<ApiKeyOptions> CreateApiKeys() =>
        [new ApiKeyOptions { Name = "dev-api-key", KeyHash = ApiKeyStore.Hash(DevApiKey) }];
}
