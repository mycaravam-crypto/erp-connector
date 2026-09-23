using System.Text.Encodings.Web;
using System.Text.Json;
using Connector.Infrastructure.DataSources;
using Connector.Infrastructure.DataSources.PostgreSql;

namespace Connector.Integration.Tests;

/// <summary>
/// Pure unit tests (no DB) pinning the exact PostgreSQL text <see cref="PostgreSqlDialect"/> renders — the
/// fragments <c>DynamicExportService</c>, <c>ImportNodeWalker</c>, <c>ImportRunReleaser</c> and
/// <see cref="PostgreSqlQueryCompiler"/> used to emit inline before Arbeitsauftrag 5. The Postgres-backed
/// export/import tests cover the same fragments executing for real.
/// </summary>
public sealed class PostgreSqlDialectTests
{
    private static readonly ISqlDialect Dialect = PostgreSqlDialect.Instance;

    [Theory]
    [InlineData("status", "\"status\"")]
    [InlineData("Mixed Case", "\"Mixed Case\"")]
    [InlineData("x\"; DROP TABLE t; --", "\"x\"\"; DROP TABLE t; --\"")]
    public void QuoteIdentifier_DoubleQuotesAndEscapesEmbeddedQuotes(string identifier, string expected) =>
        Assert.Equal(expected, Dialect.QuoteIdentifier(identifier));

    [Fact]
    public void BuildParameterName_UsesAtPrefixedIndex()
    {
        Assert.Equal("@p0", Dialect.BuildParameterName(0));
        Assert.Equal("@p12", Dialect.BuildParameterName(12));
    }

    [Fact]
    public void BuildLimit_RendersLimitClause() => Assert.Equal("LIMIT 500001", Dialect.BuildLimit(500_001));

    [Fact]
    public void QuoteStringLiteral_DoublesSingleQuotes() => Assert.Equal("'it''s'", Dialect.QuoteStringLiteral("it's"));

    [Fact]
    public void CastToText_AppendsPostgresCast() => Assert.Equal("s.\"id\"::text", Dialect.CastToText("s.\"id\""));

    [Fact]
    public void BuildNullSafeEquals_UsesIsNotDistinctFrom() =>
        Assert.Equal("a IS NOT DISTINCT FROM @p0", Dialect.BuildNullSafeEquals("a", "@p0"));

    [Fact]
    public void BuildMatchesAny_ComparesAgainstArrayParameter() =>
        Assert.Equal("t.\"k\"::text = ANY(@p0)", Dialect.BuildMatchesAny("t.\"k\"::text", "@p0"));

    // Expected values are exactly what PostgreSQL's own json_build_object/to_json produced for these native
    // text values (captured against a real server before the JSON assembly moved to C#).
    [Theory]
    [InlineData("42", "integer", "42")]
    [InlineData("9007199254740993", "bigint", "9007199254740993")]
    [InlineData("12.50", "numeric(10, 2)", "12.50")]
    [InlineData("1.5e+20", "double precision", "1.5e+20")]
    [InlineData("NaN", "double precision", "\"NaN\"")]
    [InlineData("t", "boolean", "true")]
    [InlineData("f", "boolean", "false")]
    [InlineData("2024-03-15", "date", "\"2024-03-15\"")]
    [InlineData("2024-03-15 10:11:12.345678", "timestamp without time zone", "\"2024-03-15T10:11:12.345678\"")]
    [InlineData("2024-03-15 08:11:12+00", "timestamp with time zone", "\"2024-03-15T08:11:12+00:00\"")]
    [InlineData("2024-03-15 08:11:12+05:30", "timestamp with time zone", "\"2024-03-15T08:11:12+05:30\"")]
    [InlineData("infinity", "timestamp without time zone", "\"infinity\"")]
    [InlineData("{\"a\": [1, 2]}", "json", "{\"a\":[1,2]}")]
    [InlineData("{1,2,3}", "integer[]", "[1,2,3]")]
    [InlineData("{\"x y\",z,NULL,\"NULL\",\"a\\\"b\"}", "text[]", "[\"x y\",\"z\",null,\"NULL\",\"a\\\"b\"]")]
    [InlineData("{{1,2},{3,4}}", "integer[]", "[[1,2],[3,4]]")]
    [InlineData("{}", "integer[]", "[]")]
    [InlineData("1 day 02:00:00", "interval", "\"1 day 02:00:00\"")]
    [InlineData("ab   ", "character(5)", "\"ab   \"")]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "uuid", "\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa\"")]
    public void ConvertNativeTextToJson_MatchesPostgresJsonEncoding(string text, string type, string expectedJson) =>
        Assert.Equal(expectedJson, Dialect.ConvertNativeTextToJson(text, type)!.ToJsonString(Relaxed));

    private static readonly JsonSerializerOptions Relaxed = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public void BuildStringAggregate_CastsToTextAndQuotesDelimiter() =>
        Assert.Equal("string_agg(t.\"c\"::text, ', ''')", Dialect.BuildStringAggregate("t.\"c\"", ", '"));
}
