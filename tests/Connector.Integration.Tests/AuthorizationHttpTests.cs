using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Connector.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Connector.Integration.Tests;

/// <summary>
/// HTTP-layer coverage for roles and permissions: every endpoint declares what it needs, a User only gets what
/// the User role was granted, Admin-only areas stay Admin-only, and user management can't lock the Admins out.
/// "carol" is the Development seed's User; every test that changes the User role's permissions restores the
/// defaults afterwards, since the app (and its database) is shared by the whole <see cref="ApiCollection"/>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuthorizationHttpTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = ApiAuth.Json;

    // Endpoints any signed-in user may call, whatever their permissions.
    private static readonly HashSet<string> AnyAuthenticated =
    [
        "GET /api/auth/me",
        "POST /api/auth/change-password",
        "POST /api/auth/revoke-my-sessions",
        "GET /api/connection/status",
    ];

    // Endpoints reachable without a user session: sign-in and what the login page shows, plus the API-key-only
    // preset trigger (its own scheme; a user's JWT doesn't authenticate there).
    private static readonly HashSet<string> NoUserSession =
    [
        "POST /api/auth/login",
        "POST /api/auth/hash",
        "GET /api/health",
        "GET /api/version",
        "GET /api/branding",
        "POST /api/pipeline/run/{name}",
    ];

    [Fact]
    public void EveryApiEndpoint_DeclaresAPermissionOrIsExplicitlyListed()
    {
        var endpoints = factory
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true);

        var missing = new List<string>();
        foreach (var endpoint in endpoints)
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"];
            foreach (var method in methods)
            {
                var name = $"{method} {endpoint.RoutePattern.RawText}";
                if (AnyAuthenticated.Contains(name) || NoUserSession.Contains(name))
                    continue;
                var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy);
                if (
                    !policies.Any(p =>
                        p == PermissionEndpointExtensions.AdminPolicy
                        || p?.StartsWith(PermissionPolicyProvider.Prefix, StringComparison.Ordinal) == true
                    )
                )
                    missing.Add(name);
            }
        }

        Assert.Empty(missing);
    }

    [Fact]
    public async Task Me_ReturnsRoleAndEffectivePermissions()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var adminMe = await admin.GetFromJsonAsync<JsonElement>("/api/auth/me", Json);
        Assert.Equal("Admin", adminMe.GetProperty("role").GetString());
        Assert.Equal(Permissions.All.Count, adminMe.GetProperty("permissions").GetArrayLength());

        using var user = await CarolAsync();
        var userMe = await user.GetFromJsonAsync<JsonElement>("/api/auth/me", Json);
        Assert.Equal("User", userMe.GetProperty("role").GetString());
        Assert.Equal(
            Permissions.DefaultUserPermissions.Order(StringComparer.Ordinal),
            userMe.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!)
        );
    }

    [Theory]
    [InlineData("/api/connection")]
    [InlineData("/api/users")]
    [InlineData("/api/settings/permissions")]
    [InlineData("/api/audit")]
    [InlineData("/api/settings/scheduler")]
    public async Task User_CannotReachAdminOnlyOrUngrantedAreas(string path)
    {
        using var user = await CarolAsync();

        var response = await user.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task User_WithDefaults_CanViewButNotChange()
    {
        using var user = await CarolAsync();

        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/export-definitions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/connection/status")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await user.PostAsJsonAsync("/api/export-definitions", new { }, Json)).StatusCode
        );
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync("/api/import-definitions/1")).StatusCode);
    }

    [Fact]
    public async Task GrantingAPermission_AppliesToTheNextRequest_AndImpliesView()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        using var user = await CarolAsync();
        try
        {
            var saved = await admin.PutAsJsonAsync(
                "/api/settings/permissions",
                new { userPermissions = new[] { Permissions.SettingsScheduler, Permissions.AuditView, "made.up" } },
                Json
            );
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            var stored = (await saved.Content.ReadFromJsonAsync<JsonElement>(Json))
                .GetProperty("userPermissions")
                .EnumerateArray()
                .Select(p => p.GetString())
                .ToList();
            Assert.Equal([Permissions.AuditView, Permissions.SettingsScheduler], stored);

            Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/settings/scheduler")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/audit")).StatusCode);
            // Everything not granted is gone, the former defaults included.
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/export-definitions")).StatusCode);

            // An action permission brings its menu item's view permission along.
            await admin.PutAsJsonAsync(
                "/api/settings/permissions",
                new { userPermissions = new[] { Permissions.ExportJobsCreate } },
                Json
            );
            Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/export-definitions")).StatusCode);
            Assert.NotEqual(
                HttpStatusCode.Forbidden,
                (await user.PostAsJsonAsync("/api/export-definitions", new { }, Json)).StatusCode
            );
        }
        finally
        {
            await RestoreDefaultsAsync(admin);
        }
    }

    [Fact]
    public async Task User_CannotChangePermissions()
    {
        using var user = await CarolAsync();

        var response = await user.PutAsJsonAsync(
            "/api/settings/permissions",
            new { userPermissions = Permissions.All },
            Json
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Users_CreateUpdateDelete_RoundTrip()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();

        var created = await admin.PostAsJsonAsync(
            "/api/users",
            new
            {
                username = "dave",
                password = "dave-password-1",
                role = "user",
            },
            Json
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(
            "User",
            (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("role").GetString()
        );

        var duplicate = await admin.PostAsJsonAsync(
            "/api/users",
            new
            {
                username = "DAVE",
                password = "dave-password-1",
                role = "User",
            },
            Json
        );
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var tooShort = await admin.PostAsJsonAsync(
            "/api/users",
            new
            {
                username = "frank",
                password = "short",
                role = "User",
            },
            Json
        );
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        var promoted = await admin.PutAsJsonAsync("/api/users/dave", new { role = "Admin" }, Json);
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        var users = await admin.GetFromJsonAsync<JsonElement>("/api/users", Json);
        Assert.Contains(
            users.EnumerateArray(),
            u => u.GetProperty("username").GetString() == "dave" && u.GetProperty("role").GetString() == "Admin"
        );

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync("/api/users/dave")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync("/api/users/dave")).StatusCode);
    }

    [Fact]
    public async Task Admin_CannotDemoteOrDeleteThemselves()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();

        Assert.Equal(
            HttpStatusCode.Conflict,
            (await admin.PutAsJsonAsync("/api/users/alice", new { role = "User" }, Json)).StatusCode
        );
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync("/api/users/alice")).StatusCode);
    }

    [Fact]
    public async Task DeletedUser_TokenStopsWorking_AndRoleChangeAppliesAtOnce()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        await admin.PostAsJsonAsync(
            "/api/users",
            new
            {
                username = "erin",
                password = "erin-password-1",
                role = "User",
            },
            Json
        );
        using var erin = factory.CreateClient();
        var login = await erin.PostAsJsonAsync(
            "/api/auth/login",
            new { username = "erin", password = "erin-password-1" },
            Json
        );
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("token").GetString();
        erin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.Equal(HttpStatusCode.Forbidden, (await erin.GetAsync("/api/users")).StatusCode);

        // Promoted while signed in: the same token is an Admin's on the very next request.
        await admin.PutAsJsonAsync("/api/users/erin", new { role = "Admin" }, Json);
        Assert.Equal(HttpStatusCode.OK, (await erin.GetAsync("/api/users")).StatusCode);

        await admin.DeleteAsync("/api/users/erin");
        Assert.Equal(HttpStatusCode.Unauthorized, (await erin.GetAsync("/api/auth/me")).StatusCode);
    }

    private Task<HttpClient> CarolAsync() => factory.CreateAuthenticatedClientAsync("carol", "carol123");

    private static async Task RestoreDefaultsAsync(HttpClient admin) =>
        (
            await admin.PutAsJsonAsync(
                "/api/settings/permissions",
                new { userPermissions = Permissions.DefaultUserPermissions },
                Json
            )
        ).EnsureSuccessStatusCode();
}
