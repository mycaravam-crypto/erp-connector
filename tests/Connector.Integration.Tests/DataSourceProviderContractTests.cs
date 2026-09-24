using Connector.Core.DataSources;
using Connector.Infrastructure.DataSources.MariaDb;
using Connector.Infrastructure.DataSources.PostgreSql;

namespace Connector.Integration.Tests;

/// <summary>
/// The contract every <see cref="IDataSourceProvider"/> must meet, run unchanged against each
/// provider by a concrete subclass. Every provider serves the same <c>export_order</c>/<c>export_customer</c> rows
/// (testdb/init.sql, testdb/mariadb-init.sql, <see cref="FakeServiceNow.WithExportFixture"/>). Where providers
/// legitimately differ, the test asserts the behavior its <see cref="DataSourceCapabilities"/> declares — both
/// ways, never a silent skip per provider type. A subclass whose backing fixture isn't running turns every test
/// into a no-op (<see cref="IsAvailableAsync"/>), the same convention as every other DB-backed test here.
/// </summary>
public abstract class DataSourceProviderContractTests
{
    protected abstract IDataSourceProvider CreateProvider();

    protected abstract DataSourceConfig Config { get; }

    protected virtual Task<bool> IsAvailableAsync() => Task.FromResult(true);

    private const string WrongPassword = "contract-wrong-pw-must-not-echo";

    private Task<QueryResult> RunAsync(SourceQuery query, CancellationToken ct = default) =>
        CreateProvider().ExecuteAsync(Config, query, ct);

    private static QueryCondition Where(string column, QueryOperator op, object? value = null) =>
        new()
        {
            Column = column,
            Operator = op,
            Value = value,
        };

    private static SourceQuery Orders(params string[] columns) =>
        new() { RootTable = "export_order", Columns = columns.Select(c => new QueryColumn { Column = c }).ToList() };

    private static List<string?> Ids(QueryResult result) =>
        result.ToDictionaries().Select(r => r["id"]).Order(StringComparer.Ordinal).ToList();

    [Fact]
    public async Task TestConnection_Succeeds()
    {
        if (!await IsAvailableAsync())
            return;

        var result = await CreateProvider().TestConnectionAsync(Config, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.Schema);
    }

    [Fact]
    public async Task Schema_CanBeRead()
    {
        if (!await IsAvailableAsync())
            return;

        var schema = await CreateProvider().ReadSchemaAsync(Config, CancellationToken.None);

        var order = Assert.Single(schema.Tables, t => t.Name == "export_order");
        Assert.Subset(
            order.Columns.Select(c => c.Name).ToHashSet(),
            new HashSet<string> { "id", "customer_id", "note" }
        );
        Assert.Contains(order.Columns, c => c.PrimaryKey);
    }

    [Fact]
    public async Task RelationMetadata_Exists()
    {
        if (!await IsAvailableAsync())
            return;

        var schema = await CreateProvider().ReadSchemaAsync(Config, CancellationToken.None);

        var customerId = schema
            .Tables.Single(t => t.Name == "export_order")
            .Columns.Single(c => c.Name == "customer_id");
        Assert.Equal("export_customer", customerId.ForeignKeyTable);
        Assert.Contains(
            schema.Tables.Single(t => t.Name == "export_customer").Columns,
            c => c.Name == customerId.ForeignKeyColumn
        );
    }

    [Fact]
    public async Task SimpleTable_CanBeQueried()
    {
        if (!await IsAvailableAsync())
            return;

        var result = await RunAsync(new SourceQuery { RootTable = "export_order" });

        Assert.Equal(["100", "101", "102"], Ids(result));
    }

    [Fact]
    public async Task SelectedFields_CanBeQueried()
    {
        if (!await IsAvailableAsync())
            return;

        var result = await RunAsync(Orders("id", "note"));

        Assert.Equal(["id", "note"], result.Columns.Select(c => c.Name));
        Assert.All(result.Rows, r => Assert.Equal(2, r.Values.Count));
    }

