using System.Globalization;

namespace Connector.Infrastructure.DataSources.PostgreSql;

/// <summary>
/// <see cref="ISqlDialect"/> for PostgreSQL — the single home of every PostgreSQL-specific fragment the
/// generic query builders emit: double-quote identifier quoting, <c>@pN</c> parameter placeholders (Npgsql's
/// named-parameter syntax), <c>LIMIT n</c>, <c>::text</c> casts, <c>IS NOT DISTINCT FROM</c>, and the
/// <c>json_build_object</c>/<c>json_agg</c>/<c>string_agg</c> functions. Stateless; use <see cref="Instance"/>.
/// </summary>
public sealed class PostgreSqlDialect : ISqlDialect
{
    public static readonly PostgreSqlDialect Instance = new();

    private PostgreSqlDialect() { }

    // Wraps in double quotes and doubles any embedded double quote, so no identifier can end the quoted
    // name early and inject syntax.
    public string QuoteIdentifier(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    public string BuildParameterName(int index) => "@p" + index.ToString(CultureInfo.InvariantCulture);

    public string BuildLimit(int limit) => "LIMIT " + limit.ToString(CultureInfo.InvariantCulture);

    // Standard single-quote doubling — the same escaping the export builders always used (formerly
    // DynamicExportService.SqlLit). Relies on Postgres's default standard_conforming_strings = on, under which
    // a backslash inside '...' is an ordinary character.
    public string QuoteStringLiteral(string value) => "'" + value.Replace("'", "''") + "'";

    public string CastToText(string expression) => expression + "::text";

    public string BuildNullSafeEquals(string left, string right) => $"{left} IS NOT DISTINCT FROM {right}";

    public string BuildJsonObject(IEnumerable<(string Key, string ValueExpression)> members) =>
        $"json_build_object({string.Join(", ", members.Select(m => $"{QuoteStringLiteral(m.Key)}, {m.ValueExpression}"))})";

    // json_agg() over zero rows returns SQL NULL, not '[]' — the COALESCE keeps "no related rows" an empty
    // JSON array.
    public string BuildJsonArrayAggregate(string elementExpression) =>
        $"COALESCE(json_agg({elementExpression}), '[]'::json)";

    public string BuildStringAggregate(string expression, string delimiter) =>
        $"string_agg({CastToText(expression)}, {QuoteStringLiteral(delimiter)})";
}
