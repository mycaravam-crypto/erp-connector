using System.Text.Json;
using Connector.Core.DataSources;

namespace Connector.Core.Tests;

/// <summary>
/// Coverage for <see cref="DataSourceConfig"/>'s Arbeitsauftrag 3 back-compat contract — see
/// knowledge/architecture/data-source-configuration.md. Pure JSON/model tests: no database, no HTTP, no
/// provider. <c>AppSetting.Value</c> (the only place a <see cref="DataSourceConfig"/> is actually persisted,
/// see <c>Connector.Infrastructure.AppSettingsStore</c>) is a schemaless JSON blob, so "migration" for this
/// record's shape *is* System.Text.Json deserializing an older/narrower payload against the current type —
/// exactly what these tests exercise directly, without needing a database round-trip.
/// </summary>
public sealed class DataSourceConfigTests
{
    // ── Existing PostgreSQL configuration without Type ──────────────────────────────────

    // The exact shape every AppSettings row persisted before Arbeitsauftrag 2 introduced `Type` at all —
    // no "type" key, and (since Arbeitsauftrag 3 renamed the property casing/shape but not the JSON contract)
    // still no "instanceUrl"/"hasPassword" keys either.
    private const string LegacyJsonWithoutType = """
        {"Host":"legacy-host","Port":5432,"Database":"legacy_db","Username":"legacy_user","Password":"legacy_pw"}
        """;

    [Fact]
    public void LegacyJsonWithoutType_DeserializesAsPostgreSql()
    {
        var config = JsonSerializer.Deserialize<DataSourceConfig>(LegacyJsonWithoutType);

        Assert.NotNull(config);
        Assert.Equal(DataSourceType.PostgreSql, config!.Type);
        Assert.Equal("legacy-host", config.Host);
        Assert.Equal(5432, config.Port);
        Assert.Equal("legacy_db", config.Database);
        Assert.Equal("legacy_user", config.Username);
        Assert.Equal("legacy_pw", config.Password);
        Assert.Null(config.InstanceUrl);
        Assert.Null(config.SslMode);
    }

    // Arbeitsauftrag 2's own back-compat case (Type field added, SslMode not yet) must keep working under
    // Arbeitsauftrag 3's generalized shape too.
    [Fact]
    public void JsonWithTypeButWithoutSslModeOrInstanceUrl_DeserializesCorrectly()
    {
        const string json = """
            {"Type":0,"Host":"h","Port":5432,"Database":"d","Username":"u","Password":"p"}
            """;

        var config = JsonSerializer.Deserialize<DataSourceConfig>(json);

        Assert.NotNull(config);
        Assert.Equal(DataSourceType.PostgreSql, config!.Type);
        Assert.Null(config.SslMode);
        Assert.Null(config.InstanceUrl);
    }

    // ── New PostgreSQL configuration with Type ──────────────────────────────────────────

    [Fact]
    public void NewPostgreSqlConfigWithType_RoundTripsThroughJson()
    {
        var original = new DataSourceConfig
        {
            Type = DataSourceType.PostgreSql,
            Host = "erp.example",
            Port = 5432,
            Database = "erp",
            Username = "reader",
            Password = "s3cret",
            SslMode = "Require",
        };

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<DataSourceConfig>(json);

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void ExplicitTypeDefault_MatchesOmittedType()
    {
        // Type is a value type defaulting to its enum's 0 value either way — explicitly setting
        // PostgreSql must be indistinguishable from a legacy payload that never mentioned Type at all.
        var explicitConfig = new DataSourceConfig { Type = DataSourceType.PostgreSql, Username = "u" };
        var implicitConfig = new DataSourceConfig { Username = "u" };

        Assert.Equal(explicitConfig, implicitConfig);
    }

    // ── Serialization without password leak ─────────────────────────────────────────────

    [Fact]
    public void ToString_NeverContainsThePasswordValue()
    {
        var config = new DataSourceConfig
        {
            Host = "h",
            Port = 5432,
            Database = "d",
            Username = "u",
            Password = "super-secret-value",
        };

        var text = config.ToString();

        Assert.DoesNotContain("super-secret-value", text);
        Assert.Contains("HasPassword = True", text);
    }

    [Fact]
    public void ToString_NoPassword_ReportsHasPasswordFalse()
    {
        var config = new DataSourceConfig
        {
            Host = "h",
            Port = 5432,
            Database = "d",
            Username = "u",
        };

        Assert.Contains("HasPassword = False", config.ToString());
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("x", true)]
    public void HasPassword_ReflectsWhetherAPasswordIsSet(string? password, bool expected)
    {
        var config = new DataSourceConfig { Username = "u", Password = password ?? "" };

        Assert.Equal(expected, config.HasPassword);
    }

    // ── Invalid combinations ─────────────────────────────────────────────────────────────
    // Each provider's IDataSourceProvider.ValidateConfig owns full required-field validation for its
    // DataSourceType — covered in DataSourceConfigValidationTests. This project has no
    // reference to Connector.Api, so this level only covers what the model itself can express/deserialize:
    // an "invalid combination" (e.g. PostgreSql with a null Host) deserializes just fine — DataSourceConfig
    // is a plain data holder, not a self-validating type — which is exactly why that validation exists as a
    // separate, explicit step rather than being smuggled into the model's constructor.
    [Fact]
    public void RelationalTypeWithoutHost_DeserializesButIsIncomplete()
    {
        const string json = """{"Type":0,"Database":"d","Username":"u"}""";

        var config = JsonSerializer.Deserialize<DataSourceConfig>(json);

        Assert.NotNull(config);
        Assert.Null(config!.Host);
        Assert.Null(config.Port);
    }

    // ── Unknown data source type ─────────────────────────────────────────────────────────

    [Fact]
    public void JsonWithOutOfRangeType_DeserializesTheRawNumberRatherThanThrowing()
    {
        // System.Text.Json doesn't validate enum values against defined members by default — an
        // out-of-range Type must still deserialize (so a payload from a newer/foreign version of this
        // service doesn't hard-fail parsing), leaving DataSourceProviderResolver.Resolve
        // (DataSourceProviderResolverTests.Resolve_OutOfRangeType_ThrowsUnsupportedDataSourceException) and
        // POST /api/connection (which resolves the provider first) as the actual rejection points.
        const string json = """{"Type":999,"Username":"u"}""";

        var config = JsonSerializer.Deserialize<DataSourceConfig>(json);

        Assert.NotNull(config);
        Assert.Equal((DataSourceType)999, config!.Type);
    }
}
