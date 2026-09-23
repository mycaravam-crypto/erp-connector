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
    public void BuildJsonObject_QuotesKeysAsLiteralsInOrder() =>
        Assert.Equal(
            "json_build_object('id', s.\"id\", 'o''brien', x)",
            Dialect.BuildJsonObject([("id", "s.\"id\""), ("o'brien", "x")])
        );

    [Fact]
    public void BuildJsonArrayAggregate_DefaultsToEmptyArray() =>
        Assert.Equal("COALESCE(json_agg(obj), '[]'::json)", Dialect.BuildJsonArrayAggregate("obj"));

    [Fact]
    public void BuildStringAggregate_CastsToTextAndQuotesDelimiter() =>
        Assert.Equal("string_agg(t.\"c\"::text, ', ''')", Dialect.BuildStringAggregate("t.\"c\"", ", '"));
}
