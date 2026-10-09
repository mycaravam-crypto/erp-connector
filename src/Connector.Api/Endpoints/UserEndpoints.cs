using System.Text.RegularExpressions;
using Connector.Api.Authorization;
using Connector.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Connector.Api.Endpoints;

/// <summary>
/// Admin-only user management (Settings → Users): list, create, change role or reset password, delete. The last
/// Admin can be neither demoted nor deleted, and an Admin can't change their own role or delete themselves, so
/// the UI can never lock every Admin out. Every change is audited.
/// </summary>
static class UserEndpoints
{
    internal const int MinPasswordLength = 10;
    private const int MaxPasswordLength = 72; // BCrypt ignores everything past 72 bytes.
    private const int PasswordWorkFactor = 11;

    private static readonly Regex UsernameRegex = new(
        "^[A-Za-z0-9._@-]{1,64}$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1)
    );

    internal static string? ValidatePassword(string? password)
    {
        if (password is null || password.Length < MinPasswordLength)
            return $"The password must be at least {MinPasswordLength} characters long.";
        if (System.Text.Encoding.UTF8.GetByteCount(password) > MaxPasswordLength)
            return $"The password must be at most {MaxPasswordLength} bytes long.";
        return null;
    }

    internal static string HashPassword(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, workFactor: PasswordWorkFactor);

    internal static void MapUserEndpoints(this WebApplication app)
    {
        // Every user and their role, in username order. Password hashes never leave the server.
        app.MapGet(
                "/api/users",
                async (ExportLogDbContext db, CancellationToken ct) =>
                    (await db.Users.AsNoTracking().ToListAsync(ct))
                        .OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
                        .Select(ToDto)
                        .ToList()
            )
            .RequireAdmin();

        // Creates a user. 409 when the username (compared case-insensitively) is taken.
        app.MapPost(
                "/api/users",
                async (CreateUserRequest req, ExportLogDbContext db, HttpContext httpContext, AuditService audit) =>
                {
                    var username = req.Username?.Trim();
                    if (username is null || !UsernameRegex.IsMatch(username))
                        return Results.BadRequest(
                            "The username must be 1–64 characters: letters, digits, '.', '_', '@' or '-'."
                        );
                    var role = UserRoles.Normalize(req.Role);
                    if (role is null)
                        return Results.BadRequest("The role must be Admin or User.");
                    var passwordError = ValidatePassword(req.Password);
                    if (passwordError is not null)
                        return Results.BadRequest(passwordError);
                    if (await db.FindUserAsync(username) is not null)
                        return Results.Conflict($"A user named '{username}' already exists.");

                    var user = new UserEntity
                    {
                        Username = username,
                        PasswordHash = HashPassword(req.Password!),
                        Role = role,
                        CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                    };
                    db.Users.Add(user);
                    await db.SaveChangesAsync();
                    await audit.LogAsync(CurrentUser(httpContext), "user_created", $"user={username} role={role}");
                    return Results.Created($"/api/users/{Uri.EscapeDataString(username)}", ToDto(user));
                }
            )
            .RequireAdmin();

        // Changes a user's role and/or resets their password. A password reset signs that user out everywhere.
        app.MapPut(
                "/api/users/{username}",
                async (
                    string username,
                    UpdateUserRequest req,
                    ExportLogDbContext db,
                    HttpContext httpContext,
                    AuditService audit
                ) =>
                {
                    var user = await db.FindUserAsync(username);
                    if (user is null)
                        return Results.NotFound();

                    var changes = new List<string>();
                    if (req.Role is not null)
                    {
                        var role = UserRoles.Normalize(req.Role);
                        if (role is null)
                            return Results.BadRequest("The role must be Admin or User.");
                        if (role != user.Role)
                        {
                            if (IsSelf(httpContext, user))
                                return Results.Conflict("You can't change your own role.");
                            if (user.Role == UserRoles.Admin && await IsLastAdminAsync(db))
                                return Results.Conflict("The last Admin can't be made a User.");
                            changes.Add($"role={user.Role}->{role}");
                            user.Role = role;
                        }
                    }

                    if (req.Password is not null)
                    {
                        var passwordError = ValidatePassword(req.Password);
                        if (passwordError is not null)
                            return Results.BadRequest(passwordError);
                        user.PasswordHash = HashPassword(req.Password);
                        changes.Add("password reset");
                    }

                    if (changes.Count == 0)
                        return Results.Ok(ToDto(user));

                    await db.SaveChangesAsync();
                    if (req.Password is not null)
                        await db.RevokeAllSessionsAsync(user.Username);
                    await audit.LogAsync(
                        CurrentUser(httpContext),
                        "user_updated",
                        $"user={user.Username} {string.Join(", ", changes)}"
                    );
                    return Results.Ok(ToDto(user));
                }
            )
            .RequireAdmin();

        // Deletes a user; their open sessions stop working on their next request. Their audit entries stay.
        app.MapDelete(
                "/api/users/{username}",
                async (string username, ExportLogDbContext db, HttpContext httpContext, AuditService audit) =>
                {
                    var user = await db.FindUserAsync(username);
                    if (user is null)
                        return Results.NotFound();
                    if (IsSelf(httpContext, user))
                        return Results.Conflict("You can't delete your own account.");
                    if (user.Role == UserRoles.Admin && await IsLastAdminAsync(db))
                        return Results.Conflict("The last Admin can't be deleted.");

                    db.Users.Remove(user);
                    await db.SaveChangesAsync();
                    // Also covers a later user created under the same name: this one's old tokens stay invalid.
                    await db.RevokeAllSessionsAsync(user.Username);
                    await audit.LogAsync(CurrentUser(httpContext), "user_deleted", $"user={user.Username}");
                    return Results.NoContent();
                }
            )
            .RequireAdmin();
    }

    private static UserDto ToDto(UserEntity u) => new(u.Username, u.Role, u.CreatedAt);

    private static string CurrentUser(HttpContext httpContext) => httpContext.User.Identity!.Name!;

    private static bool IsSelf(HttpContext httpContext, UserEntity user) =>
        string.Equals(CurrentUser(httpContext), user.Username, StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> IsLastAdminAsync(ExportLogDbContext db) =>
        await db.Users.CountAsync(u => u.Role == UserRoles.Admin) <= 1;
}
