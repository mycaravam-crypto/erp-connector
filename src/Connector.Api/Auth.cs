using Connector.Infrastructure;

namespace Connector.Api;

public sealed class AuthOptions
{
    public string JwtSecret { get; set; } = string.Empty;
    public int JwtExpiryHours { get; set; } = 8;

    /// <summary>Production users that seed the user table while it is still empty; after that, users are managed
    /// in the UI (Settings → Users). In Development, <see cref="DevAuthSeed"/> is used instead.</summary>
    public List<AuthUser> Users { get; set; } = [];

    /// <summary>Production API key list, for machine-to-machine callers (e.g. an external system
    /// triggering a saved export preset via <c>POST /api/pipeline/run/{name}</c>) that shouldn't go
    /// through the interactive JWT login flow. In Development, <see cref="DevAuthSeed"/> overrides this.</summary>
    public List<ApiKeyOptions> ApiKeys { get; set; } = [];
}

public sealed class AuthUser
{
    public string Username { get; set; } = string.Empty;

    /// <summary>BCrypt hash. Generate via POST /api/auth/hash (Development only).</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary><c>Admin</c> or <c>User</c>. Empty means Admin, so a user list written before roles existed keeps
    /// every user's full access.</summary>
    public string? Role { get; set; }

    /// <summary>Converts the configured users into <see cref="SeedUser"/>s. An unknown role is a startup error
    /// rather than a silent downgrade or upgrade.</summary>
    internal static List<SeedUser> ToSeed(IEnumerable<AuthUser> users, ILogger logger) =>
        users
            .Select(u =>
            {
                if (string.IsNullOrWhiteSpace(u.Role))
                {
                    logger.LogWarning("Auth:Users entry '{Username}' has no Role; it is treated as Admin.", u.Username);
                    return new SeedUser(u.Username, u.PasswordHash, UserRoles.Admin);
                }
                var role =
                    UserRoles.Normalize(u.Role)
                    ?? throw new InvalidOperationException(
                        $"Auth:Users entry '{u.Username}' has an unknown Role '{u.Role}' (expected Admin or User)."
                    );
                return new SeedUser(u.Username, u.PasswordHash, role);
            })
            .ToList();
}

public sealed class ApiKeyOptions
{
    /// <summary>Identity this key authenticates as — the audit log's Username and the request's
    /// <see cref="System.Security.Claims.ClaimTypes.Name"/> claim.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Hex-encoded SHA-256 of the raw key (see <see cref="Connector.Infrastructure.ApiKeyStore.Hash"/>).
    /// Generate with: <c>printf '%s' '&lt;raw key&gt;' | sha256sum</c>. Never store the raw key itself.</summary>
    public string KeyHash { get; set; } = string.Empty;
}
