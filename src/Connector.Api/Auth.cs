namespace Connector.Api;

public sealed class AuthOptions
{
    public string JwtSecret { get; set; } = string.Empty;
    public int JwtExpiryHours { get; set; } = 8;

    /// <summary>Production user list. In Development, <see cref="DevAuthSeed"/> overrides this.</summary>
    public List<AuthUser> Users { get; set; } = [];
}

public sealed class AuthUser
{
    public string Username { get; set; } = string.Empty;

    /// <summary>BCrypt hash. Generate via POST /api/auth/hash (Development only).</summary>
    public string PasswordHash { get; set; } = string.Empty;
}
