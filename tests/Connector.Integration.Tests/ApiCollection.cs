namespace Connector.Integration.Tests;

/// <summary>
/// Shares one <see cref="ApiFactory"/> app instance across every HTTP-layer test class (each opts in with
/// <c>[Collection(ApiCollection.Name)]</c> instead of <c>IClassFixture&lt;ApiFactory&gt;</c>). Two reasons
/// this has to be a collection fixture rather than a per-class one: booting the app is real work (EF
/// migrations, Data Protection key generation, three hosted services), and — more importantly —
/// <c>Program.cs</c> sets <c>Serilog.Log.Logger</c> on the shared static <see cref="Serilog.Log"/> class at
/// startup, so two <see cref="ApiFactory"/> hosts booting concurrently (which is exactly what xUnit's
/// default cross-class parallelism would do with one fixture per class) race on that global and fail
/// startup unpredictably. A collection fixture gives every class in it one shared instance and, per xUnit's
/// own rules, runs their tests sequentially with respect to each other — so only one host is ever starting
/// at a time. The cost: tests across every class in this collection share one app/DB, so — same as within
/// a single class already — use distinct usernames/identifiers per test rather than assuming a clean slate.
/// </summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Api";
}
