using Connector.Core.DataSources;
using Connector.Infrastructure.DataSources.MariaDb;
using MySqlConnector;

namespace Connector.Integration.Tests;

/// <summary>
/// Real-MariaDB coverage for <see cref="MariaDbDataSourceProvider"/> (Arbeitsauftrag 7): connection test, schema
/// read (tables, columns, primary and foreign keys), neutral queries (SELECT/WHERE/JOIN/LIMIT, bound
/// parameters), native SQL, cancellation and command timeout. Uses the <c>export_*</c> tables from
/// testdb/mariadb-init.sql; every DB-backed test no-ops if the MariaDB fixture isn't running.
/// </summary>
public sealed class MariaDbDataSourceProviderTests
{
    private static readonly MariaDbDataSourceProvider Provider = new();

    // ── MariaDbConnectionFactory (no DB) ─────────────────────────────────────

    [Fact]
    public void BuildConnectionString_InjectionPayloadInPassword_DoesNotOverrideServer()
    {
        var parsed = new MySqlConnectionStringBuilder(
            MariaDbConnectionFactory.BuildConnectionString(
                MariaDbTestFixture.Config with
                {
                    Host = "trusted-host.example",
                    Password = "s3cret;Server=evil.example;Port=1234",
                }
            )
        );

        Assert.Equal("trusted-host.example", parsed.Server);
        Assert.Equal(3306u, parsed.Port);
        Assert.Equal("s3cret;Server=evil.example;Port=1234", parsed.Password);
    }

    [Theory]
    [InlineData("Disable", MySqlSslMode.None)]
    [InlineData("Require", MySqlSslMode.Required)]
    [InlineData("VerifyCA", MySqlSslMode.VerifyCA)]
    [InlineData("verifyfull", MySqlSslMode.VerifyFull)]
    [InlineData("Prefer", MySqlSslMode.Preferred)]
    [InlineData(null, MySqlSslMode.Preferred)]
    public void BuildConnectionString_MapsSharedSslModeNames(string? sslMode, MySqlSslMode expected) =>
        Assert.Equal(
            expected,
            new MySqlConnectionStringBuilder(
                MariaDbConnectionFactory.BuildConnectionString(MariaDbTestFixture.Config with { SslMode = sslMode })
            ).SslMode
        );

    [Fact]
    public void BuildConnectionString_UnsetPort_DefaultsTo3306() =>
        Assert.Equal(
            3306u,
            new MySqlConnectionStringBuilder(
                MariaDbConnectionFactory.BuildConnectionString(MariaDbTestFixture.Config with { Port = null })
            ).Port
        );

    // ── Connection test / schema ─────────────────────────────────────────────

    [Fact]
    public async Task TestConnectionAsync_ValidCredentials_ReturnsSchema()
    {
        if (!await MariaDbTestFixture.IsAvailableAsync())
            return;

        var result = await Provider.TestConnectionAsync(MariaDbTestFixture.Config, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Contains(result.Schema!.Tables, t => t.Name == "export_order");
    }

    [Fact]
    public async Task TestConnectionAsync_InvalidCredentials_FailsWithoutLeakingPassword()
    {
        if (!await MariaDbTestFixture.IsAvailableAsync())
            return;

        var result = await Provider.TestConnectionAsync(
            MariaDbTestFixture.Config with
            {
                Password = "wrong-pw-do-not-echo",
            },
            CancellationToken.None
        );

        Assert.False(result.Success);
        Assert.Contains("Access denied", result.Error);
        Assert.DoesNotContain("wrong-pw-do-not-echo", result.Error);
    }

    [Fact]
    public async Task ReadSchemaAsync_ReportsColumnsPrimaryAndForeignKeys()
    {
        if (!await MariaDbTestFixture.IsAvailableAsync())
            return;

        var schema = await Provider.ReadSchemaAsync(MariaDbTestFixture.Config, CancellationToken.None);

        var order = schema.Tables.Single(t => t.Name == "export_order");
        Assert.Equal(["id", "customer_id", "placed_on", "total", "note"], order.Columns.Select(c => c.Name));

        var id = order.Columns.Single(c => c.Name == "id");
        Assert.True(id.PrimaryKey);
        Assert.False(id.Nullable);
        Assert.Equal("int", id.Type);

        var customerId = order.Columns.Single(c => c.Name == "customer_id");
        Assert.False(customerId.PrimaryKey);
        Assert.True(customerId.Nullable);
        Assert.Equal("export_customer", customerId.ForeignKeyTable);
        Assert.Equal("id", customerId.ForeignKeyColumn);

        Assert.Null(order.Columns.Single(c => c.Name == "note").ForeignKeyTable);
    }

    // ── Neutral queries ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_JoinWhereAndLimit_WithBoundParameters()
    {
        if (!await MariaDbTestFixture.IsAvailableAsync())
            return;

        var result = await Provider.ExecuteAsync(
            MariaDbTestFixture.Config,
            new SourceQuery
            {
                RootTable = "export_order",
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
                        Column = "id",
                        ParentColumn = "customer_id",
                    },
                ],
                Conditions =
                [
                    new QueryCondition
                    {
                        Column = "placed_on",
                        Operator = QueryOperator.GreaterThanOrEqual,
                        Value = new DateOnly(2024, 2, 1),
                    },
                    // A value that would be an injection if it were ever spliced into the SQL text.
                    new QueryCondition
                    {
                        Table = "export_customer",
                        Column = "name",
                        Operator = QueryOperator.In,
                        Values = ["Acme", "x' OR '1'='1"],
                    },
                ],
                Limit = 10,
            },
            CancellationToken.None
        );

