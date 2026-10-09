using Connector.Infrastructure;

namespace Connector.Api.Authorization;

/// <summary>
/// What each non-Admin role may do, stored as one <see cref="SettingsKeys.RolePermissions"/> row
/// (<c>{ "User": ["exportJobs.view", ...] }</c>) and cached in memory, since it is checked on nearly every request.
/// The cache is dropped on every save, so an Admin's change applies to the next request rather than after the
/// affected users' tokens expire. Singleton: reads go through a fresh scope's <see cref="ExportLogDbContext"/>.
/// </summary>
public sealed class RolePermissionStore(IServiceScopeFactory scopeFactory)
{
    private volatile IReadOnlyDictionary<string, IReadOnlySet<string>>? _cache;

    /// <summary>The effective permissions of <paramref name="role"/>: every permission for an Admin, the saved set
    /// (or <see cref="Permissions.DefaultUserPermissions"/> before the first save) for a User, nothing otherwise.</summary>
    public async Task<IReadOnlySet<string>> GetAsync(string? role)
    {
        if (role == UserRoles.Admin)
            return Permissions.All;
        if (role is null)
            return new HashSet<string>();

        var cache = _cache ?? await LoadAsync();
        return cache.TryGetValue(role, out var permissions) ? permissions : new HashSet<string>();
    }

    public async Task<bool> HasAsync(string? role, string permission) => (await GetAsync(role)).Contains(permission);

    /// <summary>Replaces <paramref name="role"/>'s permissions with the normalized <paramref name="granted"/> set and
    /// returns it.</summary>
    public async Task<IReadOnlySet<string>> SaveAsync(ExportLogDbContext db, string role, IEnumerable<string> granted)
    {
        var normalized = Permissions.Normalize(granted);
        var stored = await db.GetSettingAsync<Dictionary<string, List<string>>>(SettingsKeys.RolePermissions) ?? [];
        stored[role] = [.. normalized.Order(StringComparer.Ordinal)];
        await db.SetSettingAsync(SettingsKeys.RolePermissions, stored);
        _cache = null;
        return normalized;
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> LoadAsync()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExportLogDbContext>();
        var stored = await db.GetSettingAsync<Dictionary<string, List<string>>>(SettingsKeys.RolePermissions) ?? [];

        var cache = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [UserRoles.User] = stored.TryGetValue(UserRoles.User, out var user)
                ? Permissions.Normalize(user)
                : Permissions.DefaultUserPermissions,
        };
        _cache = cache;
        return cache;
    }
}
