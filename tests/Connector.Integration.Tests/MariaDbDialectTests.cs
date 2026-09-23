using System.Text.Json;
using Connector.Infrastructure.DataSources;
using Connector.Infrastructure.DataSources.MariaDb;

namespace Connector.Integration.Tests;

/// <summary>Pure unit tests (no DB) pinning the exact MariaDB text <see cref="MariaDbDialect"/> renders. The
/// MariaDB-backed provider and parity tests run the same fragments for real.</summary>
public sealed class MariaDbDialectTests
{
    private static readonly ISqlDialect Dialect = MariaDbDialect.Instance;

    [Theory]
    [InlineData("status", "`status`")]
    [InlineData("Mixed Case", "`Mixed Case`")]
    [InlineData("x`; DROP TABLE t; --", "`x``; DROP TABLE t; --`")]
    public void QuoteIdentifier_BacktickQuotesAndEscapesEmbeddedBackticks(string identifier, string expected) =>
        Assert.Equal(expected, Dialect.QuoteIdentifier(identifier));

    [Fact]
    public void BuildParameterName_UsesAtPrefixedIndex() => Assert.Equal("@p12", Dialect.BuildParameterName(12));

    [Fact]
    public void BuildLimit_RendersLimitClause() => Assert.Equal("LIMIT 25", Dialect.BuildLimit(25));

    [Fact]
    public void QuoteStringLiteral_EscapesQuotesAndBackslashes() =>
        Assert.Equal(@"'it''s a \\ path'", Dialect.QuoteStringLiteral(@"it's a \ path"));

    [Fact]
    public void CastToText_UsesCastAsChar() => Assert.Equal("CAST(s.`id` AS CHAR)", Dialect.CastToText("s.`id`"));

    [Fact]
    public void BuildNullSafeEquals_UsesSpaceshipOperator() =>
        Assert.Equal("a <=> @p0", Dialect.BuildNullSafeEquals("a", "@p0"));

    [Fact]
    public void BuildMatchesAny_BindsOneParameterPerValue()
    {
        var parameters = new Dictionary<string, object?> { ["@p0"] = "taken" };

        var sql = Dialect.BuildMatchesAny("t.`k`", ["a", "b"], parameters);

        Assert.Equal("t.`k` IN (@p1, @p2)", sql);
        Assert.Equal("a", parameters["@p1"]);
        Assert.Equal("b", parameters["@p2"]);
    }

    [Fact]
    public void BuildStringAggregate_UsesGroupConcatWithSeparator() =>
        Assert.Equal("GROUP_CONCAT(CAST(r.`x` AS CHAR) SEPARATOR ', ')", Dialect.BuildStringAggregate("r.`x`", ", "));

    // Same JSON as PostgreSqlDialectTests expects for the equivalent PostgreSQL types.
    [Theory]
    [InlineData("42", "INT", "42")]
    [InlineData("18446744073709551615", "BIGINT UNSIGNED", "18446744073709551615")]
    [InlineData("12.50", "DECIMAL", "12.50")]
    [InlineData("1", "BOOL", "true")]
    [InlineData("0", "BOOL", "false")]
    [InlineData("2024-03-15", "DATE", "\"2024-03-15\"")]
    [InlineData("2024-03-15 10:11:12.345678", "DATETIME", "\"2024-03-15T10:11:12.345678\"")]
    [InlineData("{\"a\":1}", "JSON", "{\"a\":1}")]
    [InlineData("hello", "VARCHAR", "\"hello\"")]
    public void ConvertNativeTextToJson_FollowsPostgresToJsonRules(string text, string type, string expectedJson) =>
        Assert.Equal(expectedJson, JsonSerializer.Serialize(Dialect.ConvertNativeTextToJson(text, type)));
}
