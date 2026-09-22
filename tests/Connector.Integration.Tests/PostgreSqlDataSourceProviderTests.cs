using System.Text.Json.Nodes;
using Connector.Core.DataSources;
using Connector.Infrastructure;

namespace Connector.Integration.Tests;

/// <summary>
/// Real-Postgres coverage for <see cref="PostgreSqlDataSourceProvider"/> — Arbeitsauftrag 2's explicit test
/// list items "Connection Test", "Schema Reading", and "Query Execution". Requires the local test fixture
/// (<c>docker-compose --profile test up -d testdb</c>; see testdb/init.sql); every test no-ops rather than
/// failing if it isn't running, matching every other Postgres-backed test in this project (this repo's xunit
/// version, 2.9.2, predates <c>Assert.Skip</c>).
/// </summary>
public sealed class PostgreSqlDataSourceProviderTests
{
    private static readonly PostgreSqlDataSourceProvider Provider = new();

    private const string AcmeItemId = "11111111-1111-1111-1111-111111111111"; // masterdata row → Acme
    private const string AcmeManufacturerId = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"; // has 2 addresses

    // ── BuildConnectionString ────────────────────────────────────────────────
    // Pure unit tests, no DB required — moved here from DynamicExportServiceTests since BuildConnectionString
    // itself now lives on PostgreSqlDataSourceProvider rather than on DynamicExportService.

    // Security-review finding SR-02: a connection-string-injection payload smuggled through Password (or
    // any other field) must never be able to append/override keys like Host in the string Npgsql actually
    // parses. NpgsqlConnectionStringBuilder treats the whole value as the literal password, not as syntax.
    [Fact]
    public void BuildConnectionString_PasswordWithInjectionPayload_DoesNotOverrideHost()
    {
        var cfg = new DataSourceConfig
        {
            Host = "trusted-host.example",
            Port = 5432,
            Database = "erp",
            Username = "erp_user",
            Password = "s3cret;Host=evil.example;Port=1234",
        };

        var connectionString = PostgreSqlDataSourceProvider.BuildConnectionString(cfg);
        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);

        Assert.Equal("trusted-host.example", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("s3cret;Host=evil.example;Port=1234", parsed.Password);
    }

    [Fact]
    public void BuildConnectionString_UsernameWithInjectionPayload_DoesNotOverrideDatabase()
    {
        var cfg = new DataSourceConfig
        {
            Host = "trusted-host.example",
            Port = 5432,
            Database = "erp",
            Username = "erp_user;Database=other_db",
            Password = "pw",
        };

        var connectionString = PostgreSqlDataSourceProvider.BuildConnectionString(cfg);
        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);

