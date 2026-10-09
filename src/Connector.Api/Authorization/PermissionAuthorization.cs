using System.Security.Claims;
using Connector.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Connector.Api.Authorization;

/// <summary>Met when the user holds at least one of <see cref="AnyOf"/> (Admins hold all of them).</summary>
public sealed class PermissionRequirement(IReadOnlyList<string> anyOf) : IAuthorizationRequirement
{
    public IReadOnlyList<string> AnyOf { get; } = anyOf;
}

/// <summary>Checks a <see cref="PermissionRequirement"/> against the role claim that <c>Program.cs</c>'s
/// <c>OnTokenValidated</c> loads from the user table on every request.</summary>
public sealed class PermissionAuthorizationHandler(RolePermissionStore store)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement
    )
    {
        var granted = await store.GetAsync(context.User.FindFirst(ClaimTypes.Role)?.Value);
        if (requirement.AnyOf.Any(granted.Contains))
            context.Succeed(requirement);
    }
}

/// <summary>
/// Builds a policy for every <c>perm:a|b</c> name on demand (see <see cref="PermissionEndpointExtensions"/>), so the
/// endpoints don't need one registered policy per permission or combination of permissions.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    internal const string Prefix = "perm:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal))
            return await base.GetPolicyAsync(policyName);

        var anyOf = policyName[Prefix.Length..].Split('|');
        var unknown = anyOf.FirstOrDefault(p => !Permissions.All.Contains(p));
        if (unknown is not null)
            throw new InvalidOperationException($"Unknown permission '{unknown}' in policy '{policyName}'.");

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(anyOf))
            .Build();
    }
}

public static class PermissionEndpointExtensions
{
    /// <summary>Policy for the admin-only areas (Connect, Users, Permissions) that can't be granted to other roles.</summary>
    public const string AdminPolicy = "admin";

    /// <summary>Requires at least one of <paramref name="anyOf"/>; replaces a plain <c>RequireAuthorization()</c>.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, params string[] anyOf)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(PermissionPolicyProvider.Prefix + string.Join('|', anyOf));

    /// <summary>Requires the Admin role.</summary>
    public static TBuilder RequireAdmin<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder => builder.RequireAuthorization(AdminPolicy);

    /// <summary>Registers the permission handler, the on-demand policy provider and <see cref="AdminPolicy"/>.</summary>
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder().AddPolicy(AdminPolicy, p => p.RequireRole(UserRoles.Admin));
        services.AddSingleton<RolePermissionStore>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        return services;
    }
}