    [Fact]
    public async Task Filter_Works()
    {
        if (!await IsAvailableAsync())
            return;

        Assert.Equal(
            ["100"],
            Ids(await RunAsync(Orders("id") with { Conditions = [Where("note", QueryOperator.Equal, "rush")] }))
        );
        Assert.Equal(
            ["101", "102"],
            Ids(await RunAsync(Orders("id") with { Conditions = [Where("id", QueryOperator.GreaterThan, 100)] }))
        );
        Assert.Equal(
            ["100", "102"],
            Ids(
                await RunAsync(
                    Orders("id") with
                    {
                        Conditions =
                        [
                            new QueryCondition
                            {
                                Column = "id",
                                Operator = QueryOperator.In,
                                Values = [100, 102],
                            },
                        ],
                    }
                )
            )
        );
    }

    [Fact]
    public async Task NullHandling_Works()
    {
        if (!await IsAvailableAsync())
            return;

        var nulls = await RunAsync(Orders("id", "note") with { Conditions = [Where("note", QueryOperator.IsNull)] });
        var notNull = await RunAsync(Orders("id") with { Conditions = [Where("note", QueryOperator.IsNotNull)] });

        Assert.Equal(["101", "102"], Ids(nulls));
        Assert.All(nulls.ToDictionaries(), r => Assert.Null(r["note"]));
        Assert.Equal(["100"], Ids(notNull));
    }

    [Fact]
    public async Task Limit_Works()
    {
        if (!await IsAvailableAsync())
            return;

        Assert.Equal(2, (await RunAsync(Orders("id") with { Limit = 2 })).Rows.Count);
        Assert.Empty((await RunAsync(Orders("id") with { Limit = 0 })).Rows);
    }

    [Fact]
    public async Task Join_OverRelationMetadata_Works()
    {
        if (!await IsAvailableAsync())
            return;

        var fk = await CustomerForeignKeyAsync();
        var result = await RunAsync(
            Orders("id") with
            {
                Columns =
                [
                    new QueryColumn { Column = "id" },
                    new QueryColumn
                    {
                        Table = "export_customer",
                        Column = "name",
                        Alias = "customer",
                    },
                ],
                Joins =
                [
                    new QueryJoin
                    {
                        Table = "export_customer",
                        Column = fk,
                        ParentColumn = "customer_id",
                        Type = QueryJoinType.Left,
                    },
                ],
            }
        );

        var customers = result.ToDictionaries().ToDictionary(r => r["id"]!, r => r["customer"]);
        Assert.Equal("Acme", customers["100"]);
        Assert.Equal("Acme", customers["101"]);
        Assert.Null(customers["102"]);
    }

    [Fact]
    public async Task UnknownTable_FailsExplicitly()
    {
        if (!await IsAvailableAsync())
            return;

        await Assert.ThrowsAsync<InvalidSourceQueryException>(() =>
            RunAsync(new SourceQuery { RootTable = "no_such_table" })
        );
    }

    [Fact]
    public async Task UnknownColumn_FailsExplicitly()
    {
        if (!await IsAvailableAsync())
            return;

        await Assert.ThrowsAsync<InvalidSourceQueryException>(() => RunAsync(Orders("id", "no_such_column")));
    }

    [Fact]
    public async Task Cancellation_Works()
    {
        if (!await IsAvailableAsync())
            return;

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(Orders("id"), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateProvider().ReadSchemaAsync(Config, cts.Token)
        );
    }

    [Fact]
    public async Task Secrets_AreNotExposedInErrors()
    {
        if (!await IsAvailableAsync())
            return;

        var wrong = Config with { Password = WrongPassword };

        var test = await CreateProvider().TestConnectionAsync(wrong, CancellationToken.None);
        var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
            CreateProvider().ExecuteAsync(wrong, Orders("id"), CancellationToken.None)
        );