        Assert.Equal("erp", parsed.Database);
        Assert.Equal("erp_user;Database=other_db", parsed.Username);
    }

    // Security-review finding SR-03: SslMode was previously hardcoded to Prefer everywhere. This proves
    // an explicit choice is actually honored, and that an unset/unrecognized value falls back to the
    // prior Prefer default rather than throwing (so a config saved before this field existed, or corrupted
    // input, can never itself turn a working connection into a hard failure).
    [Theory]
    [InlineData("Require", Npgsql.SslMode.Require)]
    [InlineData("VerifyFull", Npgsql.SslMode.VerifyFull)]
    [InlineData("verifyfull", Npgsql.SslMode.VerifyFull)]
    [InlineData(null, Npgsql.SslMode.Prefer)]
    [InlineData("", Npgsql.SslMode.Prefer)]
    [InlineData("not-a-real-mode", Npgsql.SslMode.Prefer)]
    public void BuildConnectionString_SslMode_HonorsExplicitChoiceOrFallsBackToPrefer(
        string? sslMode,
        Npgsql.SslMode expected
    )
    {
        var cfg = new DataSourceConfig
        {
            Host = "host.example",
            Port = 5432,
            Database = "erp",
            Username = "user",
            Password = "pw",
            SslMode = sslMode,
        };

        var connectionString = PostgreSqlDataSourceProvider.BuildConnectionString(cfg);
        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);

        Assert.Equal(expected, parsed.SslMode);
    }

    // ── TestConnectionAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task TestConnectionAsync_ValidConfig_ReturnsSuccessWithSchema()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        var result = await Provider.TestConnectionAsync(ErpTestFixture.Config, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.NotNull(result.Schema);
        Assert.Contains(result.Schema!.Tables, t => t.Name == "masterdata");
    }

    [Fact]
    public async Task TestConnectionAsync_WrongPassword_ReturnsFailureWithSanitizedError()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        var badConfig = ErpTestFixture.Config with { Password = "definitely-wrong-password" };

        var result = await Provider.TestConnectionAsync(badConfig, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Schema);
        Assert.NotNull(result.Error);
        // ErrorSanitizer.Detail must have scrubbed the credential out of whatever Npgsql echoed back.
        Assert.DoesNotContain("definitely-wrong-password", result.Error);
    }

    // ── ReadSchemaAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadSchemaAsync_ReturnsTablesWithPrimaryAndForeignKeys()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        var schema = await Provider.ReadSchemaAsync(ErpTestFixture.Config, CancellationToken.None);

        Assert.Equal(
            $"{ErpTestFixture.Config.Host}:{ErpTestFixture.Config.Port}/{ErpTestFixture.Config.Database}",
            schema.ConnectionLabel
        );

        var masterdata = Assert.Single(schema.Tables, t => t.Name == "masterdata");
        Assert.Contains(masterdata.Columns, c => c.Name == "id" && c.PrimaryKey);

        var systemconfiguration = Assert.Single(schema.Tables, t => t.Name == "systemconfiguration");
        var articleId = Assert.Single(systemconfiguration.Columns, c => c.Name == "article_id");
        Assert.Equal("masterdata", articleId.ForeignKeyTable);
        Assert.Equal("id", articleId.ForeignKeyColumn);

        // status_upper is `GENERATED ALWAYS AS (upper(status)) STORED` — exercises the IsGenerated mapping.
        var statusUpper = Assert.Single(systemconfiguration.Columns, c => c.Name == "status_upper");
        Assert.True(statusUpper.IsGenerated);
    }

    // ── ExecuteAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_FlatSelectWithParameter_ReturnsStringKeyedRow()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        var query = new SourceQuery(
            "SELECT id, article_name, manufacturer_id FROM masterdata WHERE id = @id::uuid",
            new Dictionary<string, object?> { ["id"] = AcmeItemId }
        );

        var result = await Provider.ExecuteAsync(ErpTestFixture.Config, query, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal("Compressor Unit CU-200", row["article_name"]);
        Assert.Equal(AcmeManufacturerId, row["manufacturer_id"]);
    }

    [Fact]
    public async Task ExecuteAsync_NullColumn_ReturnsNullNotEmptyString()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        // manufacturer has no `country`-typed nullable column of its own worth targeting; masterdata's own
        // `manufacturer_id` is never null in the fixture, so use manufacturer_address's optional-looking
        // columns instead — none are declared NOT NULL, and address_type is always populated in the seed, so
        // assert against a genuinely nullable/unpopulated projection instead: a literal SQL NULL column.
        var query = new SourceQuery(
            "SELECT NULL::text AS maybe_null, id FROM masterdata WHERE id = @id::uuid",
            new Dictionary<string, object?> { ["id"] = AcmeItemId }
        );

        var result = await Provider.ExecuteAsync(ErpTestFixture.Config, query, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Null(row["maybe_null"]);
    }

    [Fact]
    public async Task ExecuteAsync_DateColumn_CoercesToIso8601()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        var query = new SourceQuery(
            "SELECT commission_date FROM systemconfiguration WHERE id = '44444444-4444-4444-4444-444444444444'::uuid"
        );

        var result = await Provider.ExecuteAsync(ErpTestFixture.Config, query, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal("2024-03-15", row["commission_date"]);
    }

    [Fact]
    public async Task ExecuteAsync_JsonBuildObjectAggregation_ReturnsRawJsonTextColumn()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        var query = new SourceQuery(
            "SELECT json_build_object('id', id, 'name', article_name) AS row_json FROM masterdata WHERE id = @id::uuid",
            new Dictionary<string, object?> { ["id"] = AcmeItemId }
        );

        var result = await Provider.ExecuteAsync(ErpTestFixture.Config, query, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        var json = (JsonObject)JsonNode.Parse(row["row_json"]!)!;
        Assert.Equal("Compressor Unit CU-200", json["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task ExecuteAsync_CardinalityViolation_ThrowsDataSourceQueryExceptionWithSqlState21000()
    {
        if (!await ErpTestFixture.IsAvailableAsync())
            return;

        // Manufacturer AAAAAAAA has two addresses (testdb/init.sql) — a bare (non-json_agg) correlated
        // subquery expecting at most one row fails with Postgres SQLSTATE 21000 ("more than one row returned
        // by a subquery used as an expression"), the same guard DynamicExportService relies on for its
        // "object vs. array" nested-group cardinality check.
        var query = new SourceQuery(
            "SELECT (SELECT city FROM manufacturer_address WHERE manufacturer_id = m.id) AS city "
                + "FROM manufacturer m WHERE m.id = @id::uuid",
            new Dictionary<string, object?> { ["id"] = AcmeManufacturerId }
        );

        var ex = await Assert.ThrowsAsync<DataSourceQueryException>(() =>
            Provider.ExecuteAsync(ErpTestFixture.Config, query, CancellationToken.None)
        );

        Assert.Equal("21000", ex.ErrorCode);
    }
}
