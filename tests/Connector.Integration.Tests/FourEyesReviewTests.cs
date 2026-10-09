using Connector.Api;
using Connector.Api.Authorization;
using Connector.Api.Endpoints;
using Connector.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Connector.Integration.Tests;

/// <summary>
/// Coverage for <see cref="FourEyesReview.ValidateApproverAsync"/>: the approver must be independently
/// authenticated, so no single account can "approve" its own release by typing a colleague's name.
/// Verifies the approver's own password is required and checked against their real hash, and that the approver
/// holds the release permission themselves.
/// </summary>
public sealed class FourEyesReviewTests
{
    // alice/alice123 and bob/bob123 (Admins), carol/carol123 (User) — see DevAuthSeed's doc comment.
    private static async Task<(LocalDb Local, RolePermissionStore Permissions)> NewAsync()
    {
        var local = await LocalDb.NewAsync();
        await local.Db.SeedUsersIfEmptyAsync(DevAuthSeed.CreateUsers());
        // Registered as an instance, so the store's scopes resolve this test's context and never dispose it.
        var services = new ServiceCollection().AddSingleton(local.Db).BuildServiceProvider();
        return (local, new RolePermissionStore(services.GetRequiredService<IServiceScopeFactory>()));
    }

    private static Task<string?> ValidateAsync(
        (LocalDb Local, RolePermissionStore Permissions) ctx,
        string operatorName,
        string? approver,
        string? password
    ) =>
        FourEyesReview.ValidateApproverAsync(
            operatorName,
            approver,
            password,
            Permissions.ManagedExportRelease,
            ctx.Local.Db,
            ctx.Permissions
        );

    [Fact]
    public async Task SameOperatorAndApprover_Rejected()
    {
        var ctx = await NewAsync();
        await using var _ = ctx.Local;

        var error = await ValidateAsync(ctx, "alice", "alice", "alice123");

        Assert.NotNull(error);
        Assert.Contains("different users", error);
    }

    [Fact]
    public async Task UnknownApprover_Rejected()
    {
        var ctx = await NewAsync();
        await using var _ = ctx.Local;

        var error = await ValidateAsync(ctx, "alice", "mallory", "whatever");

        Assert.NotNull(error);
        Assert.Contains("Unknown approver", error);
    }

    [Fact]
    public async Task MissingApproverPassword_Rejected()
    {
        var ctx = await NewAsync();
        await using var _ = ctx.Local;

        var error = await ValidateAsync(ctx, "alice", "bob", null);

        Assert.NotNull(error);
        Assert.Contains("password", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WrongApproverPassword_Rejected()
    {
        var ctx = await NewAsync();
        await using var _ = ctx.Local;

        var error = await ValidateAsync(ctx, "alice", "bob", "not-bobs-password");

        Assert.NotNull(error);
        Assert.Contains("password", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OperatorCannotSelfApprove_ByGuessingOrKnowingAnotherPassword_StillRequiresThatPersonsOwnPassword()
    {
        var ctx = await NewAsync();
        await using var _ = ctx.Local;

        // The operator (alice) cannot complete a release just by knowing bob's username — she'd need bob's
        // actual password too, which she authored this test knowing but wouldn't in reality.
        var error = await ValidateAsync(ctx, "alice", "bob", "alice123");

        Assert.NotNull(error);
    }

    [Fact]
    public async Task CorrectApproverPassword_Accepted()
    {
        var ctx = await NewAsync();
        await using var _ = ctx.Local;

        Assert.Null(await ValidateAsync(ctx, "alice", "bob", "bob123"));
        // Usernames are case-insensitive, like the login.
        Assert.Null(await ValidateAsync(ctx, "alice", "BOB", "bob123"));
    }

    [Fact]
    public async Task ApproverWithoutReleasePermission_Rejected()
    {
        var ctx = await NewAsync();
        await using var _ = ctx.Local;

        // carol is a User, and the User role can't release by default.
        var error = await ValidateAsync(ctx, "alice", "carol", "carol123");

        Assert.NotNull(error);
        Assert.Contains("not allowed", error);
    }

    [Fact]
    public async Task ApproverGrantedReleasePermission_Accepted()
    {
        var ctx = await NewAsync();
        await using var _ = ctx.Local;
        await ctx.Permissions.SaveAsync(ctx.Local.Db, UserRoles.User, [Permissions.ManagedExportRelease]);

        Assert.Null(await ValidateAsync(ctx, "alice", "carol", "carol123"));
    }
}