        var row = Assert.Single(result.ToDictionaries());
        Assert.Equal("101", row["id"]);
        Assert.Equal("Acme", row["customer"]);
    }

    [Fact]
    public async Task ExecuteAsync_StartsWith_IsCaseSensitiveAndLiteral()
    {
        if (!await MariaDbTestFixture.IsAvailableAsync())
            return;

        async Task<int> CountAsync(string prefix) => (
                await Provider.ExecuteAsync(
                    MariaDbTestFixture.Config,
                    new SourceQuery
                    {
                        RootTable = "export_order_line",
                        Conditions =
                        [
                            new QueryCondition
                            {
                                Column = "sku",
                                Operator = QueryOperator.StartsWith,
                                Value = prefix,
                            },
                        ],
                    },
                    CancellationToken.None
                )
            ).Rows.Count;

        Assert.Equal(1, await CountAsync("A-"));
        Assert.Equal(0, await CountAsync("a-"));
        Assert.Equal(0, await CountAsync("%"));
    }

    [Fact]
    public async Task ExecuteAsync_LimitCapsRows()
    {
        if (!await MariaDbTestFixture.IsAvailableAsync())
            return;

        var result = await Provider.ExecuteAsync(
            MariaDbTestFixture.Config,
            new SourceQuery { RootTable = "export_order", Limit = 2 },
            CancellationToken.None
        );

        Assert.Equal(2, result.Rows.Count);
    }

    [Fact]
    public async Task ExecuteAsync_MissingTable_IsRejectedBeforeExecution()
    {
        if (!await MariaDbTestFixture.IsAvailableAsync())
            return;

        await Assert.ThrowsAsync<InvalidSourceQueryException>(() =>
            Provider.ExecuteAsync(
                MariaDbTestFixture.Config,
                new SourceQuery { RootTable = "no_such_table" },
                CancellationToken.None
            )
        );
    }

    // ── Native SQL ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteNativeAsync_MissingTable_ThrowsDataSourceQueryExceptionWithSqlState()
    {
        if (!await MariaDbTestFixture.IsAvailableAsync())
            return;

        var ex = await Assert.ThrowsAsync<DataSourceQueryException>(() =>
            Provider.ExecuteNativeAsync(
                MariaDbTestFixture.Config,
                new NativeSqlQuery("SELECT * FROM `no_such_table`"),
                CancellationToken.None
            )
        );
        Assert.Equal("42S02", ex.ErrorCode);
    }

    [Fact]
    public async Task ExecuteNativeAsync_DefaultFormatting_DatesAsIsoAndNullsAsNull()
    {
        if (!await MariaDbTestFixture.IsAvailableAsync())
            return;

        var result = await Provider.ExecuteNativeAsync(
            MariaDbTestFixture.Config,
            new NativeSqlQuery(
                "SELECT placed_on, note FROM export_order WHERE id = @id",
                new Dictionary<string, object?> { ["@id"] = 101 }
            ),
            CancellationToken.None
        );

        var row = Assert.Single(result.ToDictionaries());
        Assert.Equal("2024-02-10", row["placed_on"]);
        Assert.Null(row["note"]);
    }

    [Fact]
    public async Task ExecuteNativeAsync_CommandTimeout_Aborts()
    {
        if (!await MariaDbTestFixture.IsAvailableAsync())
            return;

        await Assert.ThrowsAnyAsync<Exception>(() =>
            Provider.ExecuteNativeAsync(
                MariaDbTestFixture.Config,
                new NativeSqlQuery("SELECT SLEEP(5)", CommandTimeoutSeconds: 1),
                CancellationToken.None
            )
        );
    }

    [Fact]
    public async Task ExecuteNativeAsync_Cancellation_Aborts()
    {
        if (!await MariaDbTestFixture.IsAvailableAsync())
            return;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var started = DateTime.UtcNow;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Provider.ExecuteNativeAsync(MariaDbTestFixture.Config, new NativeSqlQuery("SELECT SLEEP(5)"), cts.Token)
        );
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(4));
    }
}
