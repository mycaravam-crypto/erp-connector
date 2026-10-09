using Connector.Api.Authorization;
using Connector.Infrastructure;

namespace Connector.Api.Endpoints;

/// <summary>Admin-only Settings → Permissions: what the User role may see and do.</summary>
static class PermissionEndpoints
{
    internal static void MapPermissionEndpoints(this WebApplication app)
    {
        // The grantable permissions, grouped by menu item, and the User role's current set.
        app.MapGet(
                "/api/settings/permissions",
                async (RolePermissionStore store) =>
                    Results.Ok(
                        new RolePermissionsDto(
                            Permissions.Catalogue,
                            [.. (await store.GetAsync(UserRoles.User)).Order(StringComparer.Ordinal)]
                        )
                    )
            )
            .RequireAdmin();

        // Replaces the User role's permissions. Unknown keys are dropped and each permission adds its menu item's
        // "view" permission (Permissions.Normalize); the stored result is returned. Applies to the next request.
        app.MapPut(
                "/api/settings/permissions",
                async (
                    RolePermissionsRequest req,
                    RolePermissionStore store,
                    ExportLogDbContext db,
                    HttpContext httpContext,
                    AuditService audit
                ) =>
                {
                    if (req.UserPermissions is null)
                        return Results.BadRequest("userPermissions is required.");

                    var before = await store.GetAsync(UserRoles.User);
                    var after = await store.SaveAsync(db, UserRoles.User, req.UserPermissions);
                    var added = after.Except(before).Order(StringComparer.Ordinal).ToList();
                    var removed = before.Except(after).Order(StringComparer.Ordinal).ToList();
                    await audit.LogAsync(
                        httpContext.User.Identity!.Name!,
                        "permissions_updated",
                        $"role={UserRoles.User} added=[{string.Join(", ", added)}] removed=[{string.Join(", ", removed)}]"
                    );
                    return Results.Ok(
                        new RolePermissionsDto(Permissions.Catalogue, [.. after.Order(StringComparer.Ordinal)])
                    );
                }
            )
            .RequireAdmin();
    }
}