        Assert.False(test.Success);
        Assert.DoesNotContain(WrongPassword, test.Error);
        Assert.DoesNotContain(WrongPassword, ex.ToString());
    }

    [Fact]
    public void Config_PassesTheProvidersOwnValidation()
    {
        var provider = CreateProvider();

        Assert.Null(provider.ValidateConfig(Config));
        Assert.False(string.IsNullOrWhiteSpace(provider.TargetHost(Config)));
        Assert.NotNull(provider.ValidateConfig(new DataSourceConfig { Type = Config.Type }));
    }

    [Fact]
    public void Capabilities_AreConsistent() =>
        // The import path runs SQL on the provider's own connection, so it can't exist without native SQL.
        Assert.True(!CreateProvider().Capabilities.Imports || CreateProvider().Capabilities.NativeSql);

    // ── Capability-dependent behavior: asserted both ways ──────────────────────

    [Fact]
    public async Task NativeSql_MatchesCapability()
    {
        if (!await IsAvailableAsync())
            return;

        var provider = CreateProvider();
        var native = new NativeSqlQuery("SELECT 1 AS one");

        if (provider.Capabilities.NativeSql)
            Assert.Equal(
                "1",
                (await provider.ExecuteNativeAsync(Config, native, CancellationToken.None)).Rows[0].Values[0]
            );
        else
            await Assert.ThrowsAsync<UnsupportedDataSourceException>(() =>
                provider.ExecuteNativeAsync(Config, native, CancellationToken.None)
            );
    }

    [Fact]
    public async Task TextMatchCase_MatchesCapability()
    {
        if (!await IsAvailableAsync())
            return;

        var result = await RunAsync(
            Orders("id") with
            {
                Conditions = [Where("note", QueryOperator.StartsWith, "RUS")],
            }
        );

        Assert.Equal(CreateProvider().Capabilities.CaseSensitiveTextMatch ? [] : ["100"], Ids(result));
    }

    [Fact]
    public async Task ConditionOnLeftJoinedTable_MatchesCapability()
    {
        if (!await IsAvailableAsync())
            return;

        var fk = await CustomerForeignKeyAsync();
        var query = Orders("id") with
        {
            Joins =
            [
                new QueryJoin
                {
                    Table = "export_customer",
                    Column = fk,
                    ParentColumn = "customer_id",
                    Type = QueryJoinType.Left,
                },
            ],
            Conditions =
            [
                new QueryCondition
                {
                    Table = "export_customer",
                    Column = "name",
                    Operator = QueryOperator.Equal,
                    Value = "Acme",
                },
            ],
        };

        if (CreateProvider().Capabilities.ConditionsOnLeftJoinedTables)
            Assert.Equal(["100", "101"], Ids(await RunAsync(query)));
        else
            await Assert.ThrowsAsync<InvalidSourceQueryException>(() => RunAsync(query));
    }

    [Fact]
    public async Task EmptyVersusNull_MatchesCapability()
    {
        if (!await IsAvailableAsync())
            return;

        // No fixture row stores an empty string, so only the "empty means null" side is observable here: a source
        // without NULL must still report its empty fields as null (see NullHandling_Works), never as "".
        var result = await RunAsync(Orders("id", "note"));

        Assert.DoesNotContain(result.ToDictionaries(), r => r["note"] == "");
        if (!CreateProvider().Capabilities.DistinguishesEmptyFromNull)
            Assert.Contains(result.ToDictionaries(), r => r["note"] is null);
    }

    private async Task<string> CustomerForeignKeyAsync()
    {
        var schema = await CreateProvider().ReadSchemaAsync(Config, CancellationToken.None);
        return schema
            .Tables.Single(t => t.Name == "export_order")
            .Columns.Single(c => c.Name == "customer_id")
            .ForeignKeyColumn!;
    }
}

/// <summary><see cref="DataSourceProviderContractTests"/> against the PostgreSQL test fixture.</summary>
public sealed class PostgreSqlProviderContractTests : DataSourceProviderContractTests
{
    protected override IDataSourceProvider CreateProvider() => new PostgreSqlDataSourceProvider();

    protected override DataSourceConfig Config => ErpTestFixture.Config;

    protected override Task<bool> IsAvailableAsync() => ErpTestFixture.IsAvailableAsync();
}

/// <summary><see cref="DataSourceProviderContractTests"/> against the MariaDB test fixture.</summary>
public sealed class MariaDbProviderContractTests : DataSourceProviderContractTests
{
    protected override IDataSourceProvider CreateProvider() => new MariaDbDataSourceProvider();

    protected override DataSourceConfig Config => MariaDbTestFixture.Config;

    protected override Task<bool> IsAvailableAsync() => MariaDbTestFixture.IsAvailableAsync();
}

/// <summary><see cref="DataSourceProviderContractTests"/> against the in-memory ServiceNow Table API.</summary>
public sealed class ServiceNowTableApiProviderContractTests : DataSourceProviderContractTests
{
    private readonly FakeServiceNow _instance = FakeServiceNow.WithExportFixture();

    protected override IDataSourceProvider CreateProvider() => _instance.CreateProvider();

    protected override DataSourceConfig Config => FakeServiceNow.Config;
}
