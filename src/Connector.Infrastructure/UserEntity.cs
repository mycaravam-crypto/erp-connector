using Microsoft.EntityFrameworkCore;

namespace Connector.Infrastructure;

/// <summary>The two roles a user can have. An <see cref="Admin"/> can always see and do everything; what a
/// <see cref="User"/> may see and do is the permission set an Admin grants that role (see <c>Permissions</c> and
/// <c>RolePermissionStore</c> in Connector.Api).</summary>
public static class UserRoles
{
    public const string Admin = "Admin";
    public const string User = "User";

    public static readonly IReadOnlyList<string> All = [Admin, User];

    /// <summary>The canonical spelling of <paramref name="role"/> (case-insensitive), or null if it isn't a role.</summary>
    public static string? Normalize(string? role) =>
        All.FirstOrDefault(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
}

/// <summary>An interactive login. Managed in the UI by Admins (Settings → Users); the table is seeded once from
/// <c>Auth:Users</c> (or the Development seed) while it is still empty.</summary>
public sealed class UserEntity
{
    public int Id { get; set; }

    /// <summary>Unique, compared case-insensitively (NOCASE collation), like the login has always been.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>BCrypt hash.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>One of <see cref="UserRoles"/>.</summary>
    public string Role { get; set; } = UserRoles.User;

    public string CreatedAt { get; set; } = string.Empty;
}

/// <summary>A user to create while the user table is still empty — from <c>Auth:Users</c> or the Development seed.</summary>
public sealed record SeedUser(string Username, string PasswordHash, string Role);

/// <summary>Lookup and seeding helpers over <see cref="ExportLogDbContext.Users"/>.</summary>
public static class UserStore
{
    /// <summary>The user named <paramref name="username"/> (case-insensitive), or null.</summary>
    public static Task<UserEntity?> FindUserAsync(
        this ExportLogDbContext db,
        string? username,
        CancellationToken ct = default
    ) =>
        string.IsNullOrWhiteSpace(username)
            ? Task.FromResult<UserEntity?>(null)
            : db.Users.FirstOrDefaultAsync(u => u.Username == username, ct);

    /// <summary>Inserts <paramref name="seed"/> when the user table is empty, and does nothing otherwise — so a
    /// user an Admin deleted in the UI doesn't come back on the next restart. Returns the number inserted.</summary>
    public static async Task<int> SeedUsersIfEmptyAsync(this ExportLogDbContext db, IEnumerable<SeedUser> seed)
    {
        if (await db.Users.AnyAsync())
            return 0;

        var now = DateTimeOffset.UtcNow.ToString("O");
        var users = seed.DistinctBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
            .Select(u => new UserEntity
            {
                Username = u.Username,
                PasswordHash = u.PasswordHash,
                Role = u.Role,
                CreatedAt = now,
            })
            .ToList();
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        return users.Count;
    }
}
